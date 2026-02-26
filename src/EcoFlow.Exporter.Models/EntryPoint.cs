using EcoFlow.Exporter.Models;
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
    Description = "The path to the output .json file",
    DefaultValueFactory = result => new FileInfo("models.json")
};

var rootCommand = new RootCommand("Extracts model set from XAPK file")
{
    inputFilePathOption,
    outputFilePathOption
};

rootCommand.SetAction(async (result, cancellationToken) =>
{
    var inputFileInfo = result.GetRequiredValue(inputFilePathOption);
    var outputFileInfo = result.GetRequiredValue(outputFilePathOption);

    var models = await ModelsReader.GetAsync(inputFileInfo.FullName, cancellationToken);
    await File.WriteAllTextAsync(outputFileInfo.FullName, models.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
});

return await rootCommand.Parse(args).InvokeAsync();

