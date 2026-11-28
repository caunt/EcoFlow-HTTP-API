using System.Text.Json.Serialization;

namespace EcoFlow.Mqtt.Api.Json;

internal sealed record EcoFlowResponse<TData>([property: JsonPropertyName("data")] TData? Data);
