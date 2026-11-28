using EcoFlow.Mqtt.Api.Configuration;
using EcoFlow.Mqtt.Api.Exceptions;
using EcoFlow.Mqtt.Api.Extensions;
using EcoFlow.Mqtt.Api.Models;
using EcoFlow.Mqtt.Api.Session;

using Ecoflow.Cbb.Databus.Proto.Common;

using Google.Protobuf;

using Microsoft.Extensions.Hosting;

using MQTTnet;
using MQTTnet.Exceptions;

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace EcoFlow.Mqtt.Api.Services;

internal sealed class InternalMqttApi(EcoFlowConfiguration configuration) : IHostedService
{
    public const string UpdatedAtProperty = "____updatedAtUtcSeconds";

    private const int MinimumTopicPartCount = 5;

    private static readonly UTF8Encoding StrictEncoding = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // The clients whose connection was lost, read by whoever is responsible for connecting again.
    private readonly Channel<IMqttClient> _disconnections = Channel.CreateUnbounded<IMqttClient>();

    // A device state is never changed once it is here: every update puts a new one in its place, so readers need no lock.
    private readonly ConcurrentDictionary<string, DeviceState> _devices = [];

    private IMqttClient? _client;

    public IEnumerable<string> SerialNumbers => _devices.Keys.Order(StringComparer.Ordinal);

