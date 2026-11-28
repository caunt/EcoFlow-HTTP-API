using System.Text.Json.Serialization;

namespace EcoFlow.Mqtt.Api.Json;

internal sealed record ProductEntry([property: JsonPropertyName("name")] string? Name);
