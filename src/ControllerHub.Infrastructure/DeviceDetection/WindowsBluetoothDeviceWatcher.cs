using System.Runtime.InteropServices.WindowsRuntime;
using ControllerHub.Core.Interfaces;
using ControllerHub.Core.Models;
using ControllerHub.Core.Policies;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

namespace ControllerHub.Infrastructure.DeviceDetection;

public class WindowsBluetoothDeviceWatcher : IDeviceWatcherService
{
    private readonly ConnectionDebouncePolicy _debouncePolicy;
    private readonly BatteryFreshnessPolicy _batteryPolicy;
    private readonly DeviceInfo _currentDevice;
    private readonly object _syncLock = new();

    private DeviceWatcher? _deviceWatcher;
    private CancellationTokenSource? _pollingCts;
    private ulong? _discoveredMac;
    private string? _discoveredDeviceId;
    private bool _rawPresentSignal;
    private bool _disposed;

    public event EventHandler<DeviceConnectionChangedEventArgs>? ConnectionChanged;
    public event EventHandler<int>? BatteryLevelUpdated;

    public DeviceInfo CurrentDevice => _currentDevice;
    public bool IsRunning { get; private set; }

    public WindowsBluetoothDeviceWatcher()
    {
        _debouncePolicy = new ConnectionDebouncePolicy(TimeSpan.FromSeconds(4), TimeSpan.FromMilliseconds(500));
        _batteryPolicy = new BatteryFreshnessPolicy();
        _currentDevice = new DeviceInfo
        {
            DeviceKey = "XBOX_CONTROLLER_DYNAMIC",
            Name = "Xbox Wireless Controller",
            State = DeviceConnectionState.Disconnected
        };
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning) return;
        IsRunning = true;

        // 1. İlk olarak eşleştirilmiş Bluetooth LE kontrolcüsünü bul
        await DiscoverTargetDeviceAsync();

        // 2. Windows PnP DeviceWatcher başlat
        StartPnpDeviceWatcher();

