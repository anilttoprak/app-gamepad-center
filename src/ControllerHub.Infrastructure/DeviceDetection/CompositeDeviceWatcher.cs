using ControllerHub.Core.Interfaces;
using ControllerHub.Core.Models;
using ControllerHub.Core.Policies;

namespace ControllerHub.Infrastructure.DeviceDetection;

/// <summary>
/// Bluetooth LE (kablosuz) ve XInput (kablolu USB / alıcı) algılama motorlarını tek bir çatı altında
/// birleştiren hibrit kompozit izleyici. Çoklu kol desteği ve akıllı çift-kayıt engelleme sağlar.
/// </summary>
public class CompositeDeviceWatcher : IDeviceWatcherService
{
    private readonly IDeviceWatcherService _bleWatcher;
    private readonly XInputDeviceWatcher _xinputWatcher;
    private readonly HashSet<int> _suppressedSlots = new();
    private readonly Dictionary<int, CancellationTokenSource> _pendingXInputTimers = new();
    private readonly object _syncLock = new();
    private bool _disposed;

    public event EventHandler<DeviceConnectionChangedEventArgs>? ConnectionChanged;
    public event EventHandler<int>? BatteryLevelUpdated;

    public bool IsRunning => _bleWatcher.IsRunning || _xinputWatcher.IsRunning;

    public DeviceInfo CurrentDevice
    {
        get
        {
            lock (_syncLock)
            {
                var list = ConnectedDevices;
                if (list.Count > 0) return list[0];
                return _bleWatcher.CurrentDevice;
            }
        }
    }

    public IReadOnlyList<DeviceInfo> ConnectedDevices
    {
        get
        {
            lock (_syncLock)
            {
                var result = new List<DeviceInfo>();

                // 1. Bluetooth üzerinden bağlı donanımlar (Örn: Xbox Kablosuz, DualSense)
                var bleConnected = _bleWatcher.ConnectedDevices;
                result.AddRange(bleConnected);

                // 2. XInput üzerinden bağlı cihazlar
                var xinputConnected = _xinputWatcher.ConnectedDevices;

                foreach (var xDev in xinputConnected)
                {
                    int slot = xDev.PlayerIndex ?? 0;
                    if (!_suppressedSlots.Contains(slot))
                    {
                        // Sadece BLE cihazlarının sayısını aşan gerçek ekstra donanımları ekle
                        if (xinputConnected.Count > bleConnected.Count && !result.Any(d => d.DeviceKey == xDev.DeviceKey))
                        {
                            result.Add(xDev);
                        }
                    }
                }

                return result;
            }
        }
    }

    public CompositeDeviceWatcher(
        IDeviceWatcherService? bleWatcher = null,
        XInputDeviceWatcher? xinputWatcher = null)
    {
        _bleWatcher = bleWatcher ?? new WindowsBluetoothDeviceWatcher();
        _xinputWatcher = xinputWatcher ?? new XInputDeviceWatcher();

        _bleWatcher.ConnectionChanged += OnBleConnectionChanged;
        _bleWatcher.BatteryLevelUpdated += OnBleBatteryLevelUpdated;

        _xinputWatcher.ConnectionChanged += OnXInputConnectionChanged;
        _xinputWatcher.BatteryLevelUpdated += OnXInputBatteryLevelUpdated;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _bleWatcher.StartAsync(cancellationToken);
        await _xinputWatcher.StartAsync(cancellationToken);
    }

    public Task StopAsync()
    {
        lock (_syncLock)
        {
            foreach (var kvp in _pendingXInputTimers)
            {
                kvp.Value.Cancel();
            }
            _pendingXInputTimers.Clear();
        }

        return Task.WhenAll(_bleWatcher.StopAsync(), _xinputWatcher.StopAsync());
    }

