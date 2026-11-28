using System.Text.Json.Serialization;

namespace EcoFlow.Mqtt.Api.Json;

internal sealed record LoginBundle([property: JsonPropertyName("bundleId")] string BundleId);
