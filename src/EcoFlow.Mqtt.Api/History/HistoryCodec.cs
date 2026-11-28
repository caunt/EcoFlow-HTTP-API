using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace EcoFlow.Mqtt.Api.History;

// Values are written as variable-length tokens. An even token is the common case: the numeric change since the
// previous value of the same field, at the decimal scale that field currently uses. An odd token holds a code.
internal static class HistoryCodec
{
    private const int MaximumScale = 9;

    private const ulong MaximumDelta = ulong.MaxValue >> 1;

    private static readonly long[] PowersOfTen = [1, 10, 100, 1_000, 10_000, 100_000, 1_000_000, 10_000_000, 100_000_000, 1_000_000_000];

    // What an odd token says. Scale and every code above it give the field a new decimal scale.
    private enum Code
    {
        Missing,
        Null,
        False,
        True,
        Text,
        Number,
        Scale
    }

    public static long DecodeSigned(ulong value)
    {
        return long.CreateTruncating(value >> 1) ^ -long.CreateTruncating(value & 1);
    }

    public static ulong EncodeSigned(long value)
    {
        return ulong.CreateTruncating((value << 1) ^ (value >> 63));
    }

    public static HistoryValue ReadValue(ReadOnlySpan<byte> bytes, ref int position, ref long mantissa, ref byte scale)
    {
        ulong token = ReadVariableInteger(bytes, ref position);

        if ((token & 1) is 0)
        {
            mantissa = unchecked(mantissa + DecodeSigned(token >> 1));
        }
        else
        {
            int code = int.CreateTruncating(token >> 1);

            switch ((Code)code)
            {
                case Code.Missing:
                    return default;
                case Code.Null:
                    return new HistoryValue(HistoryValueKind.Null);
                case Code.False:
                    return new HistoryValue(HistoryValueKind.False);
                case Code.True:
                    return new HistoryValue(HistoryValueKind.True);
                case Code.Text:
                    {
                        int byteCount = int.CreateChecked(ReadVariableInteger(bytes, ref position));
                        string text = Encoding.UTF8.GetString(bytes.Slice(position, byteCount));

                        position += byteCount;

                        return HistoryValue.FromText(text);
                    }
                case Code.Number:
                    {
                        long bits = BinaryPrimitives.ReadInt64LittleEndian(bytes[position..]);

                        position += sizeof(long);

                        return new HistoryValue(HistoryValueKind.Number, bits);
                    }
                default:
                    {
                        scale = byte.CreateChecked(code - (int)Code.Scale);
                        mantissa = DecodeSigned(ReadVariableInteger(bytes, ref position));

                        break;
                    }
            }
        }

        return scale is 0
            ? HistoryValue.FromInteger(mantissa)
            : HistoryValue.FromNumber(double.CreateChecked(mantissa) / PowersOfTen[scale]);
    }

    public static ulong ReadVariableInteger(ReadOnlySpan<byte> bytes, ref int position)
    {
        ulong value = 0;
        int shift = 0;
        byte current;

        do
        {
            current = bytes[position++];
            value |= ulong.CreateTruncating(current & 0x7F) << shift;
            shift += 7;
        }
        while (current >= 0x80);

        return value;
    }

    public static void WriteValue(IBufferWriter<byte> writer, HistoryValue value, ref long mantissa, ref byte scale)
    {
        switch (value.Kind)
        {
            case HistoryValueKind.Missing:
                {
                    WriteCode(writer, Code.Missing);

                    return;
                }
            case HistoryValueKind.Null:
                {
                    WriteCode(writer, Code.Null);

                    return;
                }
            case HistoryValueKind.False:
                {
                    WriteCode(writer, Code.False);

                    return;
                }
            case HistoryValueKind.True:
                {
                    WriteCode(writer, Code.True);

                    return;
                }
            case HistoryValueKind.Text:
                {
                    byte[] text = Encoding.UTF8.GetBytes(value.Text ?? string.Empty);

                    WriteCode(writer, Code.Text);
                    WriteVariableInteger(writer, ulong.CreateChecked(text.Length));
                    writer.Write(text);

                    return;
                }
            default:
                break;
        }

        if (!TryGetMantissa(value, scale, out int nextScale, out long nextMantissa))
        {
            WriteCode(writer, Code.Number);
            BinaryPrimitives.WriteInt64LittleEndian(writer.GetSpan(sizeof(long)), value.Bits);
            writer.Advance(sizeof(long));

            return;
        }

        ulong delta = EncodeSigned(unchecked(nextMantissa - mantissa));

        if (nextScale == scale && delta <= MaximumDelta)
        {
            WriteVariableInteger(writer, delta << 1);
        }
        else
        {
            WriteCode(writer, (Code)((int)Code.Scale + nextScale));
            WriteVariableInteger(writer, EncodeSigned(nextMantissa));
        }

        mantissa = nextMantissa;
        scale = byte.CreateChecked(nextScale);
    }

    public static void WriteVariableInteger(IBufferWriter<byte> writer, ulong value)
    {
        Span<byte> span = writer.GetSpan(sizeHint: 10);
        int count = 0;

        for (; value >= 0x80; value >>= 7)
            span[count++] = byte.CreateTruncating(value | 0x80);

        span[count] = byte.CreateTruncating(value);

        writer.Advance(count + 1);
    }

    // Finds the integer that, divided by a power of ten, gives back exactly the same number. Telemetry is decimal text
    // with a few fraction digits, so nearly every value fits and consecutive values differ by a small integer.
    private static bool TryGetMantissa(HistoryValue value, int minimumScale, out int scale, out long mantissa)
    {
        if (value.Kind is HistoryValueKind.Integer)
        {
            // An integer stays at the scale of its field only while the scaled value survives the division on read.
            bool fitsScale = Math.Abs(double.CreateChecked(value.Bits)) * PowersOfTen[minimumScale] <= HistoryValue.MaximumExactInteger;

            scale = fitsScale ? minimumScale : 0;
            mantissa = fitsScale ? value.Bits * PowersOfTen[minimumScale] : value.Bits;

            return true;
        }

        double number = value.Number;

        for (scale = minimumScale; scale <= MaximumScale; scale++)
        {
            double scaled = Math.Round(number * PowersOfTen[scale]);

            if (!(Math.Abs(scaled) <= HistoryValue.MaximumExactInteger))
                break;

            if (scaled / PowersOfTen[scale] == number)
            {
                mantissa = long.CreateTruncating(scaled);

                return true;
            }
        }

        scale = 0;
        mantissa = 0;

        return false;
    }

    private static void WriteCode(IBufferWriter<byte> writer, Code code)
    {
        WriteVariableInteger(writer, (ulong.CreateChecked((int)code) << 1) | 1);
    }
}
