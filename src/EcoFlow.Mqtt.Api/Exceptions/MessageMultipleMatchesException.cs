namespace EcoFlow.Mqtt.Api.Exceptions;

internal sealed class MessageMultipleMatchesException : Exception
{
    public MessageMultipleMatchesException()
    {
    }

    public MessageMultipleMatchesException(string message) : base(message)
    {
    }

    public MessageMultipleMatchesException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
