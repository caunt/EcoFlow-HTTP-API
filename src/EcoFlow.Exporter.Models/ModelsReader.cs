using EcoFlow.Exporter.Common;

using Esprima;
using Esprima.Ast;

using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EcoFlow.Exporter.Models;

internal static class ModelsReader
{
    private static readonly JavaScriptParser Parser = new();

    public static async Task<JsonNode> GetAsync(string fileName, CancellationToken cancellationToken = default)
    {
        // The models are one JSON object kept as a string literal in the JavaScript of the app.
        return await ExtractJavaScriptStringsAsync(fileName, cancellationToken)
            .Distinct()
            .Where(static candidate => candidate.Contains(value: "EcoFlow DELTA Pro", StringComparison.Ordinal))
            .Where(HasOnlyEnglishLetters) // TODO: Comment this out to get models string
            .Select(ParseObject)
            .OfType<JsonNode>()
            .SingleAsync(cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);
    }

    private static async IAsyncEnumerable<string> ExtractJavaScriptStringsAsync(string filePath, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        IEnumerable<ZipFileEntry> files = ZipReader.EnumerateFilesRecursively(filePath,
            static fileName => fileName.EndsWith(value: ".js", StringComparison.OrdinalIgnoreCase));

        foreach (ZipFileEntry file in files)
        {
            using StreamReader reader = new(file.Content);

            string javascript = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            foreach (string extractedString in ExtractStrings(Parser.ParseScript(javascript)))
            {
                if (!string.IsNullOrWhiteSpace(extractedString))
                    yield return extractedString;
            }
        }
    }

    private static IEnumerable<string> ExtractStrings(Node currentNode)
    {
        switch (currentNode)
        {
            case Literal { TokenType: TokenType.StringLiteral, StringValue: { } stringValue }:
                {
                    yield return stringValue;

                    break;
                }
            case TemplateElement { Value.Cooked: { } cookedStringValue }:
                {
                    yield return cookedStringValue;

                    break;
                }
            default:
                break;
        }

        foreach (Node childNode in currentNode.ChildNodes)
        {
            foreach (string extractedChildString in ExtractStrings(childNode))
                yield return extractedChildString;
        }
    }

    private static bool HasOnlyEnglishLetters(string text)
    {
        return text.All(static character => !char.IsLetter(character) || char.IsAsciiLetter(character));
    }

    private static JsonNode? ParseObject(string text)
    {
        try
        {
            return JsonNode.Parse(text) is JsonObject node ? node : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
