namespace ControllerHub.Core.Models;

public enum DeviceConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Disconnecting,
    Unknown
}

public enum SessionEndReason
{
    Disconnected,
    AppExit,
    Recovery,
    Unknown
}

public enum BatterySource
{
    WindowsApiGatt,
    Manual,
    XInput
}

public enum BatteryConfidence
{
    Exact,
    Coarse,
    Unknown
}

public enum ControllerConnectionType
{
    Bluetooth,
    WiredUsb,
    WirelessAdapter,
    Unknown
}
