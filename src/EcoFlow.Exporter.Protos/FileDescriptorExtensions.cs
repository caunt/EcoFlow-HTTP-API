using Google.Protobuf.Reflection;

namespace EcoFlow.Exporter.Protos;

internal static class FileDescriptorExtensions
{
    public static int CalculateMessageTypeCount(this IReadOnlyList<FileDescriptor> fileDescriptors)
    {
        return fileDescriptors.EnumerateAllMessageNames().Count();
    }

    public static IEnumerable<string> EnumerateAllMessageNames(this IReadOnlyList<FileDescriptor> fileDescriptors)
    {
        return fileDescriptors.SelectMany(static fileDescriptor => fileDescriptor.MessageTypes)
            .SelectMany(EnumerateNestedMessageNames);
    }

    public static IEnumerable<string> EnumerateNestedMessageNames(this MessageDescriptor messageDescriptor)
    {
        return messageDescriptor.NestedTypes.SelectMany(EnumerateNestedMessageNames).Prepend(messageDescriptor.FullName);
    }
}
