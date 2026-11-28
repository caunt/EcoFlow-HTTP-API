using Microsoft.Extensions.Hosting;

using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;

namespace EcoFlow.Mqtt.Api.History;

// Keeps every change of every device value in memory. Only changes are stored, in compressed blocks, and the oldest
// blocks are dropped once the history is older or bigger than allowed.
//
// All of it belongs to one loop that runs the work it is sent, one piece at a time, so nothing here is shared between
// threads and nothing needs a lock. Recording never waits for it either: compressing a block only delays later work.
internal sealed class DeviceHistory(TimeSpan maximumAge, long maximumSize, string ignoredProperty) : BackgroundService
{
    // The open block is not compressed yet, so it is kept to a fraction of the allowed size.
    private const int BlocksPerMaximumSize = 8;
    private const int MaximumBlockLength = 256 * 1024;
    private const int MinimumBlockLength = 4 * 1024;

    private readonly Channel<Action> _work = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions
    {
        SingleReader = true
    });

    // Every value ever seen gets a field id. These lists are indexed by it.
    private readonly List<int> _generations = [];
    private readonly List<string> _paths = [];
    private readonly List<HistoryValue> _values = [];

    // The blocks, oldest first, and the one still being written.
    private readonly List<HistoryBlock> _blocks = [];
    private readonly Dictionary<string, DeviceFields> _devices = [];
    private readonly List<char> _path = [];
    private readonly HistoryBlockWriter _writer = new();

    private long _blocksSize;
    private long _time;

    public bool IsEnabled => maximumAge > TimeSpan.Zero && maximumSize > 0;

    private long MaximumAge => long.CreateSaturating(maximumAge.TotalMilliseconds);
    private long Size => _blocksSize + _writer.Size;

    public Task<HistoryResult> FindAsync(string serialNumber, HistoryQuery query, DateTimeOffset now)
    {
        return RunAsync(() => Find(serialNumber, query, now.ToUnixTimeMilliseconds()));
    }

    public Task<HistoryStatistics> GetStatisticsAsync(DateTimeOffset now)
    {
        return RunAsync(() => GetStatistics(now.ToUnixTimeMilliseconds()));
    }

    // Takes the whole state of a device as JSON and stores what differs from the state it was given before.
    public void Record(string serialNumber, ReadOnlyMemory<byte> state, DateTimeOffset timestamp)
    {
        if (IsEnabled)
            Post(() => Record(serialNumber, state, timestamp.ToUnixTimeMilliseconds()));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (Action work in _work.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(continueOnCapturedContext: false))
        {
            try
            {
                work();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The history is an extra: a failure in it is reported and must never stop the API.
                await Console.Error
                    .WriteLineAsync($"⚠️ Error recording history: {exception.Message}".AsMemory(), stoppingToken)
                    .ConfigureAwait(continueOnCapturedContext: false);
            }
        }
    }

    private void Evict()
    {
        while (_blocks.Count > 0)
        {
            // A block is still needed for as long as it holds the last known values at the start of the allowed age.
            long nextStartTime = _blocks.Count > 1 ? _blocks[index: 1].StartTime : _writer.StartTime;

            if (nextStartTime > _time - MaximumAge && Size <= maximumSize)
                break;

            _blocksSize -= _blocks[index: 0].Size;

            _blocks.RemoveAt(index: 0);
        }
    }

    private HistoryResult Find(string serialNumber, HistoryQuery query, long now)
    {
        _time = Math.Max(_time, now);

        Evict();

        int[] indexes = new int[_paths.Count];
        List<string> paths = [];

        Array.Fill(indexes, value: -1);

        foreach (int fieldId in _devices.TryGetValue(serialNumber, out DeviceFields? device) ? device.Fields : [])
        {
            if (!query.Selects(_paths[fieldId]))
                continue;

            indexes[fieldId] = paths.Count;

            paths.Add(_paths[fieldId]);
        }

        ImmutableArray<HistoryBlock> blocks = _writer.IsStarted ? [.. _blocks, _writer.ToBlock()] : [];

        // Nothing older than the allowed age is returned, even while its block is still held.
        long oldestTime = Math.Max(_time - MaximumAge, blocks.IsEmpty ? _time : blocks[index: 0].StartTime);
        long fromTime = Math.Max(query.From?.ToUnixTimeMilliseconds() ?? oldestTime, oldestTime);
        long toTime = Math.Min(query.To?.ToUnixTimeMilliseconds() ?? _time, _time);

        return new HistoryResult(paths: [.. paths], indexes: [.. indexes], blocks, fromTime, toTime, query.IsFlat);
    }

    private HistoryStatistics GetStatistics(long now)
    {
        _time = Math.Max(_time, now);

        Evict();

        long oldestTime = _blocks.Count > 0 ? _blocks[index: 0].StartTime : _writer.StartTime;
        double? fromSeconds = _writer.IsStarted ? Math.Max(oldestTime, _time - MaximumAge) / 1000.0 : null;

        return new HistoryStatistics(IsEnabled,
            maximumAge.TotalSeconds,
            maximumSize,
            Size,
            _blocks.Count + (_writer.IsStarted ? 1 : 0),
            _blocks.Sum(static block => long.CreateChecked(block.ChangeCount)) + _writer.ChangeCount,
            _paths.Count,
            fromSeconds);
    }

    private void Post(Action work)
    {
        if (!_work.Writer.TryWrite(work))
            throw new InvalidOperationException(message: "The history is stopped.");
    }

    private void Record(string serialNumber, ReadOnlyMemory<byte> state, long time)
    {
        // Times never go backwards, even if the system clock does.
        _time = Math.Max(_time, time);

        if (!_devices.TryGetValue(serialNumber, out DeviceFields? device))
        {
            device = new DeviceFields();

            _devices.Add(serialNumber, device);
        }

        if (!_writer.IsStarted)
            _writer.Start(_time, CollectionsMarshal.AsSpan(_values));

        device.Generation++;

        _path.Clear();

        using (JsonDocument document = JsonDocument.Parse(state))
            RecordElement(device, document.RootElement);

        // Values that are no longer part of the state, like items of an array that became shorter.
        foreach (int fieldId in device.Fields
            .Where(fieldId => _generations[fieldId] != device.Generation && _values[fieldId].Kind is not HistoryValueKind.Missing))
            RecordChange(fieldId, value: default);

        if (_writer.ChangesLength >= Math.Clamp(maximumSize / BlocksPerMaximumSize, MinimumBlockLength, MaximumBlockLength))
            Seal();

        Evict();
    }

    private void RecordChange(int fieldId, HistoryValue value)
    {
        _writer.Append(_time, fieldId, value);

        _values[fieldId] = value;
    }

    private void RecordElement(DeviceFields device, JsonElement element)
    {
        int length = _path.Count;

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                {
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        // Written by the API itself on every update, it is not a device value.
                        if (length is 0 && property.NameEquals(ignoredProperty))
                            continue;

                        if (length > 0)
                            _path.Add(item: '.');

                        _path.AddRange(property.Name.AsSpan());
                        RecordElement(device, property.Value);
                        CollectionsMarshal.SetCount(_path, length);
                    }

                    break;
                }
            case JsonValueKind.Array:
                {
                    int index = 0;

                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        _path.AddRange($"[{index++}]".AsSpan());
                        RecordElement(device, item);
                        CollectionsMarshal.SetCount(_path, length);
                    }

                    break;
                }
            default:
                {
                    RecordValue(device, HistoryValue.From(element));

                    break;
                }
        }
    }

    private void RecordValue(DeviceFields device, HistoryValue value)
    {
        ReadOnlySpan<char> path = CollectionsMarshal.AsSpan(_path);

        if (!device.FieldIds.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(path, out int fieldId))
        {
            fieldId = _paths.Count;

            device.FieldIds.Add(path.ToString(), fieldId);
            device.Fields.Add(fieldId);

            // The new field has no value yet, so whatever it holds now is recorded as its first change.
            _paths.Add(path.ToString());
            _values.Add(item: default);
            _generations.Add(item: 0);
        }

        _generations[fieldId] = device.Generation;

        if (value != _values[fieldId])
            RecordChange(fieldId, value);
    }

    private Task<TResult> RunAsync<TResult>(Func<TResult> work)
    {
        TaskCompletionSource<TResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Post(() =>
        {
            try
            {
                completion.SetResult(work());
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Whoever asked gets the failure instead of waiting forever.
                completion.SetException(exception);
            }
        });

        return completion.Task;
    }

    private void Seal()
    {
        HistoryBlock block = _writer.ToBlock().Compress();

        _blocks.Add(block);

        _blocksSize += block.Size;

        _writer.Start(_time, CollectionsMarshal.AsSpan(_values));
    }

    private sealed class DeviceFields
    {
        public Dictionary<string, int> FieldIds { get; } = [];
        public List<int> Fields { get; } = [];

        public int Generation { get; set; }
    }
}
