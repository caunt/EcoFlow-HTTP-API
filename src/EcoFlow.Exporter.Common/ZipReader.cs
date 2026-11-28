using System.IO.Compression;

namespace EcoFlow.Exporter.Common;

/// <summary>
/// Reads the files of a ZIP archive together with the files of every archive nested inside it, such as the APKs of an XAPK.
/// </summary>
public static class ZipReader
{
    private static ReadOnlySpan<byte> ZipHeader => [0x50, 0x4B, 0x03, 0x04];

    /// <summary>
    /// Enumerates the files of a ZIP archive, descending into every file that is itself a ZIP archive.
    /// </summary>
    /// <param name="zipFilePath">The path of the archive to open for reading.</param>
    /// <param name="filterPredicate">
    /// Decides by its path whether a file is returned. Every file is returned when it is <see langword="null"/>. Nested archives are
    /// always searched, whatever the predicate says about them.
    /// </param>
    /// <returns>
    /// The matching files, each uncompressed into memory when the enumeration reaches it. The archive stays open until the
    /// enumeration ends.
    /// </returns>
    public static IEnumerable<ZipFileEntry> EnumerateFilesRecursively(string zipFilePath, Predicate<string>? filterPredicate = null)
    {
        using FileStream rootFileStream = new(zipFilePath, FileMode.Open, FileAccess.Read);

        foreach (ZipFileEntry entry in EnumerateZipStream(rootFileStream, Path.GetFileName(zipFilePath), filterPredicate))
            yield return entry;
    }

    private static IEnumerable<ZipFileEntry> EnumerateZipStream(Stream inputStream, string parentPath, Predicate<string>? filterPredicate)
    {
        using ZipArchive zipArchive = new(inputStream, ZipArchiveMode.Read, leaveOpen: true);

        foreach (ZipArchiveEntry zipArchiveEntry in zipArchive.Entries)
        {
            if (string.IsNullOrEmpty(zipArchiveEntry.Name))
                continue;

            string fullEntryPath = Path.Combine(parentPath, zipArchiveEntry.FullName);
            MemoryStream uncompressedStream = new();

            using (Stream entryStream = zipArchiveEntry.Open())
                entryStream.CopyTo(uncompressedStream);

            uncompressedStream.Position = 0;

            if (IsZipHeader(uncompressedStream))
            {
                foreach (ZipFileEntry nestedEntry in EnumerateZipStream(uncompressedStream, fullEntryPath, filterPredicate))
                    yield return nestedEntry;
            }
            else if (filterPredicate is null || filterPredicate(fullEntryPath))
            {
                yield return new ZipFileEntry(fullEntryPath, uncompressedStream);
            }
        }
    }

    private static bool IsZipHeader(Stream stream)
    {
        if (stream.Length < ZipHeader.Length)
            return false;

        Span<byte> header = stackalloc byte[ZipHeader.Length];

        stream.ReadExactly(header);

        stream.Position = 0;

        return header.SequenceEqual(ZipHeader);
    }
}
