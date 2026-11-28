using EcoFlow.Mqtt.Api.Exceptions;
using EcoFlow.Mqtt.Api.Models;

using Google.Protobuf;

using ProtobufUtilities;

using System.Text.Json;
using System.Text.Json.Nodes;

namespace EcoFlow.Mqtt.Api.Extensions;

internal static class ProtobufExtensions
{
    private const int DescribedCandidateCount = 15;

    private static readonly JsonFormatter Formatter = new(new JsonFormatter.Settings(formatDefaultValues: true));

    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true
    };

    private static MessageCandidate? Parse(MessageParser parser, ByteString byteString)
    {
        try
        {
            IMessage message = parser.ParseFrom(byteString);

            return new MessageCandidate(message, message.UnknownFields?.CalculateSize() ?? 0);
        }
        catch (InvalidProtocolBufferException)
        {
            return null;
        }
    }

    extension(IMessage message)
    {
        public JsonNode ToJson()
        {
            return JsonNode.Parse(Formatter.Format(message))
                ?? throw new InvalidOperationException(message: "Failed to parse JSON from the message.");
        }

        public string ToStringWithTitle()
        {
            return $"{message.GetType()} {message.ToJson().ToJsonString(IndentedOptions)}";
        }
    }

    extension(ByteString byteString)
    {
        // Payloads do not say which message they hold, so every known message is tried and the one that leaves the fewest
        // bytes unexplained wins.
        public IMessage AsEcoFlowMessage(int commandFunction, int commandId)
        {
            MessageCandidate[] candidates = [.. ProtobufMessages.Parsers
                .AsParallel()
                .Select(parser => Parse(parser, byteString))
                .OfType<MessageCandidate>()
                .OrderBy(candidate => candidate.UnknownFieldsSize)];

            if (candidates.Length is 0)
                throw new InvalidOperationException(message: "No message could be parsed from the payload.");

            MessageCandidate best = candidates[0];

            if (candidates.Length is 1 || best.UnknownFieldsSize * 2 < candidates[1].UnknownFieldsSize)
                return best.Message;

            // Devices of different series declare the same message under the same name, any of those is as good as another.
            bool haveSameName = candidates.TakeWhile(candidate => candidate.UnknownFieldsSize == best.UnknownFieldsSize)
                .Select(candidate => candidate.Message.GetType().Name)
                .Distinct()
                .Count() is 1;

            if (haveSameName)
                return best.Message;

            string description = string.Join(separator: '\n',
                candidates.Take(DescribedCandidateCount)
                    .Select(candidate => $"{candidate.UnknownFieldsSize} => {candidate.Message.GetType()}"));

            throw new MessageMultipleMatchesException(message: $"Multiple messages could be parsed from the payload, and the best match is not significantly better than the second best match:\n{description}\ncmdFunc: {commandFunction}, cmdId:{commandId}\n{Convert.ToHexString(byteString.Span)}");
        }
    }
}
