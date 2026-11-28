using EcoFlow.Mqtt.Api.Configuration.Authentication;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EcoFlow.Mqtt.Api.Session;

internal sealed record OpenSession(OpenAuthentication Authentication) : ISession
{
    private const int NonceLimit = 1_000_000;

    public void SignRequest(HttpRequestMessage httpRequestMessage)
    {
        string timestampMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

        string nonce = RandomNumberGenerator.GetInt32(fromInclusive: 0, NonceLimit)
            .ToString(format: "D6", CultureInfo.InvariantCulture);

        string signatureSource = $"accessKey={Authentication.AccessKey}&nonce={nonce}&timestamp={timestampMilliseconds}";

        byte[] signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(Authentication.SecretKey),
            Encoding.UTF8.GetBytes(signatureSource));

        AddHeader(httpRequestMessage, name: "accessKey", Authentication.AccessKey);
        AddHeader(httpRequestMessage, name: "timestamp", timestampMilliseconds);
        AddHeader(httpRequestMessage, name: "nonce", nonce);
        AddHeader(httpRequestMessage, name: "sign", Convert.ToHexStringLower(signature));
    }

    private static void AddHeader(HttpRequestMessage httpRequestMessage, string name, string value)
    {
        if (!httpRequestMessage.Headers.TryAddWithoutValidation(name, value))
            throw new InvalidOperationException(message: $"Failed to add {name} header.");
    }
}
