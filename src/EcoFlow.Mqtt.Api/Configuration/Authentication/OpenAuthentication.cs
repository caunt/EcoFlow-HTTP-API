namespace EcoFlow.Mqtt.Api.Configuration.Authentication;

internal sealed record OpenAuthentication(string AccessKey, string SecretKey) : IAuthentication;
