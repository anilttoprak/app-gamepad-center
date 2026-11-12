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
    public DeviceConnectionState State { get; set; } = DeviceConnectionState.Disconnected;
    public int? BatteryLevel { get; set; }
    public DateTimeOffset? BatteryLastUpdatedAt { get; set; }
    public bool IsConnected => State == DeviceConnectionState.Connected;
}
