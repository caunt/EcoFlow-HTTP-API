using AlphaOmega.Debug;
using AlphaOmega.Debug.Dex.Tables;

using EcoFlow.Exporter.Common;

using Google.Protobuf.Reflection;

using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;

namespace EcoFlow.Exporter.Protos;

// Protobuf for Java keeps the serialized descriptor of every .proto file as string constants that the static initializer
// of its outer class joins together. Those constants are read back out of the Dalvik bytecode of the app.
internal static class DexProtoReader
{
    private const int ChannelCapacity = 100;

    // The Dalvik instructions that load a string constant or fill an array from registers.
    private const int ConstString = 0x1A;
    private const int ConstStringJumbo = 0x1B;
    private const int FilledNewArray = 0x24;
    private const int FilledNewArrayRange = 0x25;

    private const string DescriptorFieldName = "descriptor";
    private const string DescriptorFieldType = "com.google.protobuf.Descriptors$FileDescriptor;";
    private const string StaticInitializerName = "<clinit>";
    private const string StringArrayType = "[Ljava/lang/String;";

    public static IEnumerable<ProtoItem> Enumerate(DexFile dex)
    {
        foreach (type_id_row? typeIdRow in dex.TYPE_ID_ITEM)
        {
            class_def_row? classDefRow = dex.CLASS_DEF_ITEM
                .FirstOrDefault(classDefRow => classDefRow.class_idx.TypeDescriptor == typeIdRow.TypeDescriptor);

            if (classDefRow?.class_data_off is not { } classDataRow)
                continue;

            bool hasDescriptor = classDataRow.static_fields
                .Any(encodedFieldRow => dex.FIELD_ID_ITEM[encodedFieldRow.field_idx_diff] is { name_idx.data: DescriptorFieldName, type_idx.TypeDescriptor: DescriptorFieldType });

            if (!hasDescriptor)
                continue;

            foreach (encoded_method_row? encodedMethodRow in classDataRow.direct_methods)
            {
                if (dex.METHOD_ID_ITEM[encodedMethodRow.method_idx_diff].name_idx.data is not StaticInitializerName)
                    continue;

                if (ReadDescriptor(dex, encodedMethodRow.code_off.insns, encodedMethodRow.code_off.registers_size) is { } descriptor)
                    yield return new ProtoItem(FileDescriptorProto.Parser.ParseFrom(Encoding.Latin1.GetBytes(descriptor)), typeIdRow.TypeDescriptor);
            }
        }
    }

    public static async IAsyncEnumerable<ProtoItem> EnumerateParallelAsync(string apkFileName, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Channel<ProtoItem> outputChannel = Channel.CreateBounded<ProtoItem>(new BoundedChannelOptions(ChannelCapacity)
        {
            SingleWriter = false,
            SingleReader = true
        });

        Task writingTask = Task.Run(async () =>
        {
            try
            {
                IEnumerable<ZipFileEntry> dexFiles = ZipReader.EnumerateFilesRecursively(apkFileName,
                    fileName => fileName.EndsWith(value: ".dex", StringComparison.OrdinalIgnoreCase));

                await Parallel
                    .ForEachAsync(dexFiles,
                        cancellationToken,
                        async (entry, token) =>
                        {
                            using DexFile dexFile = new(new StreamLoader(entry.Content));

                            foreach (ProtoItem item in Enumerate(dexFile))
                            {
                                await outputChannel.Writer
                                    .WriteAsync(item, token)
                                    .ConfigureAwait(continueOnCapturedContext: false);
                            }
                        })
                    .ConfigureAwait(continueOnCapturedContext: false);
            }
            finally
            {
                // The reader stops either way, and a failure is thrown where the task is awaited below.
                outputChannel.Writer.Complete();
            }
        },
            cancellationToken);

        await foreach (ProtoItem item in outputChannel.Reader
            .ReadAllAsync(cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false))
            yield return item;

        await writingTask.ConfigureAwait(continueOnCapturedContext: false);
    }

    private static void EnsureStringArray(DexFile dex, uint typeIndex)
    {
        string typeDescriptor = dex.TYPE_ID_ITEM[typeIndex].TypeDescriptor;

        if (typeDescriptor is not StringArrayType)
            throw new InvalidOperationException(message: $"Unexpected type descriptor: {typeDescriptor}");
    }

    private static string GetString(string?[] registers, int registerIndex)
    {
        return registerIndex >= registers.Length
            ? throw new InvalidOperationException(message: $"Register {registerIndex} is out of range.")
            : registers[registerIndex]
                ?? throw new InvalidOperationException(message: $"Register {registerIndex} does not contain a string part.");
    }

    // Follows the string constants loaded into registers until the instruction that puts them in an array.
    private static string? ReadDescriptor(DexFile dex, ushort[] instructions, int registerCount)
    {
        string?[] registers = new string?[registerCount];

        for (int index = 0; index < instructions.Length; index++)
        {
            int instruction = instructions[index];
            int operand = instruction >> 8;

            switch (instruction & 0xFF)
            {
                case ConstString:
                    {
                        registers[operand] = dex.STRING_DATA_ITEM[instructions[++index]].data;

                        break;
                    }
                case ConstStringJumbo:
                    {
                        uint low = instructions[++index];
                        uint high = instructions[++index];

                        registers[operand] = dex.STRING_DATA_ITEM[low | (high << 16)].data;

                        break;
                    }
                case FilledNewArray:
                    {
                        EnsureStringArray(dex, instructions[++index]);

                        // Up to five registers: four in the next code unit, the fifth in the instruction itself.
                        int arguments = instructions[++index];
                        int[] registerIndexes = [arguments & 0x0F, (arguments >> 4) & 0x0F, (arguments >> 8) & 0x0F, (arguments >> 12) & 0x0F, operand & 0x0F];

                        return string.Concat(registerIndexes.Take(operand >> 4)
                            .Where(registerIndex => registerIndex < registers.Length)
                            .Select(registerIndex => GetString(registers, registerIndex)));
                    }
                case FilledNewArrayRange:
                    {
                        EnsureStringArray(dex, instructions[++index]);

                        int firstRegisterIndex = instructions[++index];

                        return string.Concat(Enumerable.Range(firstRegisterIndex, operand)
                            .Select(registerIndex => GetString(registers, registerIndex)));
                    }
                default:
                    break;
            }
        }

        return null;
    }
}
