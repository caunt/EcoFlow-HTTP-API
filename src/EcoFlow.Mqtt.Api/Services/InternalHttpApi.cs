using EcoFlow.Mqtt.Api.Configuration;
using EcoFlow.Mqtt.Api.Configuration.Authentication;
using EcoFlow.Mqtt.Api.Exceptions;
using EcoFlow.Mqtt.Api.Json;
using EcoFlow.Mqtt.Api.Models;
using EcoFlow.Mqtt.Api.Session;

using Nito.AsyncEx;

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace EcoFlow.Mqtt.Api.Services;

internal sealed class InternalHttpApi(EcoFlowConfiguration configuration, HttpClient httpClient)
{
    private readonly AsyncLock _serialNumberSetLock = new();

    private SerialNumberSet? _serialNumberSet;

    public async Task<ISession> AuthenticateAsync(IAuthentication authentication, CancellationToken cancellationToken = default)
    {
        switch (authentication)
        {
            case AppAuthentication appAuthentication:
                {
                    LoginPayload payload = new(OperatingSystem: "linux",
                        Scene: "IOT_APP",
                        AppVersion: "1.0.0",
                        OperatingSystemVersion: "5.15.90.1-kali-fake",
                        Convert.ToBase64String(Encoding.UTF8.GetBytes(appAuthentication.Password)),
                        new LoginBundle(BundleId: "com.ef.EcoFlow"),
                        appAuthentication.Username,
                        UserType: "ECOFLOW");

                    using HttpRequestMessage request = new(HttpMethod.Post,
                        new Uri(configuration.AppApiUri, relativeUri: "/auth/login"));

                    request.Content = JsonContent.Create(payload, ApplicationJsonContext.Default.LoginPayload);

                    string body = await ReadAsync(request, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

                    LoginData? login = Deserialize(body, ApplicationJsonContext.Default.EcoFlowResponseLoginData);

                    return login is not { Token.Length: > 0 }
                        ? throw new AuthenticationException(message: $"No token received from API: {body}")
                        : login.User is not { UserId.Length: > 0 }
                            ? throw new AuthenticationException(message: $"No user id received from API: {body}")
                            : (ISession)new AppSession(login.Token, new User(login.User.UserId));
                }
            case OpenAuthentication openAuthentication:
                return new OpenSession(openAuthentication);
            default:
                throw new NotSupportedException(message: $"Unsupported authentication method: {authentication}");
        }
    }

    public async Task<Device> GetDeviceAsync(ISession session, string serialNumber, CancellationToken cancellationToken = default)
    {
        SerialNumberSet serialNumberSet = await GetDeviceSerialNumberTemplatesAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        return await serialNumberSet.GetDeviceAsync(session, serialNumber, cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);
    }

    public async Task<SerialNumberSet> GetDeviceSerialNumberTemplatesAsync(CancellationToken cancellationToken = default)
    {
        using IDisposable serialNumberSetLock = await _serialNumberSetLock.LockAsync(cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        if (_serialNumberSet is null)
        {
            const string token = "Ecoflow20230911@"; // com.ecoflow.iot.p193ui.viewmodel.MainViewModel

            using HttpRequestMessage request = new(HttpMethod.Get,
                new Uri(configuration.AppApiUri, relativeUri: $"/app/sku/getAllRealSnList?token={token}"));

            string body = await ReadAsync(request, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

            IReadOnlyList<SerialNumberEntry> entries = Deserialize(body, ApplicationJsonContext.Default.EcoFlowResponseIReadOnlyListSerialNumberEntry)
                ?? [];

            _serialNumberSet = new SerialNumberSet(configuration,
                httpClient,
                templates: [.. entries.Select(entry => entry.SerialNumber).OfType<string>()]);
        }

        return _serialNumberSet;
    }

    public async Task<Device[]> GetDevicesAsync(ISession session, CancellationToken cancellationToken = default)
    {
        string body = await ReadAsync(session,
            appPath: "/app/user/device",
            openPath: "/iot-open/sign/device/list",
            cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        JsonElement data = Deserialize(body, ApplicationJsonContext.Default.EcoFlowResponseJsonElement);

        return await Task.WhenAll(GetSerialNumbers(data, body).Select(serialNumber => GetDeviceAsync(session, serialNumber, cancellationToken)))
            .ConfigureAwait(continueOnCapturedContext: false);
    }

    public async Task<MqttConfiguration> GetMqttConfigurationAsync(ISession session, CancellationToken cancellationToken = default)
    {
        string body = await ReadAsync(session,
            appPath: "/iot-auth/app/certification",
            openPath: "/iot-open/sign/certification",
            cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        MqttCertification? certification = Deserialize(body, ApplicationJsonContext.Default.EcoFlowResponseMqttCertification);

        if (certification is not { Url.Length: > 0, CertificateAccount.Length: > 0, CertificatePassword.Length: > 0 })
            throw new MqttConfigurationException(message: $"Invalid MQTT configuration received: {body}");

        bool useTls = certification.Protocol switch
        {
            "mqtt" => false,
            "mqtts" => true,
            _ => throw new MqttConfigurationException(message: $"Invalid MQTT configuration received: {body}")
        };

        return new MqttConfiguration(useTls,
            certification.Url,
            certification.Port,
            certification.CertificateAccount,
            certification.CertificatePassword);
    }

    private static TData? Deserialize<TData>(string body, JsonTypeInfo<EcoFlowResponse<TData>> responseType)
    {
        EcoFlowResponse<TData>? response = JsonSerializer.Deserialize(body, responseType);

        return response is null ? default : response.Data;
    }

    // The app API groups devices by category, the open API returns a plain list.
    private static List<string> GetSerialNumbers(JsonElement data, string body)
    {
        List<string> serialNumbers = [];

        switch (data.ValueKind)
        {
            case JsonValueKind.Array:
                {
                    foreach (JsonElement device in data.EnumerateArray())
                    {
                        if (device.ValueKind is not JsonValueKind.Object || !device.TryGetProperty(propertyName: "sn", out JsonElement serialNumber))
                            continue;

                        if (serialNumber.GetString() is string value)
                            serialNumbers.Add(value);
                    }

                    break;
                }
            case JsonValueKind.Object:
                {
                    foreach (JsonProperty category in data.EnumerateObject())
                    {
                        if (category.Value.ValueKind is not JsonValueKind.Object)
                            continue;

                        foreach (JsonProperty device in category.Value.EnumerateObject())
                            serialNumbers.Add(device.Name);
                    }

                    break;
                }
            default:
                throw new DeviceListException(message: $"Invalid device list received: {body}");
        }

        return serialNumbers;
    }

    private async Task<string> ReadAsync(ISession session, string appPath, string openPath, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get,
            new Uri(configuration.AppApiUri, session is OpenSession ? openPath : appPath));

        switch (session)
        {
            case AppSession appSession:
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue(scheme: "Bearer", appSession.Token);

                    break;
                }
            case OpenSession openSession:
                {
                    openSession.SignRequest(request);

                    break;
                }
            default:
                throw new NotSupportedException(message: $"Unsupported session type: {session}");
        }

        return await ReadAsync(request, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
    }

    private async Task<string> ReadAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        return await response.EnsureSuccessStatusCode()
            .Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);
    }
}
