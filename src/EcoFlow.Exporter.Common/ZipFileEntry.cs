namespace EcoFlow.Exporter.Common;

/// <summary>
/// A file found inside a ZIP archive, or inside an archive nested in it.
/// </summary>
/// <param name="FilePath">The path of the file, starting with the name of the outermost archive and continuing through every nested one.</param>
/// <param name="Content">The uncompressed content of the file, positioned at its start. The receiver owns the stream and disposes it.</param>
public sealed record ZipFileEntry(string FilePath, Stream Content);
