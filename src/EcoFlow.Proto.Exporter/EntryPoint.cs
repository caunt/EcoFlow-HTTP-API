using EcoFlow.Mqtt.Api.Protobuf.Extraction;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;

var set = await ProtosReader.GetProtoSetAsync(@"C:\Users\caunt\Downloads\com.ecoflow.xapk");
Console.WriteLine(string.Join("\t", set.File.Select(proto => proto.Name)));
