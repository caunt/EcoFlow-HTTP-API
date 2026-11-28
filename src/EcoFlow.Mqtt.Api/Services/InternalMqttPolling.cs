using EcoFlow.Mqtt.Api.Configuration;
using EcoFlow.Mqtt.Api.Models;
using EcoFlow.Mqtt.Api.Session;

using Microsoft.Extensions.Hosting;

using Nito.Disposables;

using System.Collections.Immutable;
using System.Globalization;

namespace EcoFlow.Mqtt.Api.Services;

internal sealed class InternalMqttPolling(EcoFlowConfiguration configuration, InternalMqttApi mqttApi) : BackgroundService
{
    private const int FirstSequence = 1000;
    private const int SequenceCount = 9000;

    private ImmutableDictionary<Device, MqttConfiguration> _devices = [];
    private long _sequence = -1;

    public IDisposable RegisterDevices(MqttConfiguration mqttConfiguration, IReadOnlyCollection<Device> devices)
    {
        _devices = _devices.SetItems(devices.Select(device => KeyValuePair.Create(device, mqttConfiguration)));

        return Disposable.Create(() => _devices = _devices.RemoveRange(devices));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan pollingInterval = configuration.PollingInterval;

        if (pollingInterval <= TimeSpan.Zero)
        {
            await Console.Out
                .WriteLineAsync("⚠️ Polling interval is disabled.".AsMemory(), stoppingToken)
                .ConfigureAwait(continueOnCapturedContext: false);

            return;
        }

        using PeriodicTimer periodicTimer = new(pollingInterval);

        do
        {
            if (stoppingToken.IsCancellationRequested)
                break;

            await SendAsync(stoppingToken).ConfigureAwait(continueOnCapturedContext: false);
        }
        while (await periodicTimer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(continueOnCapturedContext: false));
    }

    // Five digits of the current time followed by four of a counter, like the identifiers the official app sends.
    private string CreateId()
    {
        long sequence = FirstSequence + (Interlocked.Increment(ref _sequence) % SequenceCount);

        return $"{DateTime.Now.ToString(format: "ssfff", CultureInfo.InvariantCulture)}{sequence.ToString(CultureInfo.InvariantCulture)}";
    }

    private async Task SendAsync(CancellationToken cancellationToken)
    {
        foreach (KeyValuePair<Device, MqttConfiguration> registration in _devices)
        {
            Device device = registration.Key;

            string topic = device.Session switch
            {
                AppSession appSession => $"/app/{appSession.User.Id}/{device.SerialNumber}/thing/property/set",
                OpenSession => $"/open/${registration.Value.Username}/${device.SerialNumber}/set",
                _ => throw new NotSupportedException(message: $"Unsupported session type: {device.Session}")
            };

            string payload = $$"""{"params":{},"operateType":"getAllTaskCfg","from":"Android","lang":"en-us","id":"{{CreateId()}}","moduleSn":"{{device.SerialNumber}}","moduleType":1,"version":"1.0"}""";

            await mqttApi.SendMessageAsync(device, topic, payload, cancellationToken)
                .ConfigureAwait(continueOnCapturedContext: false);

            // Devices only stream /app/device/property/{sn} for ~120s after the official app wakes them.
            // Request a full snapshot the same way the app does; the reply arrives on .../thing/property/get_reply.
            if (device.Session is AppSession quotasSession)
            {
                string quotasTopic = $"/app/{quotasSession.User.Id}/{device.SerialNumber}/thing/property/get";
                string quotasPayload = $$"""{"from":"Android","operateType":"latestQuotas","id":"{{CreateId()}}","lang":"en-us","params":{},"version":"1.0"}""";

                await mqttApi.SendMessageAsync(device, quotasTopic, quotasPayload, cancellationToken)
                    .ConfigureAwait(continueOnCapturedContext: false);
            }

            Console.WriteLine(value: $"📡 Sent polling message to {device}");
        }
    }
}
