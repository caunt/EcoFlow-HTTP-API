namespace EcoFlow.Mqtt.Api.Exceptions;

public class AuthenticationException(string message) : InternalApiException(message);
