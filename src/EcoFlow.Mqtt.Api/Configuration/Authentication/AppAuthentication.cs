namespace EcoFlow.Mqtt.Api.Configuration.Authentication;

internal sealed record AppAuthentication(string Username, string Password) : IAuthentication;
