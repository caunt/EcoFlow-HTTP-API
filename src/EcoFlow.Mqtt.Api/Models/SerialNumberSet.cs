using EcoFlow.Mqtt.Api.Configuration;
using EcoFlow.Mqtt.Api.Json;
using EcoFlow.Mqtt.Api.Session;

using Nito.AsyncEx;

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace EcoFlow.Mqtt.Api.Models;

internal sealed class SerialNumberSet(EcoFlowConfiguration configuration, HttpClient httpClient, ImmutableArray<string> templates)
{
    private const char Wildcard = '-';

    private const int SuffixLength = 4;

    private readonly Dictionary<string, Device> _cache = [];
    private readonly AsyncLock _lock = new();

    public async Task<Device> GetDeviceAsync(ISession session, string serialNumber, CancellationToken cancellationToken = default)
    {
        using IDisposable cacheLock = await _lock.LockAsync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);

        if (_cache.TryGetValue(serialNumber, out Device? device))
            return device;

        if (!TryMatch(serialNumber, out string? pattern))
            throw new InvalidOperationException(message: $"Serial number '{serialNumber}' does not match any known patterns.");

        using HttpRequestMessage request = new(HttpMethod.Get,
            new Uri(configuration.AppApiUri, relativeUri: $"/app/sku/queryBySnPrefix?sn={pattern}"));

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        string body = await response.EnsureSuccessStatusCode()
            .Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(continueOnCapturedContext: false);

        EcoFlowResponse<IReadOnlyList<ProductEntry>>? products = JsonSerializer.Deserialize(body, ApplicationJsonContext.Default.EcoFlowResponseIReadOnlyListProductEntry);

        if (products?.Data is not { Count: > 0 } entries)
            throw new InvalidOperationException(message: $"No device information found for serial number '{serialNumber}'.");

        if (entries.Count > 1)
            throw new InvalidOperationException(message: $"Multiple device information entries found for serial number '{serialNumber}'.\n{body}");

        string? title = entries[index: 0].Name;

        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException(message: $"Device information for serial number '{serialNumber}' does not contain a valid title.\n{body}");

        device = new Device(session, serialNumber, title);
        _cache[serialNumber] = device;

        return device;
    }

    public bool TryMatch(string serialNumber, [MaybeNullWhen(false)] out string pattern)
    {
        // A template masks the characters that differ between devices, and may leave out the last ones altogether.
        string[] matches = [.. templates.Where(template => IsMatch(serialNumber, template)).Take(count: 2)];

        pattern = matches is [string match] ? match.TrimEnd(Wildcard) : null;

        return pattern is not null;
    }

    private static bool IsMatch(string serialNumber, string template)
    {
        if (serialNumber.Length != template.Length && serialNumber.Length - SuffixLength != template.Length)
            return false;

        for (int characterIndex = 0; characterIndex < template.Length; characterIndex++)
        {
            if (template[characterIndex] != Wildcard && template[characterIndex] != serialNumber[characterIndex])
                return false;
        }

        return true;
    }
}
