using WattWatch.Core.Hid;
using WattWatch.Core.Models;

namespace WattWatch.Core.Protocols;

/// <summary>
/// Flydigi Apex 5 (2.4G dongle 37D7:2501, 0xFFA0 vendor collection).
///
/// Frame layout follows SDL's hidapi_flydigi driver: a 0x03 command report with
/// the 5A A5 magic, GET_INFO = 0x01. The reply packs charging state in the high
/// nibble and the level in the low nibble of payload byte 11; Apex 5 reports in
/// 20% steps, Apex 6 in 10% steps.
///
/// Not yet verified against physical hardware in this project (the controller
/// was not connected while this was written).
/// </summary>
public sealed class FlydigiProtocol : IBatteryProtocol
{
    public const ushort VendorId = 0x37D7;
    public const ushort Apex5ProductId = 0x2501;
    public const ushort Apex6ProductId = 0x2502;

    private const byte CommandReportId = 0x03;
    private const byte Magic1 = 0x5A;
    private const byte Magic2 = 0xA5;
    private const byte GetInfo = 0x01;

    public string Id => "flydigi-v2";

    public string DisplayName => "飞智 V2 私有协议";

    public bool RequiresWrite => true;

    public bool Matches(HidInterfaceInfo info) =>
        info.VendorId == VendorId
        && (info.ProductId == Apex5ProductId || info.ProductId == Apex6ProductId)
        && info.UsagePage == 0xFFA0;

    public string FriendlyName(HidInterfaceInfo info) => info.ProductId == Apex6ProductId
        ? "飞智 八爪鱼 6"
        : "飞智 八爪鱼 5";

    public DeviceKind Kind(HidInterfaceInfo info) => DeviceKind.Gamepad;

    public ProtocolResult? Read(HidHandle handle)
    {
        var report = new byte[32];
        report[0] = CommandReportId;
        report[1] = Magic1;
        report[2] = Magic2;
        report[3] = GetInfo;
        report[4] = 0x02;
        report[5] = 0x00;

        if (!handle.Write(report, 500)) return null;

        foreach (byte[] input in handle.ReadFor(TimeSpan.FromMilliseconds(800), 120))
        {
            if (TryParse(input, handle.Info.ProductId, out ProtocolResult? result))
                return result;
        }
        return null;
    }

    internal static bool TryParse(byte[] packet, ushort productId, out ProtocolResult? result)
    {
        result = null;

        ReadOnlySpan<byte> data = packet;
        // Some firmwares prefix the report id, some do not.
        if (data.Length > 0 && data[0] != Magic1) data = data[1..];
        if (data.Length < 12) return false;
        if (data[0] != Magic1 || data[1] != Magic2 || data[2] != GetInfo) return false;

        byte status = (byte)((data[11] >> 4) & 0x0F);
        byte level = (byte)(data[11] & 0x0F);

        int percent;
        ChargingState charging;
        switch (status)
        {
            case 0:
                charging = ChargingState.Discharging;
                percent = level * (productId == Apex6ProductId ? 10 : 20);
                break;
            case 1:
                charging = ChargingState.Charging;
                percent = level * (productId == Apex6ProductId ? 10 : 20);
                break;
            case 2:
                charging = ChargingState.Full;
                percent = 100;
                break;
            default:
                return false;
        }

        result = new ProtocolResult(Math.Clamp(percent, 0, 100), charging,
            $"status={status} level={level} byte11=0x{data[11]:X2}");
        return true;
    }
}
