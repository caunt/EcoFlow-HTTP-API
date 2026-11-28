using Google.Protobuf;

namespace EcoFlow.Mqtt.Api.Models;

internal sealed record MessageCandidate(IMessage Message, int UnknownFieldsSize);
