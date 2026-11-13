using ControllerHub.Core.Interfaces;
using ControllerHub.Core.Models;

namespace ControllerHub.Infrastructure.DeviceDetection;

/// <summary>
/// Bluetooth LE (kablosuz) ve XInput (kablolu USB / alıcı) algılama motorlarını tek bir çatı altında
/// birleştiren hibrit kompozit izleyici. Çoklu kol desteği ve akıllı çift-kayıt engelleme sağlar.
/// </summary>
public class CompositeDeviceWatcher : IDeviceWatcherService
{
    private readonly IDeviceWatcherService _bleWatcher;
    private readonly XInputDeviceWatcher _xinputWatcher;
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

                // 1. Bluetooth üzerinden bağlı cihazlar (Örn: Xbox Kablosuz, DualSense)
                var bleConnected = _bleWatcher.ConnectedDevices;
                result.AddRange(bleConnected);

                // 2. XInput üzerinden bağlı cihazlar
                var xinputConnected = _xinputWatcher.ConnectedDevices;

                foreach (var xDev in xinputConnected)
                {
                    // Eğer kol kabloluysa (USB Logitech, kablolu Xbox vb.) kesinlikle bağımsız cihazdır
                    if (xDev.ConnectionType == ControllerConnectionType.WiredUsb)
                    {
                        result.Add(xDev);
                    }
                    else
                    {
                        // Kablosuz XInput slotu: Eğer BLE ile bağlı bir kol zaten varsa ve bu ilk kablosuz slot ise,
                        // Windows'un BLE kolunu XInput'a yansıtmasıdır; çift kayıt olmaması için atla.
                        // Ancak BLE'deki kol sayısından fazla kablosuz slot varsa (2. kablosuz alıcı gibi), onu da ekle.
                        if (bleConnected.Count == 0 || result.Count(d => d.ConnectionType == ControllerConnectionType.WirelessAdapter) < xinputConnected.Count(d => d.ConnectionType == ControllerConnectionType.WirelessAdapter) - bleConnected.Count)
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
        ConnectionChanged?.Invoke(this, e);
    }

    private void OnBleBatteryLevelUpdated(object? sender, int batteryPercentage)
    {
        BatteryLevelUpdated?.Invoke(this, batteryPercentage);
    }

    private void OnXInputConnectionChanged(object? sender, DeviceConnectionChangedEventArgs e)
    {
        // Eğer kablolu bir kol ise doğrudan ilet
        if (e.Device.ConnectionType == ControllerConnectionType.WiredUsb)
        {
            ConnectionChanged?.Invoke(this, e);
            return;
        }

        // Kablosuz XInput: Eğer halihazırda bağlı bir BLE cihazı varsa, bu sinyal onun XInput gölgesidir, mükerrer üretme
        if (_bleWatcher.ConnectedDevices.Count > 0)
        {
            return;
        }

        ConnectionChanged?.Invoke(this, e);
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
