using WattWatch.Core.Hid;
using WattWatch.Core.Models;

namespace WattWatch.Core.Protocols;

/// <summary>
/// Sony DualSense (PS5) battery.
///
/// The pad streams state reports continuously; the battery nibble only exists in
/// the *enhanced* report layout. Over Bluetooth, Windows leaves the pad in the
/// simple layout (report 0x01, no battery), and reading feature report 0x09 — the
/// serial number — flips it into the enhanced layout (report 0x31). That is a
/// read-only operation: nothing is written to the controller.
///
/// Layout follows SDL's hidapi_ps5 driver: battery byte at 54 for report 0x31,
/// high nibble = charging state (0 discharging / 1 charging / 2 full),
/// low nibble = level in 10% steps.
/// </summary>
public sealed class DualSenseProtocol : IBatteryProtocol
{
    public const ushort VendorId = 0x054C;
    public const ushort DualSenseProductId = 0x0CE6;
    public const ushort DualSenseEdgeProductId = 0x0DF2;

    private const byte ReportBluetoothState = 0x31;
    private const byte ReportUsbState = 0x01;
    private const byte FeatureSerialNumber = 0x09;

    public string Id => "sony-dualsense";

    public string DisplayName => "索尼 DualSense 状态报文";

    public bool RequiresWrite => false;

    public bool Matches(HidInterfaceInfo info) =>
        info.VendorId == VendorId
        && (info.ProductId == DualSenseProductId || info.ProductId == DualSenseEdgeProductId)
        && info.UsagePage == 0x0001
        && info.Usage == 0x0005
        && info.InputReportLength >= 64
        && info.FeatureReportLength > 0;

    public string FriendlyName(HidInterfaceInfo info) => info.ProductId == DualSenseEdgeProductId
        ? "索尼 DualSense Edge"
        : "索尼 DualSense (PS5)";

    public DeviceKind Kind(HidInterfaceInfo info) => DeviceKind.Gamepad;

    public ProtocolResult? Read(HidHandle handle)
    {
        // Ask for the serial number; a side effect on Bluetooth is that the pad
        // starts sending the enhanced report, which is the one with battery data.
        handle.GetFeature(FeatureSerialNumber);

        foreach (byte[] report in handle.ReadFor(TimeSpan.FromMilliseconds(900), 100))
        {
            if (TryParse(report, out ProtocolResult? result)) return result;
        }

        return null;
    }

    internal static bool TryParse(byte[] report, out ProtocolResult? result)
    {
        result = null;
        if (report.Length == 0) return false;

        int batteryIndex = report[0] switch
        {
            ReportBluetoothState => 54,
            ReportUsbState when report.Length <= 64 => 53,
            _ => -1,
        };
        if (batteryIndex < 0 || report.Length <= batteryIndex) return false;

        byte raw = report[batteryIndex];
        byte status = (byte)((raw >> 4) & 0x0F);
        byte level = (byte)(raw & 0x0F);

        // 0x0C in the low nibble means "this pad does not report a level".
        if (level == 0x0C || level > 10) return false;

        ChargingState charging;
        int percent;
        switch (status)
        {
            case 0:
                charging = ChargingState.Discharging;
                percent = Math.Min(level * 10 + 5, 100);
                break;
            case 1:
                charging = ChargingState.Charging;
                percent = Math.Min(level * 10 + 5, 100);
                break;
            case 2:
                charging = ChargingState.Full;
                percent = 100;
                break;
            default:
                return false;
        }

        result = new ProtocolResult(percent, charging, $"report=0x{report[0]:X2} battery=0x{raw:X2} status={status} level={level}");
        return true;
    }
}
