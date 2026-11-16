using WattWatch.Core.Hid;
using WattWatch.Core.Models;

namespace WattWatch.Core.Protocols;

/// <summary>
/// Razer's classic 90-byte vendor feature-report protocol (the one OpenRazer
/// documents): report id 0x00, command class 0x07, battery 0x80, charging 0x84.
///
/// Verified on a Viper V3 Pro (1532:00C1): raw 0x2E was reported as ~18% by
/// Synapse, so this generation uses the 0..255 scale, not a direct percentage.
/// </summary>
public sealed class RazerProtocol : IBatteryProtocol
{
    public const ushort VendorId = 0x1532;

    private const byte ReportId = 0x00;
    private const int PayloadLength = 90;
    private const int BufferLength = PayloadLength + 1;
    private const byte CommandClassBattery = 0x07;
    private const byte CommandGetBattery = 0x80;
    private const byte CommandGetCharging = 0x84;
    private const byte StatusSuccess = 0x02;

    private enum Scale
    {
        Scaled255,
        Direct100,
        Auto,
    }

    private static readonly Dictionary<ushort, Scale> Scales = new()
    {
        // Viper V3 Pro — verified against Synapse on 2026-xx-xx (46 -> 18%).
        [0x00C1] = Scale.Scaled255,
        // Viper V4 Pro body / dongle — same generation per VigilWatt's reports.
        [0x00E5] = Scale.Scaled255,
        [0x00E6] = Scale.Scaled255,
    };

    private static readonly byte[] TransactionCandidates = { 0x1F, 0xFF };

    public string Id => "razer-90b";

    public string DisplayName => "雷蛇 90 字节私有协议";

    public bool RequiresWrite => true;

    public bool Matches(HidInterfaceInfo info) =>
        info.VendorId == VendorId && info.FeatureReportLength == BufferLength;

    public string FriendlyName(HidInterfaceInfo info) =>
        string.IsNullOrWhiteSpace(info.Product) ? "雷蛇无线设备" : info.Product;

    public DeviceKind Kind(HidInterfaceInfo info)
    {
        string product = info.Product.ToLowerInvariant();
        if (product.Contains("headset") || product.Contains("blackshark") || product.Contains("kraken")) return DeviceKind.Headset;
        if (product.Contains("keyboard") || product.Contains("huntsman") || product.Contains("blackwidow")) return DeviceKind.Keyboard;
        return DeviceKind.Mouse;
    }

    public ProtocolResult? Read(HidHandle handle)
    {
        foreach (byte transaction in TransactionCandidates)
        {
            if (!handle.SetFeature(ReportId, BuildRequest(CommandGetBattery, transaction))) continue;

            Thread.Sleep(60);
            byte[]? response = handle.GetFeature(ReportId);
            if (!TryParse(response, CommandGetBattery, out int raw)) continue;

            ChargingState charging = ChargingState.Unknown;
            if (handle.SetFeature(ReportId, BuildRequest(CommandGetCharging, transaction)))
            {
                Thread.Sleep(40);
                byte[]? chargeResponse = handle.GetFeature(ReportId);
                if (TryParse(chargeResponse, CommandGetCharging, out int state))
                    charging = state switch
                    {
                        0 => ChargingState.Discharging,
                        1 => ChargingState.Charging,
                        _ => ChargingState.Unknown,
                    };
            }

            int percent = ToPercent(handle.Info.ProductId, raw);
            // "100% and still charging" is what users call full.
            if (percent >= 100 && charging == ChargingState.Charging) charging = ChargingState.Full;

            return new ProtocolResult(percent, charging, $"txn=0x{transaction:X2} raw={raw}");
        }

        return null;
    }

    internal static byte[] BuildRequest(byte commandId, byte transactionId)
    {
        var payload = new byte[PayloadLength];
        payload[0] = 0x00;              // new command
        payload[1] = transactionId;
        payload[2] = 0x00;
        payload[3] = 0x00;
        payload[4] = 0x00;              // protocol type
        payload[5] = 0x02;              // data size
        payload[6] = CommandClassBattery;
        payload[7] = commandId;
        payload[88] = Checksum(payload.AsSpan(2, 86));
        payload[89] = 0x00;
        return payload;
    }

    /// <summary>Named "crc" in OpenRazer, but it is a plain XOR over bytes 2..87.</summary>
    internal static byte Checksum(ReadOnlySpan<byte> data)
    {
        byte value = 0;
        foreach (byte b in data) value ^= b;
        return value;
    }

    /// <summary>buffer[0] is the report id, buffer[1] the status, buffer[10] the argument.</summary>
    internal static bool TryParse(byte[]? buffer, byte expectedCommand, out int value)
    {
        value = 0;
        if (buffer == null || buffer.Length < BufferLength) return false;
        if (buffer[1] != StatusSuccess) return false;
        if (buffer[7] != CommandClassBattery || buffer[8] != expectedCommand) return false;
        value = buffer[10];
        return true;
    }

    internal static int ToPercent(ushort productId, int raw)
    {
        Scale scale = Scales.TryGetValue(productId, out Scale known) ? known : Scale.Auto;
        int percent = scale switch
        {
            Scale.Scaled255 => raw * 100 / 255,
            Scale.Direct100 => raw,
            _ => raw <= 100 ? raw : raw * 100 / 255,
        };
        return Math.Clamp(percent, 0, 100);
    }
}
