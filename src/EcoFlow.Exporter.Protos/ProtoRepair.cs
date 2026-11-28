using Google.Protobuf.Reflection;

using System.Globalization;

using FileOptions = Google.Protobuf.Reflection.FileOptions;

namespace EcoFlow.Exporter.Protos;

// Descriptors compiled into the app lost the paths and packages their .proto files had, and several of them clash once
// they are put side by side. The steps here give every file a unique place and make the references between them valid.
internal static class ProtoRepair
{
    private static readonly string[] ReservedPrefixes = ["has", "get", "set", "clear", "add", "remove"];

    public static void Repair(IReadOnlyList<ProtoItem> items, string? goPackagePrefix)
    {
        foreach (ProtoItem item in items)
            RepairName(item);

        foreach (ProtoItem item in items)
            RepairPackage(item.FileDescriptorProto);

        foreach (ProtoItem item in items)
            RepairOptions(item.FileDescriptorProto, goPackagePrefix);

        foreach (ProtoItem item in items)
            RepairDependencies(item.FileDescriptorProto, items);

        foreach (ProtoItem item in items)
            RepairTypeNames(item.FileDescriptorProto, items);

        foreach (ProtoItem item in items)
            RepairFieldNames(item.FileDescriptorProto.MessageType);
    }

    private static IEnumerable<string> CollectTypes(IEnumerable<DescriptorProto> descriptors, string prefix)
    {
        foreach (DescriptorProto descriptor in descriptors)
        {
            string fullTypeName = $"{prefix}.{descriptor.Name}";

            yield return fullTypeName;

            foreach (string nestedType in CollectTypes(descriptor.NestedType, fullTypeName))
                yield return nestedType;

            foreach (EnumDescriptorProto enumType in descriptor.EnumType)
                yield return $"{fullTypeName}.{enumType.Name}";
        }
    }

    private static int CountCommonPrefixLength(string left, string right)
    {
        return left.Zip(right).TakeWhile(static pair => pair.First == pair.Second).Count();
    }

    // Every type a file can refer to: its own, then those of the files it imports.
    private static List<string> GetAvailableTypes(FileDescriptorProto fileDescriptorProto, IReadOnlyList<ProtoItem> items)
    {
        IEnumerable<FileDescriptorProto> dependencies = fileDescriptorProto.Dependency
            .Select(dependencyName => items.FirstOrDefault(item => item.FileDescriptorProto.Name == dependencyName))
            .OfType<ProtoItem>()
            .Select(item => item.FileDescriptorProto);

        return [.. dependencies.Prepend(fileDescriptorProto).SelectMany(GetTypes)];
    }

    private static IEnumerable<string> GetTypes(FileDescriptorProto fileDescriptorProto)
    {
        string prefix = $".{fileDescriptorProto.Package}";

        return CollectTypes(fileDescriptorProto.MessageType, prefix).Concat(fileDescriptorProto.EnumType
            .Select(enumType => $"{prefix}.{enumType.Name}"));
    }

    private static void RepairDependencies(FileDescriptorProto fileDescriptorProto, IReadOnlyList<ProtoItem> items)
    {
        for (int index = 0; index < fileDescriptorProto.Dependency.Count; index++)
        {
            string dependency = fileDescriptorProto.Dependency[index];

            if (items.Any(item => item.FileDescriptorProto.Name == dependency))
                continue;

            // Several files can end with the imported name, the one closest to the importing file is meant.
            FileDescriptorProto candidate = items.Select(item => item.FileDescriptorProto)
                .Where(other => other.Name.EndsWith(dependency, StringComparison.Ordinal))
                .MaxBy(other => CountCommonPrefixLength(fileDescriptorProto.Name, other.Name))
                ?? throw new InvalidOperationException(message: "No candidate items.");

            if (CountCommonPrefixLength(fileDescriptorProto.Name, candidate.Name) is 0)
                throw new InvalidOperationException(message: $"No candidate has a common prefix with dependency: {dependency}");

            fileDescriptorProto.Dependency[index] = candidate.Name;
        }
    }

