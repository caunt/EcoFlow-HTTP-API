using System.IO.Compression;
using System.Numerics;

namespace EcoFlow.Mqtt.Api.History;

// A block is self-contained: it starts with the value of every known field (the keyframe), followed by the changes in
// the order they happened. Values, field ids and times are kept as three separate streams, which compresses far better
// than interleaving them. Times are Unix milliseconds.
internal sealed record HistoryBlock(long StartTime,
    long EndTime,
    int KeyframeCount,
    int ChangeCount,
    int ValuesLength,
    int FieldsLength,
    int TimesLength,
    ReadOnlyMemory<byte> Data,
    bool IsCompressed)
{
    private const int CompressionQuality = 11;
    private const int MaximumCompressionWindow = 24;
    private const int MinimumCompressionWindow = 10;

    public int Size => Data.Length;

    public HistoryBlock Compress()
    {
        if (IsCompressed)
            return this;

        // The window only needs to cover the block; a bigger one just makes the encoder allocate more.
        int window = Math.Clamp(BitOperations.Log2(uint.CreateChecked(Math.Max(Data.Length, val2: 1))) + 1,
            MinimumCompressionWindow,
            MaximumCompressionWindow);

        byte[] buffer = new byte[BrotliEncoder.GetMaxCompressedLength(Data.Length)];

        return BrotliEncoder.TryCompress(Data.Span, buffer, out int bytesWritten, CompressionQuality, window)
            ? this with
            {
                Data = buffer.AsMemory(start: 0, bytesWritten).ToArray(),
                IsCompressed = true
            }
            : throw new InvalidOperationException(message: "Failed to compress a history block.");
    }

    public ReadOnlyMemory<byte> Decompress()
    {
        if (!IsCompressed)
            return Data;

        byte[] data = new byte[ValuesLength + FieldsLength + TimesLength];

        return BrotliDecoder.TryDecompress(Data.Span, data, out int bytesWritten) && bytesWritten == data.Length
            ? data
            : throw new InvalidOperationException(message: "Failed to decompress a history block.");
    }
}
