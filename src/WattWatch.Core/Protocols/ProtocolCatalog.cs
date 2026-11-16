using WattWatch.Core.Hid;

namespace WattWatch.Core.Protocols;

/// <summary>All known protocols, ordered by specificity. First match wins.</summary>
public static class ProtocolCatalog
{
    public static IReadOnlyList<IBatteryProtocol> All { get; } = new IBatteryProtocol[]
    {
        new VgnV98ProProtocol(),
        new FlydigiProtocol(),
        new RazerProtocol(),
    };

    public static IBatteryProtocol? Match(HidInterfaceInfo info)
    {
        foreach (IBatteryProtocol protocol in All)
            if (protocol.Matches(info)) return protocol;
        return null;
    }
}