    // Connects with a new client and subscribes the devices. Called again after a lost connection, it keeps their states.
    public async Task ConnectAsync(ISession session,
        MqttConfiguration mqttConfiguration,
        IReadOnlyCollection<Device> devices,
        CancellationToken cancellationToken = default)
    {
        IMqttClient client = new MqttClientFactory().CreateMqttClient();

        client.ApplicationMessageReceivedAsync += OnApplicationMessageReceivedAsync;
        client.DisconnectedAsync += eventArguments => _disconnections.Writer.WriteAsync(client, CancellationToken.None).AsTask();

        await ConnectAsync(client, session, mqttConfiguration, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        Interlocked.Exchange(ref _client, client)?.Dispose();

        foreach (Device device in devices)
        {
            DeviceState deviceState = _devices.AddOrUpdate(device.SerialNumber,
                serialNumber => new DeviceState(device, "{}"u8.ToArray()),
                (serialNumber, deviceState) => deviceState with
                {
                    Device = device
                });

            MqttClientSubscribeOptionsBuilder subscribeOptionsBuilder = new();

            foreach (string topic in GetTopics(session, mqttConfiguration, deviceState.Device))
                subscribeOptionsBuilder = subscribeOptionsBuilder.WithTopicFilter(topic);

            MqttClientSubscribeResult subscription = await client.SubscribeAsync(subscribeOptionsBuilder.Build(), cancellationToken)
                .ConfigureAwait(continueOnCapturedContext: false);

            foreach (MqttClientSubscribeResultItem item in subscription.Items
                .Where(static item => item.ResultCode > MqttClientSubscribeResultCode.GrantedQoS2))
                Console.WriteLine(value: $"⚠️ Subscription to {item.TopicFilter.Topic} was refused: {item.ResultCode}");
        }
    }

    public ReadOnlyMemory<byte> GetDevices()
    {
        ArrayBufferWriter<byte> buffer = new();

        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();

            foreach (KeyValuePair<string, DeviceState> device in _devices.OrderBy(static device => device.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(device.Key);
                writer.WriteRawValue(device.Value.State.Span, skipInputValidation: true);
            }

            writer.WriteEndObject();
        }

        return buffer.WrittenMemory;
    }

    public async Task SendMessageAsync(Device device, string topic, string payload, CancellationToken cancellationToken)
    {
        // Nothing can be sent while the connection is being restored, the next polling round sends again.
        if (_client is not { IsConnected: true } client || !_devices.ContainsKey(device.SerialNumber))
            return;

        MqttApplicationMessage message = new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(payload).Build();

        try
        {
            MqttClientPublishResult publication = await client.PublishAsync(message, cancellationToken)
                .ConfigureAwait(continueOnCapturedContext: false);

            if (!publication.IsSuccess)
                Console.WriteLine(value: $"⚠️ Failed to publish to {topic}: {publication.ReasonCode}");
        }
        catch (MqttCommunicationException exception)
        {
            Console.WriteLine(value: $"⚠️ Failed to publish to {topic}: {exception.Message}");
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _client, value: null) is not { } client)
            return;

        using (client)
            await client.DisconnectAsync(cancellationToken: cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }

    public bool TryGetDevice(string serialNumber, out ReadOnlyMemory<byte> state)
    {
        bool isKnown = _devices.TryGetValue(serialNumber, out DeviceState? deviceState);

        state = deviceState?.State ?? default;

        return isKnown;
    }

    public async Task WaitForDisconnectionAsync(CancellationToken cancellationToken)
    {
        IMqttClient disconnectedClient;

        // A client that was replaced in the meantime also reports that it disconnected, only the current one matters.
        do
        {
            disconnectedClient = await _disconnections.Reader
                .ReadAsync(cancellationToken)
                .ConfigureAwait(continueOnCapturedContext: false);
        }
        while (!ReferenceEquals(disconnectedClient, _client));
    }

    private static async Task ConnectAsync(IMqttClient client, ISession session, MqttConfiguration mqttConfiguration, CancellationToken cancellationToken)
    {
        string clientId = session is AppSession appSession
            ? $"ANDROID_{Guid.NewGuid().ToString().ToUpperInvariant()}_{appSession.User.Id}"
            : "github.com/caunt/EcoFlow-MQTT-API";

        MqttClientOptionsBuilder optionsBuilder = new MqttClientOptionsBuilder().WithTcpServer(mqttConfiguration.Url, mqttConfiguration.Port)
            .WithCredentials(mqttConfiguration.Username, mqttConfiguration.Password)
            .WithClientId(clientId);

        if (mqttConfiguration.Tls)
        {
            optionsBuilder = optionsBuilder.WithTlsOptions(new MqttClientTlsOptions
            {
                UseTls = true
            });
        }

        MqttClientConnectResult connection = await client.ConnectAsync(optionsBuilder.Build(), cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        if (connection.ResultCode is not MqttClientConnectResultCode.Success)
            throw new InvalidOperationException(message: $"MQTT broker refused the connection: {connection.ResultCode} {connection.ReasonString}");
    }

    private static List<JsonNode> DecodeMessages(Device device, ReadOnlySequence<byte> payload)
    {
        List<JsonNode> nodes = [];

        if (TryDecode(payload, out string? text))
        {
            nodes.Add(JsonNode.Parse(text) is JsonObject envelope && envelope[propertyName: "params"] is JsonNode parameters ? parameters : throw new InvalidOperationException(message: $"Failed to parse JSON payload: {text}"));

            return nodes;
        }

        foreach (Header header in Send_Header_Msg.Parser.ParseFrom(payload).Decrypted)
        {
            IMessage message = header.Pdata.AsEcoFlowMessage(header.CmdFunc, header.CmdId);
            string messageName = message.GetType().Name;
            bool isUpload = messageName.EndsWith(value: "PropertyUpload", StringComparison.OrdinalIgnoreCase);
            bool isHeartBeat = messageName.Contains(value: "HeartBeatReport", StringComparison.OrdinalIgnoreCase);

            if (isUpload || isHeartBeat)
                nodes.Add(message.ToJson());
            else
                Console.WriteLine(value: $"⚠️ Unsupported binary message received from {device}: {message.ToStringWithTitle()}");
        }

        return nodes;
    }

    private static string Describe(ReadOnlySequence<byte> payload)
    {
        return TryDecode(payload, out string? text) ? text : Convert.ToHexString(payload.ToArray());
    }

    private static string[] GetTopics(ISession session, MqttConfiguration mqttConfiguration, Device device)
    {
        return session is AppSession appSession
            ? [
                $"/app/device/property/{device.SerialNumber}",
                $"/app/{appSession.User.Id}/{device.SerialNumber}/thing/property/set",
                $"/app/{appSession.User.Id}/{device.SerialNumber}/thing/property/set_reply",
                $"/app/{appSession.User.Id}/{device.SerialNumber}/thing/property/get",
                $"/app/{appSession.User.Id}/{device.SerialNumber}/thing/property/get_reply",
            ]
            : [
            $"/open/{mqttConfiguration.Username}/{device.SerialNumber}/quota",
            $"/open/${mqttConfiguration.Username}/${device.SerialNumber}/set",
            $"/open/${mqttConfiguration.Username}/${device.SerialNumber}/set_reply",
            $"/open/${mqttConfiguration.Username}/${device.SerialNumber}/get",
            $"/open/${mqttConfiguration.Username}/${device.SerialNumber}/get_reply",
        ];
    }

    private static ReadOnlyMemory<byte> Merge(ReadOnlyMemory<byte> state, List<JsonNode> nodes)
    {
        JsonNode merged = JsonNode.Parse(state.Span) ?? new JsonObject();

        foreach (JsonNode node in nodes)
            merged.MergeWith(node);

        merged[UpdatedAtProperty] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        return Encoding.UTF8.GetBytes(merged.ToJsonString());
    }

    private static bool TryDecode(ReadOnlySequence<byte> payload, [MaybeNullWhen(false)] out string text)
    {
        try
        {
            text = StrictEncoding.GetString(payload);

            return true;
        }
        catch (DecoderFallbackException)
        {
            text = null;

            return false;
        }
    }

    // {"operateType":"latestQuotas","code":"0","data":{"online":1,"quotaMap":{...}}}
    private static bool TryParseLatestQuotas(ReadOnlySequence<byte> payload, [MaybeNullWhen(false)] out JsonNode quotaMap)
    {
        quotaMap = null;

        if (!TryDecode(payload, out string? text) || JsonNode.Parse(text) is not JsonObject reply)
            return false;

        if (reply[propertyName: "operateType"]?.ToString() is not "latestQuotas" || reply[propertyName: "code"]?.ToString() is not "0")
            return false;

        // The cloud may answer with the last known quotas of an offline device; don't treat that as fresh data.
        if (reply[propertyName: "data"] is not JsonObject data || data[propertyName: "online"]?.ToString() is "0")
            return false;

        if (data[propertyName: "quotaMap"] is not JsonObject map)
            return false;

        quotaMap = map.DeepClone();

        return true;
    }

    private Device GetDevice(string serialNumber)
    {
        return _devices.TryGetValue(serialNumber, out DeviceState? deviceState)
            ? deviceState.Device
            : throw new InvalidOperationException(message: $"No subscribed device has the serial number {serialNumber}.");
    }

    private void HandleMessage(string topic, ReadOnlySequence<byte> payload)
    {
        string[] parts = topic.Split(separator: '/');

        if (parts.Length < MinimumTopicPartCount)
        {
            Console.WriteLine(value: $"⚠️ Invalid topic received: {topic}");

            return;
        }

        // /app/{userId}/{deviceSerialNumber}/thing/property/get_reply (reply to latestQuotas polling)
        if (parts is [_, "app", _, string replySerialNumber, "thing", "property", "get_reply"])
        {
            if (TryParseLatestQuotas(payload, out JsonNode? quotaMap))
                Update(GetDevice(replySerialNumber), nodes: [quotaMap]);
            else if (configuration.VerboseLogging)
                Console.WriteLine(value: $"⚠️ Unknown topic received: {topic} => {Describe(payload)}");

            return;
        }

        // /app/device/property/{deviceSerialNumber}
        // /open/{mqttConfiguration.Username}/{deviceSerialNumber}/quota
        string? serialNumber = parts switch
        {
            [_, "app", "device", "property", string appSerialNumber, ..] => appSerialNumber,
            [_, "open", _, string openSerialNumber, "quota", ..] => openSerialNumber,
            _ => null
        };

        if (serialNumber is null)
        {
            if (configuration.VerboseLogging)
                Console.WriteLine(value: $"⚠️ Unknown topic received: {topic} => {Describe(payload)}");

            return;
        }

        if (string.IsNullOrWhiteSpace(serialNumber))
        {
            Console.WriteLine(value: $"⚠️ Invalid topic received: {topic}");

            return;
        }

        Device device = GetDevice(serialNumber);

        Update(device, DecodeMessages(device, payload));
    }

    private async Task OnApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs eventArguments)
    {
        try
        {
            HandleMessage(eventArguments.ApplicationMessage.Topic, eventArguments.ApplicationMessage.Payload);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // One payload that cannot be understood must not stop the ones that follow it.
            if (configuration.VerboseLogging || exception is not MessageMultipleMatchesException)
            {
                await Console.Error
                    .WriteLineAsync(value: $"⚠️ Error processing MQTT message: {exception.Message}")
                    .ConfigureAwait(continueOnCapturedContext: false);
            }
        }
    }

    private void Update(Device device, List<JsonNode> nodes)
    {
        if (nodes.Count is 0)
            return;

        // The merge only reads the state it is given, so it is safe to run again if another update got there first.
        DeviceState updatedState = _devices.AddOrUpdate(device.SerialNumber,
            serialNumber => throw new InvalidOperationException(message: $"No subscribed device has the serial number {serialNumber}."),
            (serialNumber, deviceState) => deviceState with
            {
                State = Merge(deviceState.State, nodes)
            });

        Console.WriteLine(value: $"🖥  Updated state for {updatedState.Device}");
    }

    private sealed record DeviceState(Device Device, ReadOnlyMemory<byte> State);
}
