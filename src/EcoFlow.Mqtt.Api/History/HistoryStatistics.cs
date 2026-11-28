using System.Text.Json.Serialization;

namespace EcoFlow.Mqtt.Api.History;

internal sealed record HistoryStatistics([property: JsonPropertyName("enabled")] bool IsEnabled,
    [property: JsonPropertyName("maxAgeSeconds")] double MaximumAgeSeconds,
    [property: JsonPropertyName("maxBytes")] long MaximumBytes,
    [property: JsonPropertyName("bytes")] long Bytes,
    [property: JsonPropertyName("blocks")] int Blocks,
    [property: JsonPropertyName("changes")] long Changes,
    [property: JsonPropertyName("fields")] int Fields,
    [property: JsonPropertyName("fromUtcSeconds")] double? FromUtcSeconds);
