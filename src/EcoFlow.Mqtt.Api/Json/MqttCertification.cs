using System.Text.Json.Serialization;

namespace EcoFlow.Mqtt.Api.Json;

internal sealed record MqttCertification([property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("port"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] int Port,
    [property: JsonPropertyName("certificateAccount")] string? CertificateAccount,
    [property: JsonPropertyName("certificatePassword")] string? CertificatePassword,
    [property: JsonPropertyName("protocol")] string? Protocol);
