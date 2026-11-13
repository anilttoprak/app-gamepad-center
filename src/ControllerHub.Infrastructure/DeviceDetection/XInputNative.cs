using System.Runtime.InteropServices;

namespace ControllerHub.Infrastructure.DeviceDetection;

[StructLayout(LayoutKind.Sequential)]
public struct XINPUT_GAMEPAD
{
    public ushort wButtons;
    public byte bLeftTrigger;
    public byte bRightTrigger;
    public short sThumbLX;
    public short sThumbLY;
    public short sThumbRX;
    public short sThumbRY;
}

[StructLayout(LayoutKind.Sequential)]
public struct XINPUT_STATE
{
    public uint dwPacketNumber;
    public XINPUT_GAMEPAD Gamepad;
}

[StructLayout(LayoutKind.Sequential)]
public struct XINPUT_BATTERY_INFORMATION
{
    public byte BatteryType;
    public byte BatteryLevel;
}

public static class XInputNative
{
    public const int ERROR_SUCCESS = 0;
    public const int ERROR_DEVICE_NOT_CONNECTED = 1167;

    public const byte BATTERY_DEVTYPE_GAMEPAD = 0x00;

    public const byte BATTERY_TYPE_DISCONNECTED = 0x00;
    public const byte BATTERY_TYPE_WIRED = 0x01;
    public const byte BATTERY_TYPE_ALKALINE = 0x02;
    public const byte BATTERY_TYPE_NIMH = 0x03;
    public const byte BATTERY_TYPE_UNKNOWN = 0xFF;

    public const byte BATTERY_LEVEL_EMPTY = 0x00;
    public const byte BATTERY_LEVEL_LOW = 0x01;
    public const byte BATTERY_LEVEL_MEDIUM = 0x02;
    public const byte BATTERY_LEVEL_FULL = 0x03;

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState", CallingConvention = CallingConvention.StdCall)]
    private static extern int XInputGetState14(int dwUserIndex, out XINPUT_STATE pState);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetBatteryInformation", CallingConvention = CallingConvention.StdCall)]
    private static extern int XInputGetBatteryInformation14(int dwUserIndex, byte devType, out XINPUT_BATTERY_INFORMATION pBatteryInformation);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState", CallingConvention = CallingConvention.StdCall)]
    private static extern int XInputGetState91(int dwUserIndex, out XINPUT_STATE pState);

    private static bool _use14 = true;

    public static int GetState(int dwUserIndex, out XINPUT_STATE state)
    {
        state = default;
        try
        {
            if (_use14)
            {
                return XInputGetState14(dwUserIndex, out state);
            }
        }
        catch (DllNotFoundException)
        {
            _use14 = false;
        }

        try
        {
            return XInputGetState91(dwUserIndex, out state);
        }
        catch
        {
            return ERROR_DEVICE_NOT_CONNECTED;
        }
    }

    public static int GetBatteryInformation(int dwUserIndex, out XINPUT_BATTERY_INFORMATION batteryInfo)
    {
        batteryInfo = default;
        try
        {
            if (_use14)
            {
                return XInputGetBatteryInformation14(dwUserIndex, BATTERY_DEVTYPE_GAMEPAD, out batteryInfo);
            }
        }
        catch
        {
            _use14 = false;
        }

        return ERROR_DEVICE_NOT_CONNECTED;
    }
}
