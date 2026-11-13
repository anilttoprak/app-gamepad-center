using ControllerHub.Core.Interfaces;
using ControllerHub.Core.Models;
using ControllerHub.Core.Policies;

namespace ControllerHub.Infrastructure.DeviceDetection;

public class XInputSlotState
{
    public int SlotIndex { get; }
    public ConnectionDebouncePolicy DebouncePolicy { get; }
    public DeviceInfo Device { get; }
    public bool RawPresentSignal { get; set; }

    public XInputSlotState(int slotIndex)
    {
        SlotIndex = slotIndex;
        DebouncePolicy = new ConnectionDebouncePolicy(TimeSpan.FromSeconds(4), TimeSpan.FromMilliseconds(500));
        Device = new DeviceInfo
        {
            DeviceKey = $"XINPUT_SLOT_{slotIndex}",
            Name = $"XInput Kontrolcü (Oyuncu {slotIndex + 1})",
            State = DeviceConnectionState.Disconnected,
            PlayerIndex = slotIndex + 1,
            ConnectionType = ControllerConnectionType.WiredUsb
        };
    }
}

public class XInputDeviceWatcher : IDeviceWatcherService
{
    private readonly XInputSlotState[] _slots = new XInputSlotState[4];
    private readonly object _syncLock = new();
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public event EventHandler<DeviceConnectionChangedEventArgs>? ConnectionChanged;
    public event EventHandler<int>? BatteryLevelUpdated;

    public bool IsRunning { get; private set; }

    public DeviceInfo CurrentDevice
    {
        get
        {
            lock (_syncLock)
            {
                var connected = _slots.FirstOrDefault(s => s.Device.IsConnected);
                return connected?.Device ?? _slots[0].Device;
            }
        }
    }

    public IReadOnlyList<DeviceInfo> ConnectedDevices
    {
        get
        {
            lock (_syncLock)
            {
                return _slots.Where(s => s.Device.IsConnected).Select(s => s.Device).ToList();
            }
        }
    }

    public XInputDeviceWatcher()
    {
        for (int i = 0; i < 4; i++)
        {
            _slots[i] = new XInputSlotState(i);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning) return Task.CompletedTask;
        IsRunning = true;

        _cts = new CancellationTokenSource();
        _ = RunPollingLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsRunning = false;
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    private async Task RunPollingLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token))
        {
            PollSlots(DateTimeOffset.UtcNow);
        }
    }

    public void PollSlots(DateTimeOffset nowUtc)
    {
        for (int i = 0; i < 4; i++)
        {
            var slot = _slots[i];
            int result = XInputNative.GetState(i, out var state);
            bool isPresent = result == XInputNative.ERROR_SUCCESS;

            slot.RawPresentSignal = isPresent;

            if (isPresent)
            {
                // Pil / Güç türünü sorgula
                int batResult = XInputNative.GetBatteryInformation(i, out var batInfo);
                if (batResult == XInputNative.ERROR_SUCCESS)
                {
                    if (batInfo.BatteryType == XInputNative.BATTERY_TYPE_WIRED)
                    {
                        slot.Device.ConnectionType = ControllerConnectionType.WiredUsb;
                        slot.Device.Name = $"USB Kablolu Oyun Kolu (Oyuncu {i + 1})";
                        slot.Device.BatteryLevel = null; // Sürekli kablolu güç
                    }
                    else if (batInfo.BatteryType == XInputNative.BATTERY_TYPE_ALKALINE ||
                             batInfo.BatteryType == XInputNative.BATTERY_TYPE_NIMH)
                    {
                        slot.Device.ConnectionType = ControllerConnectionType.WirelessAdapter;
                        slot.Device.Name = $"XInput Kablosuz Kol (Oyuncu {i + 1})";
                        slot.Device.BatteryLevel = batInfo.BatteryLevel switch
                        {
                            XInputNative.BATTERY_LEVEL_FULL => 100,
                            XInputNative.BATTERY_LEVEL_MEDIUM => 50,
                            XInputNative.BATTERY_LEVEL_LOW => 20,
                            XInputNative.BATTERY_LEVEL_EMPTY => 5,
                            _ => null
                        };
                    }
                    else
                    {
                        // BATTERY_TYPE_DISCONNECTED veya BATTERY_TYPE_UNKNOWN
                        // Windows Bluetooth köprüsü veya harici USB oyun kolları
                        slot.Device.ConnectionType = ControllerConnectionType.WiredUsb;
                        slot.Device.Name = $"USB Oyun Kolu (Oyuncu {i + 1})";
                        slot.Device.BatteryLevel = null;
                    }
                }
                else
                {
                    // Fallback to standard XInput USB/Wired
                    slot.Device.ConnectionType = ControllerConnectionType.WiredUsb;
                    slot.Device.Name = $"PC Oyun Kolu (USB - Oyuncu {i + 1})";
                    slot.Device.BatteryLevel = null;
                }
            }

            var previousState = slot.Device.State;
            var (newState, action) = slot.DebouncePolicy.Evaluate(isPresent, nowUtc);

            if (previousState != newState || action != DebounceAction.None)
            {
                slot.Device.State = newState;

                if (newState == DeviceConnectionState.Connected)
                {
                    slot.Device.BatteryLastUpdatedAt = nowUtc;
                    if (slot.Device.BatteryLevel.HasValue && slot.Device.ConnectionType == ControllerConnectionType.WirelessAdapter)
                    {
                        BatteryLevelUpdated?.Invoke(this, slot.Device.BatteryLevel.Value);
                    }
                }
                else if (newState == DeviceConnectionState.Disconnected)
                {
                    slot.Device.BatteryLevel = null;
                }

                ConnectionChanged?.Invoke(this, new DeviceConnectionChangedEventArgs(
                    newState,
                    slot.Device,
                    action,
                    nowUtc));
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }
}
