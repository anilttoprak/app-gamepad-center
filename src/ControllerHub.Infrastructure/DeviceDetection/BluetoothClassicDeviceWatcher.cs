using ControllerHub.Core.Interfaces;
using ControllerHub.Core.Models;
using ControllerHub.Core.Policies;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace ControllerHub.Infrastructure.DeviceDetection;

public class TrackedClassicBtTarget
{
    public string DeviceId { get; set; }
    public string CleanMac { get; }
    public string Name { get; set; }
    public DeviceInfo DeviceInfo { get; }
    public ConnectionDebouncePolicy DebouncePolicy { get; }
    public bool RawPresentSignal { get; set; }

    public TrackedClassicBtTarget(string deviceId, string cleanMac, string name)
    {
        DeviceId = deviceId;
        CleanMac = cleanMac;
        Name = name;
        DeviceInfo = new DeviceInfo
        {
            DeviceKey = $"BT_{cleanMac}",
            Name = name,
            State = DeviceConnectionState.Disconnected,
            ConnectionType = ControllerConnectionType.Bluetooth
        };
        DebouncePolicy = new ConnectionDebouncePolicy(TimeSpan.FromSeconds(4), TimeSpan.FromMilliseconds(500));
    }
}

public class BluetoothClassicDeviceWatcher : IDeviceWatcherService
{
    private static readonly string[] RequestedProperties = new[]
    {
        "System.Devices.Aep.IsConnected",
        "System.Devices.Icon"
    };

    // Anahtar olarak mutlaka CleanMac kullanılır; böylece Windows'un aynı kol için ürettiği
    // farklı endpoint/servis ID'leri tek bir hedef kol nesnesi altında birleştirilir.
    private readonly Dictionary<string, TrackedClassicBtTarget> _trackedDevices = new(StringComparer.OrdinalIgnoreCase);
    private readonly DeviceInfo _fallbackDefaultDevice;
    private readonly object _syncLock = new();

    private DeviceWatcher? _deviceWatcher;
    private Radio? _bluetoothRadio;
    private CancellationTokenSource? _pollingCts;
    private bool _disposed;

    public event EventHandler<DeviceConnectionChangedEventArgs>? ConnectionChanged;
#pragma warning disable CS0067
    public event EventHandler<int>? BatteryLevelUpdated;
#pragma warning restore CS0067

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

    public BluetoothClassicDeviceWatcher()
    {
        _fallbackDefaultDevice = new DeviceInfo
        {
            DeviceKey = "BT_CLASSIC_CONTROLLER",
            Name = "Wireless Controller",
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

        // 2. Eşleştirilmiş Bluetooth Classic oyun kollarını keşfet
        await DiscoverTargetDevicesAsync();

        // 3. Windows PnP DeviceWatcher başlat
        StartPnpDeviceWatcher();

        // 4. 1 saniyelik periyodik durum doğrulama ve Debounce tick döngüsü
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
            // Kullanıcı Windows üzerinden Bluetooth'u kapattığında tüm kollar derhal kopmuş sayılır
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
                await CheckDeviceStatesAsync();
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

    public async Task DiscoverTargetDevicesAsync()
    {
        try
        {
            string aqs = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            var devices = await DeviceInformation.FindAllAsync(aqs, RequestedProperties);

            foreach (var d in devices)
            {
                if (IsClassicGamepad(d))
                {
                    string cleanMac = ExtractMacAddress(d.Id);
                    bool isConnected = false;

                    // Eğer PC Bluetooth radyosu kapalıysa bağlı olamaz
                    if (_bluetoothRadio != null && _bluetoothRadio.State != RadioState.On)
                    {
                        isConnected = false;
                    }
                    else if (d.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var cVal) && cVal is bool b)
                    {
                        isConnected = b;
                    }

                    lock (_syncLock)
                    {
                        if (!_trackedDevices.TryGetValue(cleanMac, out var target))
                        {
                            target = new TrackedClassicBtTarget(d.Id, cleanMac, d.Name)
                            {
                                RawPresentSignal = isConnected
                            };
                            _trackedDevices[cleanMac] = target;
                        }
                        else
                        {
                            target.DeviceId = d.Id;
                            target.Name = d.Name;
                            target.DeviceInfo.Name = d.Name;
                            target.RawPresentSignal = isConnected;
                        }
                    }
                }
            }
        }
        catch { }
    }

    private void StartPnpDeviceWatcher()
    {
        try
        {
            string aqs = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            _deviceWatcher = DeviceInformation.CreateWatcher(aqs, RequestedProperties);

            _deviceWatcher.Added += OnDeviceWatcherAdded;
            _deviceWatcher.Updated += OnDeviceWatcherUpdated;
            _deviceWatcher.Removed += OnDeviceWatcherRemoved;

            _deviceWatcher.Start();
        }
        catch { }
    }

    private void OnDeviceWatcherAdded(DeviceWatcher sender, DeviceInformation info)
    {
        if (IsClassicGamepad(info))
        {
            string cleanMac = ExtractMacAddress(info.Id);
            bool isConnected = false;

            if (_bluetoothRadio != null && _bluetoothRadio.State != RadioState.On)
            {
                isConnected = false;
            }
            else if (info.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var cVal) && cVal is bool b)
            {
                isConnected = b;
            }

            lock (_syncLock)
            {
                if (!_trackedDevices.TryGetValue(cleanMac, out var target))
                {
                    target = new TrackedClassicBtTarget(info.Id, cleanMac, info.Name)
                    {
                        RawPresentSignal = isConnected
                    };
                    _trackedDevices[cleanMac] = target;
                }
                else
                {
                    target.DeviceId = info.Id;
                    target.RawPresentSignal = isConnected;
                }
            }

            EvaluateDebounce(DateTimeOffset.UtcNow);
        }
    }

