namespace ControllerHub.Core.Models;

public class BatteryReading
{
    public long Id { get; set; }
    public string DeviceKey { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public int? Percentage { get; set; }
    public BatterySource Source { get; set; } = BatterySource.WindowsApiGatt;
    public BatteryConfidence Confidence { get; set; } = BatteryConfidence.Exact;
    public string? Note { get; set; }
}

public class BatteryChange
{
    public long Id { get; set; }
    public string DeviceKey { get; set; } = string.Empty;
    public DateTimeOffset ChangedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public int? PreviousPercentage { get; set; }
    public int? NewPercentage { get; set; } = 100;
    public string? Note { get; set; }
    public string ChangeType { get; set; } = "Manual";
}

public class DeviceInfo
{
    public string DeviceKey { get; set; } = string.Empty;
    public string Name { get; set; } = "Xbox Wireless Controller";
    public string DeviceName
    {
        get => Name;
        set => Name = value;
    }
    public DeviceConnectionState State { get; set; } = DeviceConnectionState.Disconnected;
    public int? BatteryLevel { get; set; }
    public DateTimeOffset? BatteryLastUpdatedAt { get; set; }
    public ControllerConnectionType ConnectionType { get; set; } = ControllerConnectionType.Bluetooth;
    public int? PlayerIndex { get; set; }

    public string ConnectionTypeDescription => ConnectionType switch
    {
        ControllerConnectionType.Bluetooth => "Bluetooth",
        ControllerConnectionType.WiredUsb => "USB Kablolu",
        ControllerConnectionType.WirelessAdapter => "Kablosuz Alıcı",
        _ => "Bilinmiyor"
    };

    public bool IsConnected => State == DeviceConnectionState.Connected;
}
