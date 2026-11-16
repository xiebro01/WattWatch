using WattWatch.Core.Hid;
using WattWatch.Core.Models;
using WattWatch.Core.Protocols;

namespace WattWatch.Core.Sources;

/// <summary>A 2.4G device reached through a HID top-level collection.</summary>
public sealed class HidBatterySource : IBatterySource
{
    public HidBatterySource(HidInterfaceInfo info, IBatteryProtocol protocol)
    {
        Info = info;
        Protocol = protocol;
    }

    public HidInterfaceInfo Info { get; }

    public IBatteryProtocol Protocol { get; }

    public string Id => Protocol.Id;

    public string Key => Protocol.Id + ":" + Info.StableKey;

    public string Name => Protocol.FriendlyName(Info);

    public DeviceKind Kind => Protocol.Kind(Info);

    public string Transport => Info.TransportLabel;

    public string ProtocolLabel => Protocol.DisplayName;

    public bool RequiresWrite => Protocol.RequiresWrite;

    public TimeSpan? PreferredInterval => null;

    public ProtocolResult? Read()
    {
        using HidHandle handle = HidHandle.Open(Info);
        return Protocol.Read(handle);
    }
}
