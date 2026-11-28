using System.Text.Json.Serialization;

namespace EcoFlow.Mqtt.Api.Json;

internal sealed record LoginUser([property: JsonPropertyName("userId")] string? UserId);
