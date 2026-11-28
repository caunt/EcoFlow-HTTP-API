using EcoFlow.Mqtt.Api.Configuration;
using EcoFlow.Mqtt.Api.Configuration.Authentication;
using EcoFlow.Mqtt.Api.Extensions;
using EcoFlow.Mqtt.Api.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using System.Text;
using System.Text.Json.Nodes;

const string jsonContentType = "application/json; charset=utf-8";

Console.OutputEncoding = Encoding.UTF8;

EcoFlowConfiguration configuration = EcoFlowConfiguration.FromEnvironment();

if (configuration.Authentications.IsEmpty)
{
    await Console.Out
        .WriteLineAsync(value: "⚠️ No authentication method configured.")
        .ConfigureAwait(continueOnCapturedContext: false);

    await Console.Out.WriteLineAsync(EcoFlowConfiguration.AuthenticationHint).ConfigureAwait(continueOnCapturedContext: false);

    // Started by a double click, the console window would close before the messages above can be read.
    if (OperatingSystem.IsWindows())
    {
        await Console.Out.WriteLineAsync(value: "Press any key to exit.").ConfigureAwait(continueOnCapturedContext: false);

        while (!Console.KeyAvailable)
            await Task.Delay(TimeSpan.FromMilliseconds(milliseconds: 100)).ConfigureAwait(continueOnCapturedContext: false);
    }

    return 1;
}

IAuthentication authentication = configuration.Authentications.FirstOrDefault(candidate => candidate is AppAuthentication)
    ?? configuration.Authentications[index: 0];

if (configuration.Authentications.Length > 1)
    Console.WriteLine(value: $"⚠️ Multiple authentication methods configured. Using the {authentication.GetType().Name}.");

WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(args);

builder.Configuration
    .Sources
    .Insert(index: 0,
        new MemoryConfigurationSource
        {
            InitialData = new Dictionary<string, string?>
            {
                [key: "Http_Ports"] = "8080"
            }
        });

ILoggingBuilder logging = builder.Logging
    .AddFilter(nameof(Microsoft), LogLevel.Warning)
    .AddFilter(nameof(System), LogLevel.Warning)
    .AddFilter(category: "Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Error);

IServiceCollection services = builder.Services
    .AddHttpClient()
    .ConfigureHttpClientDefaults(httpClientBuilder => httpClientBuilder = httpClientBuilder.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        UseCookies = false
    }))
    .AddSingleton(configuration)
    .AddSingleton(serviceProvider => new InternalHttpApi(configuration, serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient()))
    .AddSingleton(serviceProvider => new InternalMqttApi(configuration))
    .AddSingleton(serviceProvider => new InternalMqttPolling(configuration, serviceProvider.GetRequiredService<InternalMqttApi>()))
    .AddHostedService(serviceProvider => serviceProvider.GetRequiredService<InternalMqttApi>())
    .AddSingleton(serviceProvider => new InternalMqttConnector(authentication,
        serviceProvider.GetRequiredService<InternalHttpApi>(),
        serviceProvider.GetRequiredService<InternalMqttApi>(),
        serviceProvider.GetRequiredService<InternalMqttPolling>()))
    .AddHostedService(serviceProvider => serviceProvider.GetRequiredService<InternalMqttPolling>())
    .AddHostedService(serviceProvider => serviceProvider.GetRequiredService<InternalMqttConnector>());

WebApplication app = builder.Build();
InternalMqttApi mqttApi = app.Services.GetRequiredService<InternalMqttApi>();

// Failing here stops the start, so wrong credentials are reported at once instead of being retried forever.
await app.Services
    .GetRequiredService<InternalMqttConnector>()
    .ConnectAsync(CancellationToken.None)
    .ConfigureAwait(continueOnCapturedContext: false);

RouteHandlerBuilder devicesEndpoint = app.MapGet(pattern: "/",
    (HttpRequest httpRequest) =>
    {
        ReadOnlyMemory<byte> state = mqttApi.GetDevices();

        return httpRequest.Query.ContainsKey(key: "flat")
            ? Results.Text(string.Join(separator: '\n', JsonNode.Parse(state.Span).Flatten()))
            : Results.Bytes(state, jsonContentType);
    });

devicesEndpoint.Add(static endpoint => endpoint.DisplayName = "All devices");

RouteHandlerBuilder deviceEndpoint = app.MapGet(pattern: "/{serialNumber}",
    (HttpRequest httpRequest, string serialNumber) =>
    {
        bool isFlat = httpRequest.Query.ContainsKey(key: "flat");

        if (mqttApi.TryGetDevice(serialNumber, out ReadOnlyMemory<byte> state))
        {
            return isFlat
                ? Results.Text(string.Join(separator: '\n', JsonNode.Parse(state.Span).Flatten()))
                : Results.Bytes(state, jsonContentType);
        }

        string serialNumbers = string.Join(separator: ',', mqttApi.SerialNumbers);

        return Results.Text(isFlat ? serialNumbers : $"Device not found. Existing serial numbers: {serialNumbers}", statusCode: StatusCodes.Status404NotFound);
    });

deviceEndpoint.Add(static endpoint => endpoint.DisplayName = "Single device");

using CancellationTokenRegistration startedRegistration = app.Lifetime
    .ApplicationStarted
    .Register(() =>
    {
        foreach (string address in app.Urls)
            Console.WriteLine(value: $"🌐 Listening on: {address}");
    });

await app.RunAsync().ConfigureAwait(continueOnCapturedContext: false);

return 0;
