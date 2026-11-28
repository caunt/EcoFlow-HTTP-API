namespace EcoFlow.Mqtt.Api.Models;

internal sealed record MqttConfiguration(bool Tls, string Url, int Port, string Username, string Password);
