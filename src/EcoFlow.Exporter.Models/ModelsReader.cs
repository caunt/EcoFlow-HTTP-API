using EcoFlow.Exporter.Common;
using Esprima;
using Esprima.Ast;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EcoFlow.Exporter.Models;

public static partial class ModelsReader
{
    private static readonly JavaScriptParser _parser = new();

    public static async Task<JsonNode> GetAsync(string fileName, CancellationToken cancellationToken = default)
    {
        return await ExtractJavaScriptStringsFromFile(fileName, cancellationToken)
            .Distinct()
            .Where(candidate => candidate.Contains("EcoFlow DELTA Pro"))
            .Where(HasOnlyEnglishLettersAndSymbols) // TODO: Comment this out to get models string
            .Select(candidate =>
            {
                if (!TryParseJson(candidate, out var node))
                    return null;

                if (node.GetValueKind() is not JsonValueKind.Object)
                    return null;

                return node;
            })
            .WhereNotNull()
            .SingleAsync(cancellationToken);
    }

    private static bool HasOnlyEnglishLettersAndSymbols(string inputString)
    {
        if (inputString is null)
            return false;

        foreach (var currentCharacter in inputString)
        {
            if (char.IsLetter(currentCharacter))
            {
                var isEnglishLetter = currentCharacter is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

                if (!isEnglishLetter)
                    return false;
            }
        }

        return true;
    }

    private static bool TryParseJson(string inputString, [MaybeNullWhen(false)] out JsonNode node)
    {
        try
        {
            node = JsonNode.Parse(inputString);
            return node is not null;
        }
        catch (JsonException)
        {
            node = null;
            return false;
        }
    }

    private static async IAsyncEnumerable<string> ExtractJavaScriptStringsFromFile(string filePath, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var files = ZipReader.EnumerateFilesRecursively(filePath, fileName => fileName.EndsWith(".js", StringComparison.OrdinalIgnoreCase));

        foreach (var (chunkFileName, chunkFileStream) in files)
        {
            var chunkStreamReader = new StreamReader(chunkFileStream);
            var chunkFileContent = await chunkStreamReader.ReadToEndAsync(cancellationToken);

            foreach (var extractedString in ExtractStrings(chunkFileContent))
                yield return extractedString;
        }
    }

    private static IEnumerable<string> ExtractStrings(string javascript)
    {
        var parsedScript = _parser.ParseScript(javascript);

        foreach (var extractedString in ExtractStringsFromNode(parsedScript))
        {
            if (string.IsNullOrWhiteSpace(extractedString))
                continue;

            yield return extractedString;
        }
    }

    private static IEnumerable<string> ExtractStringsFromNode(Node currentNode)
    {
        if (currentNode is null)
            yield break;

        if (currentNode is Literal literalNode)
        {
            if (literalNode.TokenType == TokenType.StringLiteral)
            {
                if (literalNode.Value is string stringValue)
                    yield return stringValue;
            }
        }
        else if (currentNode is TemplateElement templateElementNode)
        {
            if (templateElementNode.Value.Cooked is { } cookedStringValue)
                yield return cookedStringValue;
        }

        foreach (var childNode in currentNode.ChildNodes)
        {
            foreach (var extractedChildString in ExtractStringsFromNode(childNode))
            {
                if (extractedChildString is null)
                    continue;

                yield return extractedChildString;
            }
        }
    }
}
