using Microsoft.AspNetCore.Http;

using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;

namespace EcoFlow.Mqtt.Api.History;

// The history of one device between two times: the value of every selected field at the start, then every change.
// Blocks are decompressed one at a time while writing, so a long history never has to fit in memory at once.
internal sealed class HistoryResult(ImmutableArray<string> paths,
    ImmutableArray<int> indexes,
    ImmutableArray<HistoryBlock> blocks,
    long fromTime,
    long toTime,
    bool isFlat) : IResult
{
    private const double MillisecondsPerSecond = 1000;

    public Task ExecuteAsync(HttpContext httpContext)
    {
        httpContext.Response.ContentType = isFlat ? "text/plain; charset=utf-8" : "application/json; charset=utf-8";

        return isFlat
            ? WriteFlatAsync(httpContext.Response.BodyWriter, httpContext.RequestAborted)
            : WriteJsonAsync(httpContext.Response.BodyWriter, httpContext.RequestAborted);
    }

    // time path=value
    public Task WriteFlatAsync(PipeWriter pipeWriter, CancellationToken cancellationToken = default)
    {
        return WriteAsync(pipeWriter,
            initial =>
            {
                for (int index = 0; index < initial.Length; index++)
                {
                    if (initial[index].Kind is not HistoryValueKind.Missing)
                        WriteLine(fromTime, index, initial[index]);
                }
            },
            WriteLine,
            static () => { },
            cancellationToken);

        void WriteLine(long time, int index, HistoryValue value)
        {
            pipeWriter.Write(Encoding.UTF8
                .GetBytes(s: $"{ToSeconds(time).ToString(format: "R", CultureInfo.InvariantCulture)} {paths[index]}={value}\n"));
        }
    }

    // {"from":0,"to":0,"fields":["path"],"initial":[value],"changes":[[time,fieldIndex,value]]}
    public async Task WriteJsonAsync(PipeWriter pipeWriter, CancellationToken cancellationToken = default)
    {
        using Utf8JsonWriter writer = new(pipeWriter);

        writer.WriteStartObject();
        writer.WriteNumber(propertyName: "from", ToSeconds(fromTime));
        writer.WriteNumber(propertyName: "to", ToSeconds(toTime));
        writer.WriteStartArray(propertyName: "fields");

        foreach (string path in paths)
            writer.WriteStringValue(path);

        writer.WriteEndArray();

        await WriteAsync(pipeWriter,
            initial =>
            {
                writer.WriteStartArray(propertyName: "initial");

                foreach (HistoryValue value in initial)
                    value.WriteTo(writer);

                writer.WriteEndArray();
                writer.WriteStartArray(propertyName: "changes");
            },
            (time, index, value) =>
            {
                writer.WriteStartArray();
                writer.WriteNumberValue(ToSeconds(time));
                writer.WriteNumberValue(index);
                value.WriteTo(writer);

                // One change is [time, index of the field, value].
                writer.WriteEndArray();
            },
            writer.Flush,
            cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static double ToSeconds(long time)
    {
        return time / MillisecondsPerSecond;
    }

    private async Task WriteAsync(PipeWriter pipeWriter,
        Action<HistoryValue[]> writeInitial,
        Action<long, int, HistoryValue> writeChange,
        Action flush,
        CancellationToken cancellationToken)
    {
        HistoryBlockReader reader = new(indexes.Length);
        HistoryValue[] values = new HistoryValue[indexes.Length];
        HistoryValue[] initial = new HistoryValue[paths.Length];
        bool isInitialWritten = false;

        // The values at the start come from the last block that started before it.
        int firstBlockIndex = 0;

        while (firstBlockIndex + 1 < blocks.Length && blocks[firstBlockIndex + 1].StartTime <= fromTime)
            firstBlockIndex++;

        for (int blockIndex = firstBlockIndex; blockIndex < blocks.Length && blocks[blockIndex].StartTime <= toTime; blockIndex++)
        {
            reader.Open(blocks[blockIndex], values);

            for (int fieldId = 0; fieldId < indexes.Length && !isInitialWritten; fieldId++)
            {
                if (indexes[fieldId] >= 0)
                    initial[indexes[fieldId]] = values[fieldId];
            }

            while (reader.TryRead(out long time, out int fieldId, out HistoryValue value) && time <= toTime)
            {
                int index = indexes[fieldId];

                if (index < 0)
                    continue;

                if (time < fromTime)
                {
                    initial[index] = value;

                    continue;
                }

                if (!isInitialWritten)
                {
                    writeInitial(initial);

                    isInitialWritten = true;
                }

                writeChange(time, index, value);
            }

            if (!isInitialWritten)
            {
                writeInitial(initial);

                isInitialWritten = true;
            }

            flush();

            FlushResult flushResult = await pipeWriter.FlushAsync(cancellationToken)
                .ConfigureAwait(continueOnCapturedContext: false);

            // Nobody is reading anymore.
            if (flushResult.IsCompleted)
                return;
        }

        if (!isInitialWritten)
            writeInitial(initial);
    }
}
