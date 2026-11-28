using System.Text.Json.Nodes;

namespace EcoFlow.Mqtt.Api.Extensions;

internal static class JsonExtensions
{
    private static IEnumerable<string> Flatten(JsonNode? node, string path)
    {
        return node switch
        {
            JsonObject jsonObject => jsonObject.SelectMany(property => Flatten(property.Value,
                path.Length is 0 ? property.Key : $"{path}.{property.Key}")),
            JsonArray jsonArray => jsonArray.SelectMany((item, index) => Flatten(item, path: $"{path}[{index}]")),
            _ => [$"{path}={node}"]
        };
    }

    extension(JsonNode? node)
    {
        public IEnumerable<string> Flatten()
        {
            return Flatten(node, string.Empty);
        }

        public void MergeWith(JsonNode nextNode)
        {
            if (node is not JsonObject previousObject || nextNode is not JsonObject nextObject)
                return;

            foreach (KeyValuePair<string, JsonNode?> nextProperty in nextObject)
            {
                if (previousObject[nextProperty.Key] is JsonObject && nextProperty.Value is JsonObject nextValue)
                {
                    node.MergeWith(nextValue);
                }
                else
                {
                    JsonNode? clonedNode = nextProperty.Value?.DeepClone();

                    clonedNode.Sort();

                    previousObject[nextProperty.Key] = clonedNode;

                    previousObject.Sort();
                }
            }
        }

        public void Sort()
        {
            switch (node)
            {
                case JsonObject jsonObject:
                    {
                        KeyValuePair<string, JsonNode?>[] sortedProperties = [.. jsonObject.OrderBy(static property => property.Key, StringComparer.Ordinal)];

                        jsonObject.Clear();

                        foreach (KeyValuePair<string, JsonNode?> property in sortedProperties)
                        {
                            property.Value.Sort();

                            jsonObject[property.Key] = property.Value;
                        }

                        break;
                    }
                case JsonArray jsonArray:
                    {
                        foreach (JsonNode? item in jsonArray)
                            item.Sort();

                        break;
                    }
                default:
                    break;
            }
        }
    }
}
