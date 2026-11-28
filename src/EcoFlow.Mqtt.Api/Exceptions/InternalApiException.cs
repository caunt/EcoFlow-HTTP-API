namespace EcoFlow.Mqtt.Api.Exceptions;

internal abstract class InternalApiException : Exception
{
    protected InternalApiException()
    {
    }

    protected InternalApiException(string message) : base(message)
    {
    }

    protected InternalApiException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
