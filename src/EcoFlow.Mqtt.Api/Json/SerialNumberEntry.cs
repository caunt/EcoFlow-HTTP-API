using System.Text.Json.Serialization;

namespace EcoFlow.Mqtt.Api.Json;

internal sealed record SerialNumberEntry([property: JsonPropertyName("sn")] string? SerialNumber);
