using System.Runtime.InteropServices.WindowsRuntime;
using ControllerHub.Core.Interfaces;
using ControllerHub.Core.Models;
using ControllerHub.Core.Policies;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace ControllerHub.Infrastructure.DeviceDetection;

public class WindowsBluetoothDeviceWatcher : IDeviceWatcherService
{
    private class TrackedBleTarget
    {
        public ulong MacAddress { get; }
        public string DeviceId { get; set; }
        public string Name { get; set; }
        public DeviceInfo DeviceInfo { get; }
        public ConnectionDebouncePolicy DebouncePolicy { get; }
        public bool RawPresentSignal { get; set; }
        public GattCharacteristic? BatteryCharacteristic { get; set; }

        public TrackedBleTarget(ulong mac, string deviceId, string name)
        {
            MacAddress = mac;
            DeviceId = deviceId;
            Name = name;
            DeviceInfo = new DeviceInfo
            {
                DeviceKey = $"XBOX_BLE_{mac:X}",
                Name = name,
                State = DeviceConnectionState.Disconnected,
                ConnectionType = ControllerConnectionType.Bluetooth
            };
            DebouncePolicy = new ConnectionDebouncePolicy(TimeSpan.FromSeconds(4), TimeSpan.FromMilliseconds(500));
        }
    }

    private readonly BatteryFreshnessPolicy _batteryPolicy;
    private readonly Dictionary<ulong, TrackedBleTarget> _trackedDevices = new();
    private readonly DeviceInfo _fallbackDefaultDevice;
    private readonly object _syncLock = new();

    private DeviceWatcher? _deviceWatcher;
    private Radio? _bluetoothRadio;
    private CancellationTokenSource? _pollingCts;
    private bool _disposed;

    public event EventHandler<DeviceConnectionChangedEventArgs>? ConnectionChanged;
    public event EventHandler<int>? BatteryLevelUpdated;

    public DeviceInfo CurrentDevice
    {
        get
        {
            lock (_syncLock)
            {
                var connected = _trackedDevices.Values.FirstOrDefault(t => t.DeviceInfo.IsConnected);
                if (connected != null) return connected.DeviceInfo;

                var first = _trackedDevices.Values.FirstOrDefault();
                if (first != null) return first.DeviceInfo;

                return _fallbackDefaultDevice;
            }
        }
    }

    public IReadOnlyList<DeviceInfo> ConnectedDevices
    {
        get
        {
            lock (_syncLock)
            {
                return _trackedDevices.Values
                    .Where(t => t.DeviceInfo.IsConnected)
                    .Select(t => t.DeviceInfo)
                    .ToList();
            }
        }
    }

    public bool IsRunning { get; private set; }

    public WindowsBluetoothDeviceWatcher()
    {
        _batteryPolicy = new BatteryFreshnessPolicy();
        _fallbackDefaultDevice = new DeviceInfo
        {
            DeviceKey = "XBOX_CONTROLLER_DYNAMIC",
            Name = "Xbox Wireless Controller",
            State = DeviceConnectionState.Disconnected,
            ConnectionType = ControllerConnectionType.Bluetooth
        };
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning) return;
        IsRunning = true;

        // 1. Bilgisayarın Bluetooth radyo donanım durumunu bağla
        await InitializeRadioAsync();

        // 2. Eşleştirilmiş tüm Bluetooth LE kontrolcülerini keşfet
        await DiscoverTargetDevicesAsync();

        // 3. Windows PnP DeviceWatcher başlat (Sadece hedef cihazların tak/çıkar olaylarını izler)
        StartPnpDeviceWatcher();

