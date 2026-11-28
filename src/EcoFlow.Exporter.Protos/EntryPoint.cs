using EcoFlow.Exporter.Protos;

using Google.Protobuf;
using Google.Protobuf.Reflection;

using System.CommandLine;
using System.Text;
using System.Text.Json;

Console.OutputEncoding = Encoding.UTF8;

Option<FileInfo> inputFilePathOption = new(name: "--input", aliases: ["-i"])
{
    Description = "The path to the input .xapk file",
    Required = true
};

Option<FileInfo> outputFilePathOption = new(name: "--output", aliases: ["-o"])
{
    Description = "The path to the output File Descriptor Set .pb file",
    DefaultValueFactory = result => new FileInfo(fileName: "file_descriptor_set.pb")
};

Option<string?> goPackagePrefixOption = new(name: "--go-package-prefix")
{
    Description = "The Go import path prefix for go_package options (e.g. github.com/owner/repo/protos)"
};

RootCommand rootCommand = new(description: "Extracts proto descriptor set from XAPK file") { inputFilePathOption, outputFilePathOption, goPackagePrefixOption };

JsonSerializerOptions indentedOptions = new()
{
    WriteIndented = true
};

rootCommand.SetAction(async (result, cancellationToken) =>
{
    FileInfo inputFile = result.GetRequiredValue(inputFilePathOption);
    FileInfo outputFile = result.GetRequiredValue(outputFilePathOption);
    string? goPackagePrefix = result.GetValue(goPackagePrefixOption);

    FileDescriptorSet set = await ProtosReader
        .GetProtoSetAsync(inputFile.FullName,
            item => Console.WriteLine(value: $"Extracted {item.FileDescriptorProto.Name}"),
            goPackagePrefix,
            cancellationToken)
        .ConfigureAwait(continueOnCapturedContext: false);

    using JsonDocument diagnostics = JsonDocument.Parse(JsonFormatter.ToDiagnosticString(set));

    await File.WriteAllBytesAsync(outputFile.FullName, set.ToByteArray(), cancellationToken)
        .ConfigureAwait(continueOnCapturedContext: false);

    await File.WriteAllTextAsync(path: "meta.json", JsonSerializer.Serialize(diagnostics.RootElement, indentedOptions), cancellationToken)
        .ConfigureAwait(continueOnCapturedContext: false);

    IReadOnlyList<FileDescriptor> fileDescriptors = FileDescriptor.BuildFromByteStrings(set.File
        .Select(file => file.ToByteString()));

    Console.WriteLine(value: $"OK: built {fileDescriptors.Count} FileDescriptor(s), {fileDescriptors.CalculateMessageTypeCount()} MessageType(s).");
});

return await rootCommand.Parse(args).InvokeAsync().ConfigureAwait(continueOnCapturedContext: false);
