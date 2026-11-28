namespace EcoFlow.Mqtt.Api.Exceptions;

internal sealed class DeviceListException : InternalApiException
{
    public DeviceListException()
    {
    }

    public DeviceListException(string message) : base(message)
    {
    }

    public DeviceListException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
