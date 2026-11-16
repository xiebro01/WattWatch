using WattWatch.Core.Hid;
using WattWatch.Core.Models;

namespace WattWatch.Core.Protocols;

/// <summary>
/// VGN V98 Pro (2.4G receiver 320F:5088). The vendor channel is the 0xFF1C/0x0092
/// collection with 64-byte reports; battery is a 7-byte 0x1A query on report id 0x04.
///
/// Verified live: write 04 00 00 1A 06 00 00 00 -> read 04 00 00 1A 06 00 00 00 4D 00 ...
/// (level at index 8, charging flag at index 9).
/// </summary>
public sealed class VgnV98ProProtocol : IBatteryProtocol
{
    public const ushort VendorId = 0x320F;
    public const ushort ProductId = 0x5088;

    private const byte ReportId = 0x04;
    private static readonly byte[] Query = { 0x00, 0x00, 0x1A, 0x06, 0x00, 0x00, 0x00 };

    public string Id => "vgn-v98pro";

    public string DisplayName => "VGN 0x1A 私有协议";

    public bool RequiresWrite => true;

    public bool Matches(HidInterfaceInfo info) =>
        info.VendorId == VendorId && info.ProductId == ProductId
        && info.UsagePage == 0xFF1C && info.Usage == 0x0092
        && info.OutputReportLength >= 17 && info.InputReportLength >= 10;

    public string FriendlyName(HidInterfaceInfo info) => "VGN V98 Pro";

    public DeviceKind Kind(HidInterfaceInfo info) => DeviceKind.Keyboard;

    public ProtocolResult? Read(HidHandle handle)
    {
        var report = new byte[Math.Max(handle.Info.OutputReportLength, Query.Length + 1)];
        report[0] = ReportId;
        Array.Copy(Query, 0, report, 1, Query.Length);

        if (!handle.Write(report, 500)) return null;

        foreach (byte[] input in handle.ReadFor(TimeSpan.FromMilliseconds(500), 100))
        {
            if (TryParse(input, out int percent, out ChargingState charging, out string detail))
                return new ProtocolResult(percent, charging, detail);
        }
        return null;
    }

    internal static bool TryParse(byte[] input, out int percent, out ChargingState charging, out string detail)
    {
        percent = 0;
        charging = ChargingState.Unknown;
        detail = string.Empty;

        if (input.Length < 10) return false;
        if (input[0] != ReportId || input[3] != 0x1A) return false;

        byte level = input[8];
        if (level > 100) return false;

        percent = level;
        charging = input[9] != 0 ? ChargingState.Charging : ChargingState.Discharging;
        detail = $"level={level} flag=0x{input[9]:X2}";
        return true;
    }
}
