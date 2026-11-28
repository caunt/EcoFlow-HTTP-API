using EcoFlow.Mqtt.Api.Session;

namespace EcoFlow.Mqtt.Api.Models;

internal sealed record Device(ISession Session, string SerialNumber, string Title)
{
    private const int SuffixLength = 4;

    private const string Brand = "EcoFlow";

    public override string ToString()
    {
        // Only the end of the serial number is shown, it is enough to tell devices of one model apart.
        string title = Title.Contains(Brand, StringComparison.OrdinalIgnoreCase) ? Title : $"{Brand} {Title}";

        return $"{title} - {SerialNumber[^SuffixLength..]}";
    }
}
