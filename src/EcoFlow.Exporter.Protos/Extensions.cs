using Google.Protobuf.Reflection;

namespace EcoFlow.Exporter.Protos;

public static class Extensions
{
    public static IEnumerable<string> EnumerateAllMessageNames(this IReadOnlyList<FileDescriptor> fileDescriptors)
    {
        if (fileDescriptors is null)
            yield break;

        foreach (var fileDescriptor in fileDescriptors)
        {
            if (fileDescriptor is null)
                continue;

            foreach (var messageDescriptor in fileDescriptor.MessageTypes)
                foreach (var messageName in EnumerateNestedMessageNames(messageDescriptor))
                    yield return messageName;
        }
    }

    public static IEnumerable<string> EnumerateNestedMessageNames(this MessageDescriptor messageDescriptor)
    {
        if (messageDescriptor is null)
            yield break;

        yield return messageDescriptor.FullName;

        foreach (var nestedMessageDescriptor in messageDescriptor.NestedTypes)
            foreach (var nestedMessageName in EnumerateNestedMessageNames(nestedMessageDescriptor))
                yield return nestedMessageName;
    }

    public static int CalculateMessageTypeCount(this IReadOnlyList<FileDescriptor> fileDescriptors)
    {
        return EnumerateAllMessageNames(fileDescriptors).Count();
    }
}