        // 4. Düzenli 1 saniyelik periyodik durum doğrulama ve Debounce tick döngüsü
        _pollingCts = new CancellationTokenSource();
        _ = RunPollingLoopAsync(_pollingCts.Token);
    }

    public Task StopAsync()
    {
        IsRunning = false;
        _pollingCts?.Cancel();
        StopDeviceWatcherSafely();
        return Task.CompletedTask;
    }

    private async Task InitializeRadioAsync()
    {
        try
        {
            var adapter = await BluetoothAdapter.GetDefaultAsync();
            if (adapter != null)
            {
                _bluetoothRadio = await adapter.GetRadioAsync();
                if (_bluetoothRadio != null)
                {
                    _bluetoothRadio.StateChanged += OnRadioStateChanged;
                }
            }
        }
        catch { }
    }

    private void OnRadioStateChanged(Radio sender, object args)
    {
        if (sender.State != RadioState.On)
        {
            // Kullanıcı Windows üzerinden Bluetooth'u kapattığında tüm BLE kollar derhal kopmuş sayılır
            lock (_syncLock)
            {
                foreach (var target in _trackedDevices.Values)
                {
                    target.RawPresentSignal = false;
                    if (target.DeviceInfo.IsConnected)
                    {
                        target.DeviceInfo.State = DeviceConnectionState.Disconnected;
                        ConnectionChanged?.Invoke(this, new DeviceConnectionChangedEventArgs(
                            DeviceConnectionState.Disconnected,
                            target.DeviceInfo,
                            DebounceAction.SessionEnded,
                            DateTimeOffset.UtcNow));
                    }
                }
            }
        }
        else
        {
            _ = Task.Run(async () =>
            {
                await DiscoverTargetDevicesAsync();
                await CheckDeviceStateAsync();
                EvaluateDebounce(DateTimeOffset.UtcNow);
            });
        }
    }

    private void StopDeviceWatcherSafely()
    {
        if (_deviceWatcher != null)
        {
            try
            {
                if (_deviceWatcher.Status == DeviceWatcherStatus.Started ||
                    _deviceWatcher.Status == DeviceWatcherStatus.EnumerationCompleted)
                {
                    _deviceWatcher.Stop();
                }
            }
            catch { }
        }
    }

    private async Task DiscoverTargetDevicesAsync()
    {
        try
        {
            string aqs = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
            var devices = await DeviceInformation.FindAllAsync(aqs);

            foreach (var d in devices)
            {
                if (IsXboxControllerName(d.Name))
                {
                    try
                    {
                        var ble = await BluetoothLEDevice.FromIdAsync(d.Id);
                        if (ble != null)
                        {
                            ulong mac = ble.BluetoothAddress;
                            lock (_syncLock)
                            {
                                if (!_trackedDevices.TryGetValue(mac, out var target))
                                {
                                    target = new TrackedBleTarget(mac, d.Id, d.Name);
                                    _trackedDevices[mac] = target;
                                }
                                else
                                {
                                    target.DeviceId = d.Id;
                                    target.Name = d.Name;
                                    target.DeviceInfo.Name = d.Name;
                                }
                            }
                            ble.Dispose();
                        }
                    }
                    catch { }
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

            _deviceWatcher.Added += OnDeviceWatcherAdded;
            _deviceWatcher.Updated += OnDeviceWatcherUpdated;
            _deviceWatcher.Removed += OnDeviceWatcherRemoved;

            _deviceWatcher.Start();
        }
        catch { }
    }

    private void OnDeviceWatcherAdded(DeviceWatcher sender, DeviceInformation info)
    {
        if (IsXboxControllerName(info.Name))
        {
            _ = Task.Run(async () =>
            {
                await DiscoverTargetDevicesAsync();
                await CheckDeviceStateAsync();
                EvaluateDebounce(DateTimeOffset.UtcNow);
            });
        }
    }

    private void OnDeviceWatcherUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        // Yalnızca kayıtlı kollarımızdan birine ait güncelleme geldiyse kontrol et (diğer BLE paketlerini yoksay)
        bool isOurDevice;
        lock (_syncLock)
        {
            isOurDevice = _trackedDevices.Values.Any(t => t.DeviceId == update.Id);
        }

        if (isOurDevice)
        {
            _ = Task.Run(async () =>
            {
                await CheckDeviceStateAsync();
                EvaluateDebounce(DateTimeOffset.UtcNow);
            });
        }
    }

    private void OnDeviceWatcherRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        lock (_syncLock)
        {
            var target = _trackedDevices.Values.FirstOrDefault(t => t.DeviceId == update.Id);
            if (target != null)
            {
                target.RawPresentSignal = false;
            }
        }
        EvaluateDebounce(DateTimeOffset.UtcNow);
    }

    private async Task RunPollingLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        int batteryCheckCounter = 0;
        while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token))
        {
            await CheckDeviceStateAsync();
            EvaluateDebounce(DateTimeOffset.UtcNow);

            // Bağlıyken her 60 saniyede bir taze donanımsal pil okuması yap
            if (++batteryCheckCounter % 60 == 0)
            {
                List<TrackedBleTarget> connectedTargets;
                lock (_syncLock)
                {
                    connectedTargets = _trackedDevices.Values.Where(t => t.DeviceInfo.IsConnected).ToList();
                }

                foreach (var target in connectedTargets)
                {
                    _ = RefreshBatteryForTargetAsync(target, DateTimeOffset.UtcNow);
                }
            }
        }
    }

    private async Task CheckDeviceStateAsync()
    {
        List<TrackedBleTarget> targets;
        lock (_syncLock)
        {
            targets = _trackedDevices.Values.ToList();
        }

        if (targets.Count == 0)
        {
            await DiscoverTargetDevicesAsync();
            lock (_syncLock)
            {
                targets = _trackedDevices.Values.ToList();
            }
        }

        // Eğer Windows üzerinden Bluetooth kapatılmışsa tüm BLE kolları anında kapalı işaretle
        if (_bluetoothRadio != null && _bluetoothRadio.State != RadioState.On)
        {
            foreach (var target in targets)
            {
                lock (_syncLock)
                {
                    target.RawPresentSignal = false;
                }
            }
            return;
        }

        foreach (var target in targets)
        {
            bool isPresentNow = false;
            try
            {
                var ble = await BluetoothLEDevice.FromBluetoothAddressAsync(target.MacAddress);
                if (ble != null)
                {
                    isPresentNow = ble.ConnectionStatus == BluetoothConnectionStatus.Connected;
                    ble.Dispose();
                }
            }
            catch
            {
                isPresentNow = false;
            }

            lock (_syncLock)
            {
                target.RawPresentSignal = isPresentNow;
            }
        }
    }

    private void EvaluateDebounce(DateTimeOffset nowUtc)
    {
        List<TrackedBleTarget> targets;
        lock (_syncLock)
        {
            targets = _trackedDevices.Values.ToList();
        }

        foreach (var target in targets)
        {
            bool raw;
            lock (_syncLock)
            {
                raw = target.RawPresentSignal;
            }

            var previousState = target.DeviceInfo.State;
            var (newState, action) = target.DebouncePolicy.Evaluate(raw, nowUtc);

            if (previousState != newState || action != DebounceAction.None)
            {
                target.DeviceInfo.State = newState;

                if (newState == DeviceConnectionState.Connected)
                {
                    // Bağlantı kurulduğunda donanımın ve BLE GATT servislerinin tam oturması için
                    // 2.5 saniyelik stabilizasyon süresi tanı (Windows önbelleğindeki sahte/varsayılan %50 okumalarını önler)
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(2500);
                        await RefreshBatteryForTargetAsync(target, DateTimeOffset.UtcNow);
                    });
                }
                else if (newState == DeviceConnectionState.Disconnected)
                {
                    target.DeviceInfo.BatteryLevel = null;
                    if (target.BatteryCharacteristic != null)
                    {
                        try { target.BatteryCharacteristic.ValueChanged -= OnBatteryCharacteristicValueChanged; } catch { }
                        target.BatteryCharacteristic = null;
                    }
                }

                ConnectionChanged?.Invoke(this, new DeviceConnectionChangedEventArgs(
                    newState,
                    target.DeviceInfo,
                    action,
                    nowUtc));
            }
        }
    }

    private async Task RefreshBatteryForTargetAsync(TrackedBleTarget target, DateTimeOffset nowUtc)
    {
        try
        {
            var ble = await BluetoothLEDevice.FromBluetoothAddressAsync(target.MacAddress);
            if (ble == null || ble.ConnectionStatus != BluetoothConnectionStatus.Connected)
            {
                ble?.Dispose();
                return;
            }

            var svcResult = await ble.GetGattServicesForUuidAsync(GattServiceUuids.Battery);
            if (svcResult.Status == GattCommunicationStatus.Success && svcResult.Services.Count > 0)
            {
                var chResult = await svcResult.Services[0].GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel);
                if (chResult.Status == GattCommunicationStatus.Success && chResult.Characteristics.Count > 0)
                {
                    var ch = chResult.Characteristics[0];

                    // 1. Doğrudan donanımdan taze (Uncached) okumayı dene
                    var readResult = await ch.ReadValueAsync(BluetoothCacheMode.Uncached);
                    if (readResult.Status != GattCommunicationStatus.Success)
                    {
                        // İlk bağlantı anında el sıkışması sürüyorsa 1 saniye sonra bir kez daha taze okumayı dene
                        await Task.Delay(1000);
                        readResult = await ch.ReadValueAsync(BluetoothCacheMode.Uncached);
                    }

                    if (readResult.Status == GattCommunicationStatus.Success)
                    {
                        byte[] bytes = readResult.Value.ToArray();
                        if (bytes.Length > 0)
                        {
                            ProcessRawBatteryReading(target, bytes[0], nowUtc);
                        }
                    }
                    else
                    {
                        // Yalnızca Uncached yanıt vermezse önbelleğe başvur; ancak Windows'un varsayılan sahte 50 (0x32) değerini ele
                        var cachedResult = await ch.ReadValueAsync(BluetoothCacheMode.Cached);
                        if (cachedResult.Status == GattCommunicationStatus.Success)
                        {
                            byte[] bytes = cachedResult.Value.ToArray();
                            if (bytes.Length > 0 && bytes[0] != 50)
                            {
                                ProcessRawBatteryReading(target, bytes[0], nowUtc);
                            }
                        }
                    }

                    // 2. Kontrolcünün canlı pil bildirimlerine (GATT Notify) abone ol
                    if (target.BatteryCharacteristic == null && ch.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                    {
                        try
                        {
                            var cccdStatus = await ch.WriteClientCharacteristicConfigurationDescriptorAsync(
                                GattClientCharacteristicConfigurationDescriptorValue.Notify);
                            if (cccdStatus == GattCommunicationStatus.Success)
                            {
                                target.BatteryCharacteristic = ch;
                                target.BatteryCharacteristic.ValueChanged += (s, a) =>
                                {
                                    try
                                    {
                                        byte[] notifyBytes = a.CharacteristicValue.ToArray();
                                        if (notifyBytes.Length > 0)
                                        {
                                            ProcessRawBatteryReading(target, notifyBytes[0], DateTimeOffset.UtcNow);
                                        }
                                    }
                                    catch { }
                                };
                            }
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }
    }

    private void OnBatteryCharacteristicValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        // Handled via target-specific lambda above
    }

    private void ProcessRawBatteryReading(TrackedBleTarget target, int rawPct, DateTimeOffset nowUtc)
    {
        var validation = _batteryPolicy.ValidateReading(
            isDeviceConnected: true,
            rawPercentage: rawPct,
            nowUtc: nowUtc);

        if (validation.IsValid && validation.Percentage.HasValue)
        {
            lock (_syncLock)
            {
                target.DeviceInfo.BatteryLevel = validation.Percentage.Value;
                target.DeviceInfo.BatteryLastUpdatedAt = nowUtc;
            }
            BatteryLevelUpdated?.Invoke(this, validation.Percentage.Value);
        }
    }

    public static bool IsSupportedControllerName(string? name) => IsXboxControllerName(name);

    public static bool IsXboxControllerName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return name.Contains("Xbox", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Wireless Controller", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("DualSense", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("DualShock", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Pro Controller", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("8BitDo", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Gamepad", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Controller", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            if (_bluetoothRadio != null)
            {
                try { _bluetoothRadio.StateChanged -= OnRadioStateChanged; } catch { }
                _bluetoothRadio = null;
            }
            _pollingCts?.Cancel();
            _pollingCts?.Dispose();
            StopDeviceWatcherSafely();

            lock (_syncLock)
            {
                foreach (var target in _trackedDevices.Values)
                {
                    if (target.BatteryCharacteristic != null)
                    {
                        try { target.BatteryCharacteristic.ValueChanged -= OnBatteryCharacteristicValueChanged; } catch { }
                        target.BatteryCharacteristic = null;
                    }
                }
                _trackedDevices.Clear();
            }
        }
    }
}
