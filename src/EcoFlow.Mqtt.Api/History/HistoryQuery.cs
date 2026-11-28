using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace EcoFlow.Mqtt.Api.History;

// What a history request asks for: ?from=&to=&fields=a,b&flat. Times are Unix seconds, or seconds relative to now when
// they are not positive, so from=-3600 is the last hour.
internal sealed record HistoryQuery(DateTimeOffset? From, DateTimeOffset? To, ImmutableArray<string> Fields, bool IsFlat)
{
    private const double MaximumUnixTimeSeconds = 253_402_300_799;
    private const double MillisecondsPerSecond = 1000;

    public static bool TryParse(IQueryCollection query, DateTimeOffset now, [NotNullWhen(true)] out HistoryQuery? historyQuery)
    {
        string[] fields = query[key: "fields"].ToString()
            .Split(separator: ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        bool hasValidTimes = TryParseTime(query, name: "from", now, out DateTimeOffset? from) & TryParseTime(query,
            name: "to",
            now,
            out DateTimeOffset? to);

        historyQuery = hasValidTimes ? new HistoryQuery(from, to, Fields: [.. fields], query.ContainsKey(key: "flat")) : null;

        return hasValidTimes;
    }

    // A field is selected by its own path, or by the path of anything it is part of.
    public bool Selects(string path)
    {
        return Fields.IsEmpty
            || Fields.Any(field => path.StartsWith(field, StringComparison.Ordinal) && (path.Length == field.Length || path[field.Length] is '.' or '['));
    }

    private static bool TryParseTime(IQueryCollection query, string name, DateTimeOffset now, out DateTimeOffset? time)
    {
        time = null;

        if (!query.TryGetValue(name, out StringValues values))
            return true;

        if (!double.TryParse(values.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
            return false;

        if (!(Math.Abs(seconds) <= MaximumUnixTimeSeconds))
            return false;

        time = seconds > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(long.CreateTruncating(seconds * MillisecondsPerSecond))
            : now.AddSeconds(Math.Max(seconds, -now.ToUnixTimeSeconds()));

        return true;
    }
}
