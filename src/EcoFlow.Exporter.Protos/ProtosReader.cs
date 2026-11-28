using Google.Protobuf.Reflection;

namespace EcoFlow.Exporter.Protos;

internal static class ProtosReader
{
    public static async Task<FileDescriptorSet> GetProtoSetAsync(string apkFileName, Action<ProtoItem> onExtracted, string? goPackagePrefix = null, CancellationToken cancellationToken = default)
    {
        List<ProtoItem> items = [];

        await foreach (ProtoItem item in DexProtoReader.EnumerateParallelAsync(apkFileName, cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false))
        {
            onExtracted(item);
            items.Add(item);
        }

        ProtoRepair.Repair(items, goPackagePrefix);

        FileDescriptorSet set = new();

        set.File.AddRange(ProtoSorter.Sort(items.Select(static item => item.FileDescriptorProto)));

        return set;
    }
}
