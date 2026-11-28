using EcoFlow.Mqtt.Api.History;

using System.Text.Json;
using System.Text.Json.Serialization;

namespace EcoFlow.Mqtt.Api.Json;

[JsonSerializable(typeof(LoginPayload))]
[JsonSerializable(typeof(EcoFlowResponse<LoginData>))]
[JsonSerializable(typeof(EcoFlowResponse<MqttCertification>))]
[JsonSerializable(typeof(EcoFlowResponse<IReadOnlyList<SerialNumberEntry>>))]
[JsonSerializable(typeof(EcoFlowResponse<IReadOnlyList<ProductEntry>>))]
[JsonSerializable(typeof(EcoFlowResponse<JsonElement>))]
[JsonSerializable(typeof(HistoryStatistics))]
internal sealed partial class ApplicationJsonContext : JsonSerializerContext;
