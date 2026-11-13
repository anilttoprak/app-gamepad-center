using ControllerHub.Core.Models;
using ControllerHub.Core.Policies;

namespace ControllerHub.Core.Interfaces;

public class DeviceConnectionChangedEventArgs : EventArgs
{
    public DeviceConnectionState State { get; }
    public DeviceInfo Device { get; }
    public DebounceAction Action { get; }
    public DateTimeOffset TimestampUtc { get; }

    public DeviceConnectionChangedEventArgs(
        DeviceConnectionState state,
        DeviceInfo device,
        DebounceAction action,
        DateTimeOffset timestampUtc)
    {
        State = state;
        Device = device;
        Action = action;
        TimestampUtc = timestampUtc;
    }
}

public interface IDeviceWatcherService : IDisposable
{
    event EventHandler<DeviceConnectionChangedEventArgs>? ConnectionChanged;
    event EventHandler<int>? BatteryLevelUpdated;

    DeviceInfo CurrentDevice { get; }
    IReadOnlyList<DeviceInfo> ConnectedDevices { get; }
    bool IsRunning { get; }

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
}
