using EcoFlow.Mqtt.Api.Configuration.Authentication;

using System.Collections.Immutable;
using System.Globalization;

namespace EcoFlow.Mqtt.Api.Configuration;

internal sealed record EcoFlowConfiguration(Uri AppApiUri, Uri OpenApiUri, ImmutableArray<IAuthentication> Authentications, bool VerboseLogging, TimeSpan PollingInterval)
{
    public const string AuthenticationHint = "Set [ECOFLOW_USERNAME and ECOFLOW_PASSWORD] or [ECOFLOW_ACCESS_KEY and ECOFLOW_SECRET_KEY] environment variables.";

    private const int DefaultPollingIntervalSeconds = 15;

    public static EcoFlowConfiguration FromEnvironment()
    {
        bool isVerbose = bool.TryParse(Read(name: "VERBOSE_LOGGING"), out bool verboseLogging) && verboseLogging;

        bool hasPollingInterval = int.TryParse(Read(name: "POLLING_INTERVAL_SECONDS"),
            CultureInfo.InvariantCulture,
            out int pollingIntervalSeconds);

        return new EcoFlowConfiguration(new Uri(Read(name: "APP_API_URI") ?? "https://api.ecoflow.com"),
            new Uri(Read(name: "OPEN_API_URI") ?? "https://api-e.ecoflow.com"),
            ReadAuthentications(),
            isVerbose,
            TimeSpan.FromSeconds(hasPollingInterval ? pollingIntervalSeconds : DefaultPollingIntervalSeconds));
    }

    private static string? Read(string name)
    {
        return Environment.GetEnvironmentVariable(variable: $"ECOFLOW_{name}");
    }

    private static ImmutableArray<IAuthentication> ReadAuthentications()
    {
        List<IAuthentication> authentications = [];

        if (Read(name: "ACCESS_KEY") is string accessKey && Read(name: "SECRET_KEY") is string secretKey)
            authentications.Add(new OpenAuthentication(accessKey, secretKey));

        if (Read(name: "USERNAME") is string username && Read(name: "PASSWORD") is string password)
            authentications.Add(new AppAuthentication(username, password));

        return [.. authentications];
    }
}