    private void OnBleConnectionChanged(object? sender, DeviceConnectionChangedEventArgs e)
    {
        lock (_syncLock)
        {
            if (e.Action == DebounceAction.SessionStarted)
            {
                // Bekleyen tüm XInput onay gecikmelerini derhal iptal et ve bu slotları bastır
                foreach (var kvp in _pendingXInputTimers)
                {
                    kvp.Value.Cancel();
                    _suppressedSlots.Add(kvp.Key);
                }
                _pendingXInputTimers.Clear();

                // Eğer BLE bağlanmadan önce bir XInput slotu 2 saniyelik onay süresini doldurup fiilen yayınlandıysa, onu bastır ve kapat
                var unsuppressed = _xinputWatcher.ConnectedDevices
                    .Where(d => d.PlayerIndex.HasValue && !_suppressedSlots.Contains(d.PlayerIndex.Value))
                    .ToList();

                foreach (var shadowDev in unsuppressed)
                {
                    _suppressedSlots.Add(shadowDev.PlayerIndex!.Value);

                    // Bu sanal XInput slotu için açılmış olan sahte oturumu sonlandır
                    ConnectionChanged?.Invoke(this, new DeviceConnectionChangedEventArgs(
                        DeviceConnectionState.Disconnected,
                        shadowDev,
                        DebounceAction.SessionEnded,
                        e.TimestampUtc));
                }
            }

            ConnectionChanged?.Invoke(this, e);
        }
    }

    private void OnBleBatteryLevelUpdated(object? sender, int batteryPercentage)
    {
        BatteryLevelUpdated?.Invoke(this, batteryPercentage);
    }

    private void OnXInputConnectionChanged(object? sender, DeviceConnectionChangedEventArgs e)
    {
        lock (_syncLock)
        {
            int slot = e.Device.PlayerIndex ?? 0;

            if (e.Action == DebounceAction.SessionStarted)
            {
                // Eğer halihazırda bağlı bir BLE cihazı varsa ve bu slot onun sanal yansımasıysa, anında bastır
                if (_bleWatcher.ConnectedDevices.Count > _suppressedSlots.Count)
                {
                    _suppressedSlots.Add(slot);
                    return;
                }

                // XInput sinyali Windows'ta Bluetooth BLE GATT servisinden ~100-800ms daha önce gelir.
                // Erken bildirim ("USB Kolu bağlandı") ve sahte oturum açılmasını önlemek için 2 saniyelik onay süresi uygula.
                if (_pendingXInputTimers.TryGetValue(slot, out var existingCts))
                {
                    existingCts.Cancel();
                }

                var cts = new CancellationTokenSource();
                _pendingXInputTimers[slot] = cts;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(2000, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        return; // BLE bağlandığı için veya bağlantı koptuğu için iptal edildi
                    }

                    lock (_syncLock)
                    {
                        _pendingXInputTimers.Remove(slot);

                        // 2 saniye sonra hala BLE bağlanmadıysa ve slot bastırılmadıysa, gerçek bir USB koldur
                        if (!_suppressedSlots.Contains(slot))
                        {
                            if (_bleWatcher.ConnectedDevices.Count > _suppressedSlots.Count)
                            {
                                _suppressedSlots.Add(slot);
                            }
                            else
                            {
                                ConnectionChanged?.Invoke(this, e);
                            }
                        }
                    }
                });

                return;
            }
            else if (e.Action == DebounceAction.SessionEnded)
            {
                if (_pendingXInputTimers.TryGetValue(slot, out var pendingCts))
                {
                    pendingCts.Cancel();
                    _pendingXInputTimers.Remove(slot);
                }

                if (_suppressedSlots.Remove(slot))
                {
                    return;
                }
            }
            else
            {
                if (_suppressedSlots.Contains(slot))
                {
                    return;
                }
            }

            ConnectionChanged?.Invoke(this, e);
        }
    }

    private void OnXInputBatteryLevelUpdated(object? sender, int batteryPercentage)
    {
        // Yalnızca BLE aktif değilse XInput bataryasını ilet
        if (_bleWatcher.ConnectedDevices.Count == 0)
        {
            BatteryLevelUpdated?.Invoke(this, batteryPercentage);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _bleWatcher.ConnectionChanged -= OnBleConnectionChanged;
            _bleWatcher.BatteryLevelUpdated -= OnBleBatteryLevelUpdated;
            _xinputWatcher.ConnectionChanged -= OnXInputConnectionChanged;
            _xinputWatcher.BatteryLevelUpdated -= OnXInputBatteryLevelUpdated;

            lock (_syncLock)
            {
                foreach (var kvp in _pendingXInputTimers)
                {
                    kvp.Value.Cancel();
                }
                _pendingXInputTimers.Clear();
            }

            _bleWatcher.Dispose();
            _xinputWatcher.Dispose();
        }
    }
}
