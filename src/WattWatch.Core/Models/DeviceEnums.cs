namespace WattWatch.Core.Models;

public enum ChargingState
{
    Unknown = 0,
    Discharging = 1,
    Charging = 2,
    Full = 3,
}

public enum DeviceKind
{
    Unknown = 0,
    Mouse = 1,
    Keyboard = 2,
    Gamepad = 3,
    Headset = 4,
}

public static class ChargingStateText
{
    public static string ToChinese(ChargingState state) => state switch
    {
        ChargingState.Charging => "充电中",
        ChargingState.Full => "已充满",
        ChargingState.Discharging => "放电中",
        _ => "状态未知",
    };
}
