using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;

using Google.Protobuf;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace EcoFlow.Mqtt.Api.Analyzers;

/// <summary>
/// Generates the list of every protobuf message parser and the accessors that read the unknown fields of a parsed message.
/// </summary>
/// <remarks>
/// The API finds out which message a payload holds by parsing it with every parser and keeping the message that leaves the
/// fewest unknown fields. Protobuf keeps those in a private field, so the generated accessors reach it without reflection.
/// </remarks>
[Generator]
public sealed class ProtobufGenerator : IIncrementalGenerator
{
    private const string ParserMemberName = "Parser";
    private const string UnknownFieldsMemberName = "_unknownFields";

    /// <summary>
    /// Registers the generation of <c>ProtobufUtilities.g.cs</c> from every class of the compilation that is a protobuf message.
    /// </summary>
    /// <param name="context">The context that receives the syntax provider and the source output.</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValueProvider<ImmutableArray<string?>> messageClassNames = context.SyntaxProvider
            .CreateSyntaxProvider(static (syntaxNode, cancellationToken) => syntaxNode is ClassDeclarationSyntax, GetMessageClassName)
            .Collect();

        context.RegisterSourceOutput(messageClassNames,
            static (sourceProductionContext, classNames) =>
            {
                string source = GenerateSource(classNames: [.. classNames.OfType<string>()]);

                sourceProductionContext.AddSource(hintName: "ProtobufUtilities.g.cs", SourceText.From(source, Encoding.UTF8));
            });
    }

    private static string GenerateSource(ImmutableArray<string> classNames)
    {
        List<string> lines =
        [
            "using System;",
            "using System.Collections.Generic;",
            "using System.Runtime.CompilerServices;",
            "using Google.Protobuf;",
            "",
            "namespace ProtobufUtilities;",
            "",
            "/// <summary>Lists every protobuf message known to the API.</summary>",
            "public static class ProtobufMessages",
            "{",
            "    /// <summary>The parser of every protobuf message, in no particular order.</summary>",
            $"    public static readonly {nameof(MessageParser)}[] Parsers =",
            "    [",
            .. classNames.Select(static className => $"        {className}.{ParserMemberName},"),
            "    ];",
            "}",
            "",
            "/// <summary>Reads the unknown fields that protobuf keeps private in every parsed message.</summary>",
            "public static class UnknownFieldsExtensionPropertyAccessors",
            "{",
            $"    extension({nameof(IMessage)} inputMessage)",
            "    {",
            "        /// <summary>Gets the fields of the payload that the message does not declare, or null when there are none.</summary>",
            $"        public {nameof(UnknownFieldSet)} UnknownFields",
            "        {",
            "            get",
            "            {",
            "                switch (inputMessage)",
            "                {",
            .. classNames.Select(static className => $"                    case {className} message:\n                        return GetUnknownFields(message);"),
            "                    default:",
            $"                        throw new {nameof(ArgumentException)}(\"The provided message type is not supported.\", nameof(inputMessage));",
            "                }",
            "            }",
            "        }",
            "    }",
            "",
            .. classNames.Select(static className => $"    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = \"{UnknownFieldsMemberName}\")]\n    private static extern ref {nameof(UnknownFieldSet)} GetUnknownFields({className} message);\n"),
            "}",
        ];

        return string.Join(separator: "\n", lines);
    }

    private static string? GetMessageClassName(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.SemanticModel.GetDeclaredSymbol(context.Node, cancellationToken) is not INamedTypeSymbol classSymbol)
            return null;

        bool hasParserProperty = classSymbol.GetMembers(ParserMemberName).Any(static member => member is IPropertySymbol);
        bool isMessage = classSymbol.AllInterfaces.Any(static interfaceSymbol => interfaceSymbol.Name is nameof(IMessage));

        return hasParserProperty && isMessage ? classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) : null;
    }
}