        // 3. Arka plan periyodik doğrulama ve Debounce tick döngüsü (her 1 saniyede bir)
        _pollingCts = new CancellationTokenSource();
        _ = RunPollingLoopAsync(_pollingCts.Token);
    }

    public Task StopAsync()
    {
        IsRunning = false;
        _pollingCts?.Cancel();
        _deviceWatcher?.Stop();
        return Task.CompletedTask;
    }

    private async Task DiscoverTargetDeviceAsync()
    {
        try
        {
            string aqs = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
            var devices = await DeviceInformation.FindAllAsync(aqs);

            foreach (var d in devices)
            {
                if (IsXboxControllerName(d.Name))
                {
                    lock (_syncLock)
                    {
                        _discoveredDeviceId = d.Id;
                        _currentDevice.Name = d.Name;
                    }

                    try
                    {
                        var ble = await BluetoothLEDevice.FromIdAsync(d.Id);
                        if (ble != null)
                        {
                            lock (_syncLock)
                            {
                                _discoveredMac = ble.BluetoothAddress;
                                _currentDevice.DeviceKey = $"XBOX_BLE_{ble.BluetoothAddress:X}";
                            }
                        }
                    }
                    catch { }
                    break;
                }
            }
        }
        catch { }
    }

    private void StartPnpDeviceWatcher()
    {
        try
        {
            string aqs = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
            _deviceWatcher = DeviceInformation.CreateWatcher(aqs);

            _deviceWatcher.Added += OnDeviceWatcherAddedOrUpdated;
            _deviceWatcher.Updated += OnDeviceWatcherAddedOrUpdated;
            _deviceWatcher.Removed += OnDeviceWatcherRemoved;

            _deviceWatcher.Start();
        }
        catch { }
    }

    private void OnDeviceWatcherAddedOrUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        if (_discoveredDeviceId != null && update.Id == _discoveredDeviceId)
        {
            // Cihaz durumu güncellendi
            _ = CheckDeviceStateAsync();
        }
    }

    private void OnDeviceWatcherAddedOrUpdated(DeviceWatcher sender, DeviceInformation info)
    {
        if (IsXboxControllerName(info.Name))
        {
            lock (_syncLock)
            {
                _discoveredDeviceId = info.Id;
                _currentDevice.Name = info.Name;
            }
            _ = CheckDeviceStateAsync();
        }
    }

    private void OnDeviceWatcherRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        if (_discoveredDeviceId != null && update.Id == _discoveredDeviceId)
        {
            lock (_syncLock)
            {
                _rawPresentSignal = false;
            }
            EvaluateDebounce(DateTimeOffset.UtcNow);
        }
    }

    private async Task RunPollingLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token))
        {
            await CheckDeviceStateAsync();
            EvaluateDebounce(DateTimeOffset.UtcNow);
        }
    }

    private async Task CheckDeviceStateAsync()
    {
        if (_discoveredMac == null && _discoveredDeviceId == null)
        {
            await DiscoverTargetDeviceAsync();
        }

        bool isPresentNow = false;

        if (_discoveredMac.HasValue)
        {
            try
            {
                var ble = await BluetoothLEDevice.FromBluetoothAddressAsync(_discoveredMac.Value);
                if (ble != null)
                {
                    // BluetoothConnectionStatus.Connected kontrolü
                    isPresentNow = ble.ConnectionStatus == BluetoothConnectionStatus.Connected;
                }
            }
            catch
            {
                isPresentNow = false;
            }
        }

        lock (_syncLock)
        {
            _rawPresentSignal = isPresentNow;
        }
    }

    private void EvaluateDebounce(DateTimeOffset nowUtc)
    {
        bool raw;
        lock (_syncLock)
        {
            raw = _rawPresentSignal;
        }

        var previousState = _currentDevice.State;
        var (newState, action) = _debouncePolicy.Evaluate(raw, nowUtc);

        if (previousState != newState || action != DebounceAction.None)
        {
            _currentDevice.State = newState;

            if (newState == DeviceConnectionState.Connected)
            {
                // Bağlantı kuruldu; taze pil okuması yap
                _ = RefreshBatteryAsync(nowUtc);
            }
            else if (newState == DeviceConnectionState.Disconnected)
            {
                _currentDevice.BatteryLevel = null;
            }

            ConnectionChanged?.Invoke(this, new DeviceConnectionChangedEventArgs(
                newState,
                _currentDevice,
                action,
                nowUtc));
        }
    }

    private async Task RefreshBatteryAsync(DateTimeOffset nowUtc)
    {
        if (!_discoveredMac.HasValue) return;

        try
        {
            var ble = await BluetoothLEDevice.FromBluetoothAddressAsync(_discoveredMac.Value);
            if (ble == null || ble.ConnectionStatus != BluetoothConnectionStatus.Connected) return;

            var svcResult = await ble.GetGattServicesForUuidAsync(GattServiceUuids.Battery);
            if (svcResult.Status == GattCommunicationStatus.Success && svcResult.Services.Count > 0)
            {
                var chResult = await svcResult.Services[0].GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel);
                if (chResult.Status == GattCommunicationStatus.Success && chResult.Characteristics.Count > 0)
                {
                    var readResult = await chResult.Characteristics[0].ReadValueAsync();
                    if (readResult.Status == GattCommunicationStatus.Success)
                    {
                        byte[] bytes = readResult.Value.ToArray();
                        if (bytes.Length > 0)
                        {
                            int rawPct = bytes[0];
                            var validation = _batteryPolicy.ValidateReading(
                                isDeviceConnected: true,
                                rawPercentage: rawPct,
                                nowUtc: nowUtc);

                            if (validation.IsValid && validation.Percentage.HasValue)
                            {
                                _currentDevice.BatteryLevel = validation.Percentage.Value;
                                _currentDevice.BatteryLastUpdatedAt = nowUtc;
                                BatteryLevelUpdated?.Invoke(this, validation.Percentage.Value);
                            }
                        }
                    }
                }
            }
        }
        catch { }
    }

    private static bool IsXboxControllerName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return name.Contains("Xbox", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Wireless Controller", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _pollingCts?.Cancel();
            _pollingCts?.Dispose();
            _deviceWatcher?.Stop();
        }
    }
}
