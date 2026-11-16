namespace WattWatch.Core.Hid;

/// <summary>
/// One HID top-level collection as reported by SetupAPI. Everything here is
/// read-only metadata: creating one of these never sends a packet to the device.
/// </summary>
public sealed record HidInterfaceInfo(
    string DevicePath,
    ushort VendorId,
    ushort ProductId,
    ushort VersionNumber,
    string Product,
    string Manufacturer,
    string SerialNumber,
    ushort UsagePage,
    ushort Usage,
    ushort InputReportLength,
    ushort OutputReportLength,
    ushort FeatureReportLength)
{
    public string VidPid => $"{VendorId:X4}:{ProductId:X4}";

    /// <summary>Bluetooth HID paths look like <c>hid#{service-guid}_vid&amp;...</c>,
    /// USB ones like <c>hid#vid_...</c>.</summary>
    public bool IsBluetooth => DevicePath.Contains("hid#{", StringComparison.OrdinalIgnoreCase);

    public string TransportLabel => IsBluetooth ? "蓝牙" : "2.4G";

    /// <summary>Stable-ish identity for history: the physical model, not the port.</summary>
    public string StableKey =>
        $"{VendorId:X4}:{ProductId:X4}:{(string.IsNullOrWhiteSpace(SerialNumber) ? "nosn" : SerialNumber)}";

    public override string ToString() =>
        $"{VidPid} up=0x{UsagePage:X4}/0x{Usage:X4} in={InputReportLength} out={OutputReportLength} feat={FeatureReportLength} \"{Product}\"";
}
