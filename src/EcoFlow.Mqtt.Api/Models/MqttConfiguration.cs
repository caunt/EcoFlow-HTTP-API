namespace EcoFlow.Mqtt.Api.Models;

public record MqttConfiguration(bool Tls, string Url, int Port, string Username, string Password);
