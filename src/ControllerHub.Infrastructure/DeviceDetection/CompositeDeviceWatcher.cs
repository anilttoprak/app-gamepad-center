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

    public async Task StopAsync()
    {
        await _bleWatcher.StopAsync();
        await _xinputWatcher.StopAsync();
    }

    private void OnBleConnectionChanged(object? sender, DeviceConnectionChangedEventArgs e)
    {
        lock (_syncLock)
        {
            if (e.Action == DebounceAction.SessionStarted)
            {
                // Eğer BLE bağlanmadan önce bir XInput slotu bağlanmış ve henüz bastırılmamışsa,
                // o slot aslında bu BLE kolunun Windows sanal yansımasıdır. Onu hemen bastır ve kapat.
                var unsuppressed = _xinputWatcher.ConnectedDevices
                    .Where(d => d.PlayerIndex.HasValue && !_suppressedSlots.Contains(d.PlayerIndex.Value))
                    .ToList();

                if (unsuppressed.Count > 0)
                {
                    var shadowDev = unsuppressed[0];
                    _suppressedSlots.Add(shadowDev.PlayerIndex!.Value);

                    // Bu sanal XInput slotu için açılmış olabilecek sahte oturumu sonlandır
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
                // Eğer bağlı bir BLE cihazı varsa ve bu slot onun Windows XInput yansımasıysa, bastır
                if (_bleWatcher.ConnectedDevices.Count > _suppressedSlots.Count)
                {
                    _suppressedSlots.Add(slot);
                    return;
                }
            }
            else if (e.Action == DebounceAction.SessionEnded)
            {
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

            _bleWatcher.Dispose();
            _xinputWatcher.Dispose();
        }
    }
}
