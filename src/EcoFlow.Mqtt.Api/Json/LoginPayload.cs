using System.Text.Json.Serialization;

namespace EcoFlow.Mqtt.Api.Json;

internal sealed record LoginPayload([property: JsonPropertyName("os")] string OperatingSystem,
    [property: JsonPropertyName("scene")] string Scene,
    [property: JsonPropertyName("appVersion")] string AppVersion,
    [property: JsonPropertyName("osVersion")] string OperatingSystemVersion,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("oauth")] LoginBundle Bundle,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("userType")] string UserType);
