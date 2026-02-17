using EcoFlow.Mqtt.Api.Models;

namespace EcoFlow.Mqtt.Api.Session;

public record AppSession(string Token, User User) : ISession;