    private void OnDeviceWatcherUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        string cleanMac = ExtractMacAddress(update.Id);
        TrackedClassicBtTarget? target;
        lock (_syncLock)
        {
            _trackedDevices.TryGetValue(cleanMac, out target);
        }

        if (target != null)
        {
            if (_bluetoothRadio != null && _bluetoothRadio.State != RadioState.On)
            {
                lock (_syncLock) target.RawPresentSignal = false;
                EvaluateDebounce(DateTimeOffset.UtcNow);
            }
            else if (update.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var cVal) && cVal is bool b)
            {
                lock (_syncLock)
                {
                    target.RawPresentSignal = b;
                }
                EvaluateDebounce(DateTimeOffset.UtcNow);
            }
        }
        else
        {
            // Yeni eşleşen bir aygıt olabilir
            _ = Task.Run(async () =>
            {
                try
                {
                    var devInfo = await DeviceInformation.CreateFromIdAsync(update.Id, RequestedProperties);
                    if (devInfo != null && IsClassicGamepad(devInfo))
                    {
                        OnDeviceWatcherAdded(sender, devInfo);
                    }
                }
                catch { }
            });
        }
    }

    private void OnDeviceWatcherRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        string cleanMac = ExtractMacAddress(update.Id);
        lock (_syncLock)
        {
            if (_trackedDevices.TryGetValue(cleanMac, out var target))
            {
                target.RawPresentSignal = false;
            }
        }
        EvaluateDebounce(DateTimeOffset.UtcNow);
    }

    private async Task RunPollingLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        int discoveryCounter = 0;

        while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token))
        {
            await CheckDeviceStatesAsync();
            EvaluateDebounce(DateTimeOffset.UtcNow);

            if (++discoveryCounter % 30 == 0)
            {
                await DiscoverTargetDevicesAsync();
            }
        }
    }

    public async Task CheckDeviceStatesAsync()
    {
        List<TrackedClassicBtTarget> targets;
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

        // Eğer Windows üzerinden Bluetooth kapatılmışsa tüm Classic BT kolları anında kapalı işaretle
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
                var dev = await DeviceInformation.CreateFromIdAsync(target.DeviceId, RequestedProperties);
                if (dev != null && dev.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var cVal) && cVal is bool b)
                {
                    isPresentNow = b;
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

    public void EvaluateDebounce(DateTimeOffset nowUtc)
    {
        List<TrackedClassicBtTarget> targets;
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

                ConnectionChanged?.Invoke(this, new DeviceConnectionChangedEventArgs(
                    newState,
                    target.DeviceInfo,
                    action,
                    nowUtc));
            }
        }
    }

    public static bool IsClassicGamepad(DeviceInformation info)
    {
        var name = info.Name ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name)) return false;

        // Ses, kulaklık, klavye ve fare aygıtlarını hariç tut
        if (name.Contains("head", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("ear", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("sound", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("flow", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("airpod", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("buds", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("keyboard", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("mouse", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (info.Properties.TryGetValue("System.Devices.Icon", out var iconObj) && iconObj is string iconStr)
        {
            if (iconStr.Contains("-2033")) return false; // Ses cihazı ikonu
            if (iconStr.Contains("-2207")) return true;  // Standart Windows Game Controller ikonu
        }

        return name.Contains("Wireless Controller", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Controller", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Gamepad", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("DualShock", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("DualSense", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Joy-Con", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("PlayStation", StringComparison.OrdinalIgnoreCase);
    }

    public static string ExtractMacAddress(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return "UNKNOWN";

        int dashIdx = deviceId.LastIndexOf('-');
        if (dashIdx >= 0 && dashIdx < deviceId.Length - 1)
        {
            string raw = deviceId.Substring(dashIdx + 1).Replace(":", "").Replace("-", "").Trim().ToUpperInvariant();
            if (raw.Length == 12 && raw.All(Uri.IsHexDigit))
            {
                return raw;
            }
        }

        int devIdx = deviceId.IndexOf("DEV_", StringComparison.OrdinalIgnoreCase);
        if (devIdx >= 0 && devIdx + 16 <= deviceId.Length)
        {
            string candidate = deviceId.Substring(devIdx + 4, 12).ToUpperInvariant();
            if (candidate.All(Uri.IsHexDigit))
            {
                return candidate;
            }
        }

        var hexChars = deviceId.Where(Uri.IsHexDigit).ToArray();
        if (hexChars.Length >= 12)
        {
            return new string(hexChars, hexChars.Length - 12, 12).ToUpperInvariant();
        }

        return "CLASSIC_BT";
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
        }
    }
}
