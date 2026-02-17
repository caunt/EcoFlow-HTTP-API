namespace EcoFlow.Mqtt.Api.Configuration.Authentication;

public record AppAuthentication(string Username, string Password) : IAuthentication;
