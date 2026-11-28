namespace EcoFlow.Mqtt.Api.Exceptions;

internal sealed class AuthenticationException : InternalApiException
{
    public AuthenticationException()
    {
    }

    public AuthenticationException(string message) : base(message)
    {
    }

    public AuthenticationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
