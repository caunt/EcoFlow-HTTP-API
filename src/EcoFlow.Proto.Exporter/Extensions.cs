using Google.Protobuf.Reflection;

namespace EcoFlow.Proto.Exporter;

public static class Extensions
{
    public static IEnumerable<int> IndexesOf(this string text, char symbol) => Enumerable.Range(0, text.Length).Where(index => text[index] == symbol);

    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> items) where T : class => items.Where(item => item is not null).Cast<T>();

    public static IAsyncEnumerable<T> WhereNotNull<T>(this IAsyncEnumerable<T?> items) where T : class => items.Where(item => item is not null).Cast<T>();

    public static IEnumerable<string> EnumerateAllMessageNames(this IReadOnlyList<FileDescriptor> fileDescriptors)
    {
        if (fileDescriptors is null)
        {
            yield break;
        }

        foreach (var fileDescriptor in fileDescriptors)
        {
            if (fileDescriptor is not null)
            {
                foreach (var messageDescriptor in fileDescriptor.MessageTypes)
                {
                    foreach (var messageName in EnumerateNestedMessageNames(messageDescriptor))
                    {
                        yield return messageName;
                    }
                }
            }
        }
    }

    public static IEnumerable<string> EnumerateNestedMessageNames(this MessageDescriptor messageDescriptor)
    {
        if (messageDescriptor is null)
        {
            yield break;
        }

        yield return messageDescriptor.FullName;

        foreach (var nestedMessageDescriptor in messageDescriptor.NestedTypes)
        {
            foreach (var nestedMessageName in EnumerateNestedMessageNames(nestedMessageDescriptor))
            {
                yield return nestedMessageName;
            }
        }
    }

    public static int CalculateMessageTypeCount(this IReadOnlyList<FileDescriptor> fileDescriptors)
    {
        return EnumerateAllMessageNames(fileDescriptors).Count();
    }
}
