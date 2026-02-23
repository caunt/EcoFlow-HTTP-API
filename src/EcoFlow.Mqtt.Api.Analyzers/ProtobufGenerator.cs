using Google.Protobuf;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

[Generator]
public class ProtobufGenerator : IIncrementalGenerator
{
    private const string ParserMemberName = "Parser";
    private const string UnknownFieldsMemberName = "_unknownFields";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var syntaxProvider = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: (syntaxNode, cancellationToken) => syntaxNode is ClassDeclarationSyntax,
            transform: (generatorSyntaxContext, cancellationToken) =>
            {
                var classDeclarationSyntax = (ClassDeclarationSyntax)generatorSyntaxContext.Node;
                var declaredSymbol = generatorSyntaxContext.SemanticModel.GetDeclaredSymbol(classDeclarationSyntax, cancellationToken);

                if (declaredSymbol is not INamedTypeSymbol namedTypeSymbol)
                    return null;

                var hasParserProperty = namedTypeSymbol.GetMembers(ParserMemberName).Any(member => member is IPropertySymbol);
                var implementsIMessage = namedTypeSymbol.AllInterfaces.Any(interfaceSymbol => interfaceSymbol.Name is nameof(IMessage));

                if (!hasParserProperty || !implementsIMessage)
                    return null;

                return namedTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            }
        ).Where(fullyQualifiedClassName => fullyQualifiedClassName is not null);

        var compilationAndClasses = context.CompilationProvider.Combine(syntaxProvider.Collect());

        context.RegisterSourceOutput(compilationAndClasses, (sourceProductionContext, sourceData) =>
        {
            var classNamesEnumerable = sourceData.Right;
            var sourceLinesEnumerable = GenerateSourceLines(classNamesEnumerable);
            var finalSourceText = string.Join("\n", sourceLinesEnumerable);

            sourceProductionContext.AddSource("ProtobufUtilities.g.cs", SourceText.From(finalSourceText, Encoding.UTF8));
        });
    }

    private IEnumerable<string> GenerateSourceLines(IEnumerable<string> classNamesEnumerable)
    {
        yield return "using System;";
        yield return "using System.Collections.Generic;";
        yield return "using System.Runtime.CompilerServices;";
        yield return "using Google.Protobuf;";
        yield return "";
        yield return "namespace ProtobufUtilities;";
        yield return "";
        yield return "public static class ProtobufMessages";
        yield return "{";
        yield return $"    public static readonly {nameof(MessageParser)}[] Parsers =";
        yield return "    [";

        foreach (var fullyQualifiedClassName in classNamesEnumerable)
            yield return $"        {fullyQualifiedClassName}.{ParserMemberName},";

        yield return "    ];";
        yield return "}";
        yield return "";

        yield return "public static class UnknownFieldsExtensionPropertyAccessors";
        yield return "{";
        yield return $"    extension({nameof(IMessage)} inputMessage)";
        yield return "    {";
        yield return $"        public {nameof(UnknownFieldSet)} UnknownFields";
        yield return "        {";
        yield return "            get";
        yield return "            {";
        yield return "                switch (inputMessage)";
        yield return "                {";

        foreach (var fullyQualifiedClassName in classNamesEnumerable)
        {
            yield return $"                    case {fullyQualifiedClassName} message:";
            yield return $"                        return GetUnknownFields(message);";
        }

        yield return "                    default:";
        yield return $"                        throw new {nameof(ArgumentException)}(\"The provided message type is not supported.\", nameof(inputMessage));";
        yield return "                }";
        yield return "            }";
        yield return "        }";
        yield return "    }";
        yield return "";

        foreach (var fullyQualifiedClassName in classNamesEnumerable)
        {
            yield return $"    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = \"{UnknownFieldsMemberName}\")]";
            yield return $"    private static extern ref {nameof(UnknownFieldSet)} GetUnknownFields({fullyQualifiedClassName} message);";
            yield return "";
        }

        yield return "}";
    }
}