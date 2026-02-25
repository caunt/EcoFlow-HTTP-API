using EcoFlow.Exporter.Protos;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using System.CommandLine;
using System.Text;
using System.Text.Json;

Console.OutputEncoding = Encoding.UTF8;

var inputFilePathOption = new Option<FileInfo>("--input", "-i")
{
    Description = "The path to the input .xapk file",
    Required = true
};

var outputFilePathOption = new Option<FileInfo>("--output", "-o")
{
    Description = "The path to the output File Descriptor Set .pb file",
    DefaultValueFactory = result => new FileInfo("file_descriptor_set.pb")
};

var rootCommand = new RootCommand("Extracts proto descriptor set from XAPK file")
{
    inputFilePathOption,
    outputFilePathOption
};

rootCommand.SetAction(async (result, cancellationToken) =>
{
    var inputFileInfo = result.GetRequiredValue(inputFilePathOption);
    var outputFileInfo = result.GetRequiredValue(outputFilePathOption);

    var set = await ProtosReader.GetProtoSetAsync(inputFileInfo.FullName, update =>
    {
        Console.WriteLine($"Extracted {update.FileDescriptorProto.Name}");
        return update;
    }, cancellationToken);

    await File.WriteAllBytesAsync(outputFileInfo.FullName, set.ToByteArray(), cancellationToken);
    await File.WriteAllTextAsync("meta.json", JsonSerializer.Serialize(JsonDocument.Parse(JsonFormatter.ToDiagnosticString(set)).RootElement, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

    var fileDescriptors = FileDescriptor.BuildFromByteStrings(set.File.Select(file => file.ToByteString()));
    Console.WriteLine($"OK: built {fileDescriptors.Count} FileDescriptor(s), {fileDescriptors.CalculateMessageTypeCount()} MessageType(s).");
});

return await rootCommand.Parse(args).InvokeAsync();

