using System.Text;

namespace EcoFlow.Mqtt.Api.Configuration.Authentication;

public record OpenAuthentication(string AccessKey, string SecretKey) : IAuthentication
{
    public byte[] AccessKeyBytes => Encoding.UTF8.GetBytes(AccessKey);
    public byte[] SecretKeyBytes => Encoding.UTF8.GetBytes(SecretKey);
}
