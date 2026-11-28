namespace EcoFlow.Mqtt.Api.Exceptions;

internal sealed class MqttConfigurationException : InternalApiException
{
    public MqttConfigurationException()
    {
    }

    public MqttConfigurationException(string message) : base(message)
    {
    }

    public MqttConfigurationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
