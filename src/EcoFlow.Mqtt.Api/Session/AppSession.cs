using EcoFlow.Mqtt.Api.Models;

namespace EcoFlow.Mqtt.Api.Session;

internal sealed record AppSession(string Token, User User) : ISession;
