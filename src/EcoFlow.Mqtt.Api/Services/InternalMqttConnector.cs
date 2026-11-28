using EcoFlow.Mqtt.Api.Configuration.Authentication;
using EcoFlow.Mqtt.Api.Models;
using EcoFlow.Mqtt.Api.Session;

using Microsoft.Extensions.Hosting;

namespace EcoFlow.Mqtt.Api.Services;

// The EcoFlow broker closes every connection after a while. This signs in and subscribes again whenever that happens,
// so the device states collected so far are kept instead of being lost with the process.
internal sealed class InternalMqttConnector(IAuthentication authentication, InternalHttpApi httpApi, InternalMqttApi mqttApi, InternalMqttPolling mqttPolling) : BackgroundService
{
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromMinutes(minutes: 1);
    private static readonly TimeSpan MinimumRetryDelay = TimeSpan.FromSeconds(seconds: 5);

    private IDisposable? _pollingRegistration;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await Console.Out
            .WriteLineAsync("🔑 Authenticating with EcoFlow...".AsMemory(), cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        ISession session = await httpApi.AuthenticateAsync(authentication, cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        Device[] devices = await httpApi.GetDevicesAsync(session, cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        Console.WriteLine(value: $"📱 Device List: \n\t* {string.Join(separator: "\n\t* ", devices.Select(device => device.ToString()).Order(StringComparer.Ordinal))}");

        MqttConfiguration mqttConfiguration = await httpApi.GetMqttConfigurationAsync(session, cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        Console.WriteLine(value: $"🔌 Subscribing devices to MQTT ({mqttConfiguration.Url}:{mqttConfiguration.Port})...");

        await mqttApi.ConnectAsync(session, mqttConfiguration, devices, cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        // The devices of the previous connection are dropped first, they may be the same ones.
        _pollingRegistration?.Dispose();

        _pollingRegistration = mqttPolling.RegisterDevices(mqttConfiguration, devices);
    }

    public override void Dispose()
    {
        _pollingRegistration?.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await mqttApi.WaitForDisconnectionAsync(stoppingToken).ConfigureAwait(continueOnCapturedContext: false);

            await Console.Out
                .WriteLineAsync("⚠️ MQTT connection lost. Reconnecting...".AsMemory(), stoppingToken)
                .ConfigureAwait(continueOnCapturedContext: false);

            await ReconnectAsync(stoppingToken).ConfigureAwait(continueOnCapturedContext: false);
        }
    }

    private async Task ReconnectAsync(CancellationToken cancellationToken)
    {
        TimeSpan retryDelay = MinimumRetryDelay;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Whatever went wrong, the cloud or the network may be back a moment later.
                Console.WriteLine(value: $"⚠️ Reconnecting failed, trying again in {retryDelay.TotalSeconds:0} seconds: {exception.Message}");
            }

            await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            retryDelay = TimeSpan.FromTicks(Math.Min(retryDelay.Ticks * 2, MaximumRetryDelay.Ticks));
        }
    }
}
