using System.Text.Json.Serialization;

namespace EcoFlow.Mqtt.Api.Json;

internal sealed record LoginData([property: JsonPropertyName("token")] string? Token, [property: JsonPropertyName("user")] LoginUser? User);
