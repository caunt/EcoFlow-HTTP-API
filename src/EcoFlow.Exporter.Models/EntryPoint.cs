using EcoFlow.Exporter.Models;

using System.CommandLine;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

Console.OutputEncoding = Encoding.UTF8;

Option<FileInfo> inputFilePathOption = new(name: "--input", aliases: ["-i"])
{
    Description = "The path to the input .xapk file",
    Required = true
};

Option<FileInfo> outputFilePathOption = new(name: "--output", aliases: ["-o"])
{
    Description = "The path to the output .json file",
    DefaultValueFactory = result => new FileInfo(fileName: "models.json")
};

RootCommand rootCommand = [with(description: "Extracts model set from XAPK file"), inputFilePathOption, outputFilePathOption];

JsonSerializerOptions indentedOptions = new()
{
    WriteIndented = true
};

rootCommand.SetAction(async (result, cancellationToken) =>
{
    FileInfo inputFile = result.GetRequiredValue(inputFilePathOption);
    FileInfo outputFile = result.GetRequiredValue(outputFilePathOption);

    JsonNode models = await ModelsReader.GetAsync(inputFile.FullName, cancellationToken)
        .ConfigureAwait(continueOnCapturedContext: false);

    await File.WriteAllTextAsync(outputFile.FullName, models.ToJsonString(indentedOptions), cancellationToken)
        .ConfigureAwait(continueOnCapturedContext: false);
});

return await rootCommand.Parse(args).InvokeAsync().ConfigureAwait(continueOnCapturedContext: false);