    // Generated accessors such as hasValue or getValue clash with a field that is itself called has_value or get_value.
    private static void RepairFieldNames(IEnumerable<DescriptorProto> descriptors)
    {
        foreach (DescriptorProto descriptor in descriptors)
        {
            foreach (FieldDescriptorProto field in descriptor.Field)
            {
                bool hasCollision = descriptor.Field
                    .Any(otherField => ReservedPrefixes.Any(prefix => string.Equals(otherField.Name, b: $"{prefix}_{field.Name}", StringComparison.OrdinalIgnoreCase)));

                if (hasCollision)
                    field.Name = $"value_of_{field.Name}";
            }

            RepairFieldNames(descriptor.NestedType);
        }
    }

    private static void RepairName(ProtoItem item)
    {
        FileDescriptorProto fileDescriptorProto = item.FileDescriptorProto;

        // If package is nested, the file is properly authored (e.g. google/protobuf) and its name is correct
        if (fileDescriptorProto.Package.Contains(value: '.', StringComparison.Ordinal))
            return;

        // Name may still be a build path shared by unrelated files (e.g. app/src/main/proto),
        // so keep only the file name and scope it by the Java outer class, which is unique per file
        fileDescriptorProto.Name = $"{item.ProtoName.Replace(oldChar: '.', newChar: '/')}/{Path.GetFileName(fileDescriptorProto.Name)}";
    }

    private static void RepairOptions(FileDescriptorProto fileDescriptorProto, string? goPackagePrefix)
    {
        FileOptions options = fileDescriptorProto.Options ??= new FileOptions();

        // If go_package is present, the file is properly authored (e.g. google/protobuf) and its options are correct
        if (options.HasGoPackage)
            return;

        // Java package is shared by unrelated files, which breaks Kotlin, so fall back to the unique proto package
        options.ClearJavaPackage();

        if (goPackagePrefix is null)
            return;

        // Go cannot derive import path from the proto package, so build it from the file location
        string directory = Path.GetDirectoryName(fileDescriptorProto.Name)?.Replace(oldChar: '\\', newChar: '/')
            ?? throw new InvalidOperationException(message: $"Unexpected proto name format: '{fileDescriptorProto.Name}'");

        options.GoPackage = $"{goPackagePrefix.TrimEnd(trimChar: '/')}/{directory};{CultureInfo.InvariantCulture.TextInfo.ToLower(Path.GetFileName(directory))}";
    }

    private static void RepairPackage(FileDescriptorProto fileDescriptorProto)
    {
        // If package is nested, it is likely correct and we can skip it
        if (fileDescriptorProto.Package.Contains(value: '.', StringComparison.Ordinal))
            return;

        string name = fileDescriptorProto.Name;
        int extensionIndex = name.LastIndexOf(value: '.', StringComparison.Ordinal);

        if (extensionIndex < 0)
            throw new InvalidOperationException(message: $"Unexpected proto name format: '{name}'");

        // The package is the directory of the file: the name without its extension and its last segment.
        string package = name[..extensionIndex];
        int lastDirectoryIndex = package.LastIndexOf(value: '/', StringComparison.Ordinal);

        fileDescriptorProto.Package = (lastDirectoryIndex > 0 ? package[..lastDirectoryIndex] : package).Replace(oldChar: '/', newChar: '.');
    }

    private static void RepairTypeNames(FileDescriptorProto fileDescriptorProto, IReadOnlyList<ProtoItem> items)
    {
        RepairTypeNames(fileDescriptorProto.MessageType, GetAvailableTypes(fileDescriptorProto, items));
    }

    private static void RepairTypeNames(IEnumerable<DescriptorProto> descriptors, List<string> availableTypes)
    {
        foreach (DescriptorProto descriptor in descriptors)
        {
            foreach (FieldDescriptorProto field in descriptor.Field.Where(field => field.HasTypeName))
            {
                string[] typeParts = field.TypeName.TrimStart(trimChar: '.').Split(separator: '.');

                // The longest ending of the name that some known type has wins, so the most specific match is kept.
                string? matchingType = Enumerable.Range(start: 0, typeParts.Length)
                    .Select(partIndex => $".{string.Join(separator: '.', typeParts[partIndex..])}")
                    .Select(suffix => availableTypes.FirstOrDefault(availableType => availableType.EndsWith(suffix, StringComparison.Ordinal)))
                    .FirstOrDefault(availableType => availableType is not null);

                if (matchingType is not null)
                    field.TypeName = matchingType;
            }

            RepairTypeNames(descriptor.NestedType, availableTypes);
        }
    }
}
