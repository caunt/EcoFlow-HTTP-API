using Google.Protobuf;

using CommonHeader = Ecoflow.Cbb.Databus.Proto.Common.Header;
using CommonHeaders = Ecoflow.Cbb.Databus.Proto.Common.Send_Header_Msg;
using PlatformHeader = Ecoflow.Wn100Module.Pb.WnCommon.Header;
using PlatformHeaders = Ecoflow.Wn100Module.Pb.WnCommon.Send_Header_Msg;

namespace EcoFlow.Mqtt.Api.Extensions;

internal static class EcoFlowExtensions
{
    private const int EncryptedType = 1;

    // Payloads are obscured by combining every byte with the low byte of the sequence number of their header.
    private static ByteString Decrypt(ByteString byteString, int sequence)
    {
        byte key = byte.CreateTruncating(sequence);
        byte[] decrypted = byteString.ToByteArray();

        for (int index = 0; index < decrypted.Length; index++)
            decrypted[index] ^= key;

        return UnsafeByteOperations.UnsafeWrap(decrypted);
    }

    extension(CommonHeaders headers)
    {
        public IEnumerable<CommonHeader> Decrypted
        {
            get
            {
                foreach (CommonHeader header in headers.Msg)
                {
                    if (header.EncType is EncryptedType)
                        header.Pdata = Decrypt(header.Pdata, header.Seq);

                    yield return header;
                }
            }
        }
    }

    extension(PlatformHeaders headers)
    {
        public IEnumerable<PlatformHeader> Decrypted
        {
            get
            {
                foreach (PlatformHeader header in headers.Msg)
                {
                    if (header.EncType is EncryptedType)
                        header.Pdata = Decrypt(header.Pdata, header.Seq);

                    yield return header;
                }
            }
        }
    }
}
