using Google.Protobuf.Reflection;

namespace EcoFlow.Exporter.Protos;

internal sealed record ProtoItem(FileDescriptorProto FileDescriptorProto, string TypeDescriptor)
{
    private const int MinimumSeparatorCount = 3;

    // The Java outer class that holds the file, without its first package segment: ecoflow.common.proto.DeviceSys.
    public string ProtoName
    {
        get
        {
            string typeDescriptor = TypeDescriptor.EndsWith(value: ';', StringComparison.Ordinal)
                ? TypeDescriptor[..^1]
                : TypeDescriptor;

            return typeDescriptor.Count(static character => character is '.') >= MinimumSeparatorCount
                ? typeDescriptor[(typeDescriptor.IndexOf(value: '.', StringComparison.Ordinal) + 1)..]
                : typeDescriptor;
        }
    }
}
