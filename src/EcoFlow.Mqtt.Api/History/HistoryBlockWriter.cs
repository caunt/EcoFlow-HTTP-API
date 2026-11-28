using System.Buffers;

namespace EcoFlow.Mqtt.Api.History;

// Collects the changes of the block that is still open. It is not compressed until it is full.
internal sealed class HistoryBlockWriter
{
    private readonly ArrayBufferWriter<byte> _fields = new();
    private readonly ArrayBufferWriter<byte> _times = new();
    private readonly ArrayBufferWriter<byte> _values = new();

    // The encoder state of every field, see HistoryCodec.
    private long[] _mantissas = [];
    private byte[] _scales = [];

    // What the next change is measured from, and where the changes start after the keyframe.
    private long _endTime;
    private int _fieldId;
    private int _keyframeCount;
    private int _keyframeLength;

    public int ChangesLength => _values.WrittenCount - _keyframeLength + _fields.WrittenCount + _times.WrittenCount;
    public int Size => _values.Capacity + _fields.Capacity + _times.Capacity + (_mantissas.Length * sizeof(long)) + _scales.Length;

    public int ChangeCount { get; private set; }
    public bool IsStarted { get; private set; }
    public long StartTime { get; private set; }

    public void Append(long time, int fieldId, HistoryValue value)
    {
        Grow(fieldId + 1);
        HistoryCodec.WriteVariableInteger(_times, ulong.CreateChecked(time - _endTime));
        HistoryCodec.WriteVariableInteger(_fields, HistoryCodec.EncodeSigned(fieldId - _fieldId));
        HistoryCodec.WriteValue(_values, value, ref _mantissas[fieldId], ref _scales[fieldId]);

        _fieldId = fieldId;
        _endTime = time;
        ChangeCount++;
    }

    public void Start(long time, ReadOnlySpan<HistoryValue> keyframe)
    {
        _values.ResetWrittenCount();
        _fields.ResetWrittenCount();
        _times.ResetWrittenCount();

        // Every field starts over, so the block can be read without the ones before it.
        Array.Clear(_mantissas);
        Array.Clear(_scales);
        Grow(keyframe.Length);

        for (int fieldId = 0; fieldId < keyframe.Length; fieldId++)
            HistoryCodec.WriteValue(_values, keyframe[fieldId], ref _mantissas[fieldId], ref _scales[fieldId]);

        // Where the changes start, and what the first of them is measured from.
        _keyframeLength = _values.WrittenCount;
        _keyframeCount = keyframe.Length;
        _fieldId = 0;
        _endTime = time;

        // Nothing has changed yet.
        StartTime = time;
        ChangeCount = 0;
        IsStarted = true;
    }

    public HistoryBlock ToBlock()
    {
        byte[] data = new byte[_values.WrittenCount + _fields.WrittenCount + _times.WrittenCount];

        _values.WrittenSpan.CopyTo(data);
        _fields.WrittenSpan.CopyTo(data.AsSpan(_values.WrittenCount));
        _times.WrittenSpan.CopyTo(data.AsSpan(_values.WrittenCount + _fields.WrittenCount));

        return new HistoryBlock(StartTime,
            _endTime,
            _keyframeCount,
            ChangeCount,
            _values.WrittenCount,
            _fields.WrittenCount,
            _times.WrittenCount,
            data,
            IsCompressed: false);
    }

    private void Grow(int fieldCount)
    {
        if (fieldCount <= _mantissas.Length)
            return;

        int capacity = Math.Max(fieldCount, _mantissas.Length * 2);

        Array.Resize(ref _mantissas, capacity);
        Array.Resize(ref _scales, capacity);
    }
}
