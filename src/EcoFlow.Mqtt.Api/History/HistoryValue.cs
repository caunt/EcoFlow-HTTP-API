using System.Globalization;
using System.Text.Json;

namespace EcoFlow.Mqtt.Api.History;

// One value of a device state. Numbers keep their exact value: integers as they are, everything else as a double.
internal readonly record struct HistoryValue(HistoryValueKind Kind, long Bits = 0, string? Text = null)
{
    public const long MaximumExactInteger = 1L << 53;

    public double Number => BitConverter.Int64BitsToDouble(Bits);

    public static HistoryValue From(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => new HistoryValue(HistoryValueKind.Null),
            JsonValueKind.False => new HistoryValue(HistoryValueKind.False),
            JsonValueKind.True => new HistoryValue(HistoryValueKind.True),
            JsonValueKind.Number => element.TryGetInt64(out long integer)
                ? FromInteger(integer)
                : FromNumber(element.GetDouble()),
            _ => FromText(element.ToString())
        };
    }

    public static HistoryValue FromInteger(long value)
    {
        return new HistoryValue(HistoryValueKind.Integer, value);
    }

    public static HistoryValue FromNumber(double value)
    {
        // Integral numbers are always kept as integers, so 1 and 1.0 are the same value.
        return Math.Abs(value) <= MaximumExactInteger && Math.Floor(value) == value
            ? FromInteger(long.CreateTruncating(value))
            : new HistoryValue(HistoryValueKind.Number, BitConverter.DoubleToInt64Bits(value));
    }

    public static HistoryValue FromText(string value)
    {
        return new HistoryValue(HistoryValueKind.Text, Text: value);
    }

    public override string ToString()
    {
        return Kind switch
        {
            HistoryValueKind.False => "false",
            HistoryValueKind.True => "true",
            HistoryValueKind.Integer => Bits.ToString(CultureInfo.InvariantCulture),
            HistoryValueKind.Number => Number.ToString(format: "R", CultureInfo.InvariantCulture),
            HistoryValueKind.Text => Text ?? string.Empty,
            _ => string.Empty
        };
    }

    public void WriteTo(Utf8JsonWriter writer)
    {
        switch (Kind)
        {
            case HistoryValueKind.False:
                {
                    writer.WriteBooleanValue(value: false);

                    break;
                }
            case HistoryValueKind.True:
                {
                    writer.WriteBooleanValue(value: true);

                    break;
                }
            case HistoryValueKind.Integer:
                {
                    writer.WriteNumberValue(Bits);

                    break;
                }
            case HistoryValueKind.Number:
                {
                    writer.WriteNumberValue(Number);

                    break;
                }
            case HistoryValueKind.Text:
                {
                    writer.WriteStringValue(Text);

                    break;
                }
            default:
                {
                    writer.WriteNullValue();

                    break;
                }
        }
    }
}
