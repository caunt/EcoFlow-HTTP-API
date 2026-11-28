using Google.Protobuf.Collections;
using Google.Protobuf.Reflection;

namespace EcoFlow.Exporter.Protos;

// Orders files so that every file comes after the files it imports, and the messages of a file so that every message
// comes after the messages its fields use. Whatever has no such tie keeps the order it was found in.
internal static class ProtoSorter
{
    public static IEnumerable<FileDescriptorProto> Sort(IEnumerable<FileDescriptorProto> files)
    {
        Dictionary<string, List<FileDescriptorProto>> filesByName = new(StringComparer.Ordinal);
        Dictionary<string, int> originalIndexByName = new(StringComparer.Ordinal);

        foreach ((int fileIndex, FileDescriptorProto currentFile) in files.Index())
        {
            if (string.IsNullOrEmpty(currentFile.Name))
                throw new InvalidOperationException(message: "FileDescriptorProto.Name must be set for dependency sorting.");

            if (!filesByName.TryGetValue(currentFile.Name, out List<FileDescriptorProto>? sameNameFiles))
            {
                sameNameFiles = [];

                filesByName.Add(currentFile.Name, sameNameFiles);
                originalIndexByName.Add(currentFile.Name, fileIndex);
            }

            sameNameFiles.Add(currentFile);
        }

        Dictionary<string, List<string>> dependencies = filesByName.ToDictionary(group => group.Key,
            group => group.Value
                .SelectMany(file => file.Dependency)
                .Distinct(StringComparer.Ordinal)
                .Where(dependency => !string.Equals(dependency, group.Key, StringComparison.Ordinal))
                .ToList(),
            StringComparer.Ordinal);

        foreach (KeyValuePair<string, List<string>> group in dependencies)
        {
            if (group.Value.Find(dependency => !filesByName.ContainsKey(dependency)) is { } missingDependency)
                throw new InvalidOperationException(message: $"Missing dependency '{missingDependency}' referenced by a file named '{group.Key}'.");
        }

        List<string> sortedNames = Order(dependencies, originalIndexByName, breakCycles: false);

        if (sortedNames.Count != filesByName.Count)
        {
            IEnumerable<string> remainingNames = filesByName.Keys
                .Except(sortedNames, StringComparer.Ordinal)
                .OrderBy(name => originalIndexByName[name]);

            throw new InvalidOperationException(message: $"Dependency cycle detected among: {string.Join(separator: ", ", remainingNames)}");
        }

        foreach (FileDescriptorProto file in sortedNames.SelectMany(name => filesByName[name]))
        {
            Sort(file.MessageType);

            yield return file;
        }
    }

    // Takes the names with nothing left to wait for, earliest found first, and releases whatever was waiting for them.
    private static List<string> Order(Dictionary<string, List<string>> dependencies, Dictionary<string, int> originalIndexByName, bool breakCycles)
    {
        Dictionary<string, int> pendingCountByName = dependencies.ToDictionary(pair => pair.Key,
            pair => pair.Value.Count,
            StringComparer.Ordinal);

        ILookup<string, string> dependentsByName = dependencies.SelectMany(pair => pair.Value.Select(dependency => KeyValuePair.Create(dependency, pair.Key)))
            .ToLookup(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        PriorityQueue<string, int> readyQueue = new();

        foreach (string readyName in pendingCountByName.Where(pair => pair.Value is 0).Select(pair => pair.Key))
            readyQueue.Enqueue(readyName, originalIndexByName[readyName]);

        List<string> sortedNames = new(dependencies.Count);

        while (sortedNames.Count < dependencies.Count)
        {
            if (readyQueue.Count is 0)
            {
                // Messages may use each other in a circle; the earliest one still waiting goes first.
                string? cycleBreakerName = breakCycles
                    ? pendingCountByName.Where(pair => pair.Value > 0)
                        .Select(pair => pair.Key)
                        .MinBy(name => originalIndexByName[name])
                    : null;

                if (cycleBreakerName is null)
                    break;

                readyQueue.Enqueue(cycleBreakerName, originalIndexByName[cycleBreakerName]);
            }

            string currentName = readyQueue.Dequeue();

            sortedNames.Add(currentName);

            pendingCountByName[currentName] = 0;

            foreach (string dependentName in dependentsByName[currentName].Where(dependentName => pendingCountByName[dependentName] > 0))
            {
                pendingCountByName[dependentName]--;

                if (pendingCountByName[dependentName] is 0)
                    readyQueue.Enqueue(dependentName, originalIndexByName[dependentName]);
            }
        }

        return sortedNames;
    }

    private static void Sort(RepeatedField<DescriptorProto> messages)
    {
        Dictionary<string, DescriptorProto> messagesByName = new(StringComparer.Ordinal);
        Dictionary<string, int> originalIndexByName = new(StringComparer.Ordinal);

        foreach ((int messageIndex, DescriptorProto currentMessage) in messages.Index())
        {
            if (string.IsNullOrEmpty(currentMessage.Name))
                throw new InvalidOperationException(message: "DescriptorProto.Name must be set for dependency sorting.");

            if (messagesByName.TryAdd(currentMessage.Name, currentMessage))
                originalIndexByName.Add(currentMessage.Name, messageIndex);
        }

        // A field uses a message of this file when its type is that name, or ends with it after a dot.
        Dictionary<string, List<string>> dependencies = messagesByName.ToDictionary(pair => pair.Key,
            pair => messagesByName.Keys
                .Where(knownName => !string.Equals(knownName, pair.Key, StringComparison.Ordinal) && pair.Value.Field.Any(field => UsesMessage(field, knownName)))
                .ToList(),
            StringComparer.Ordinal);

        List<string> sortedNames = Order(dependencies, originalIndexByName, breakCycles: true);

        foreach ((int writeIndex, string name) in sortedNames.Index())
            messages[writeIndex] = messagesByName[name];
    }

    private static bool UsesMessage(FieldDescriptorProto field, string messageName)
    {
        return field.TypeName is not null
            && (string.Equals(field.TypeName, messageName, StringComparison.Ordinal)
                || field.TypeName.EndsWith(value: $".{messageName}", StringComparison.Ordinal));
    }
}
