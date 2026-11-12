using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControllerHub.Core.Interfaces;
using ControllerHub.Core.Models;
using ControllerHub.Infrastructure.DeviceDetection;

namespace ControllerHub.App.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IDeviceWatcherService _deviceWatcher;
    private bool _disposed;

    public DashboardViewModel DashboardVM { get; } = new();
    public SessionsViewModel SessionsVM { get; } = new();
    public AnalyticsViewModel AnalyticsVM { get; } = new();
    public BatteryViewModel BatteryVM { get; } = new();
    public SettingsViewModel SettingsVM { get; } = new();

    [ObservableProperty]
    private ViewModelBase _currentView;

    [ObservableProperty]
    private string _activeControllerName = "Xbox Wireless Controller";

    [ObservableProperty]
    private string _connectionStatusBadge = "⚪ Bağlı Değil";

    [ObservableProperty]
    private bool _isDeviceConnected = false;

    [ObservableProperty]
    private string _hardwareDiagnosticsText = "PnP: Başlatılıyor... | Debounce: 4s aktif";

    [ObservableProperty]
    private string _statusColorBrush = "#667085"; // Default gray

    public MainViewModel()
    {
        _currentView = DashboardVM;
        _deviceWatcher = new WindowsBluetoothDeviceWatcher();

        _deviceWatcher.ConnectionChanged += OnDeviceConnectionChanged;
        _deviceWatcher.BatteryLevelUpdated += OnBatteryLevelUpdated;

        _ = _deviceWatcher.StartAsync();
    }

    private void OnDeviceConnectionChanged(object? sender, DeviceConnectionChangedEventArgs e)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            IsDeviceConnected = (e.State == DeviceConnectionState.Connected);
            ActiveControllerName = e.Device.Name;

            (ConnectionStatusBadge, StatusColorBrush) = e.State switch
            {
                DeviceConnectionState.Connected => ("🟢 Bağlı", "#16A36A"),
                DeviceConnectionState.Connecting => ("🟡 Bağlanıyor...", "#D97706"),
                DeviceConnectionState.Disconnecting => ("🟠 Doğrulanıyor...", "#D97706"),
                DeviceConnectionState.Disconnected => ("⚪ Bağlı Değil", "#DC4C64"),
                _ => ("Bilinmiyor", "#667085")
            };

            HardwareDiagnosticsText = $"Durum: {e.State} | Aksiyon: {e.Action} | Zaman: {e.TimestampUtc.ToLocalTime():HH:mm:ss}";

            // Dashboard ve Pil ekranlarını canlı bilgilendir
            DashboardVM.IsConnected = IsDeviceConnected;
            DashboardVM.ConnectionStateText = IsDeviceConnected ? "🟢 Bağlı (Canlı Takip Ediliyor)" : "⚪ Bağlı Değil";

            if (e.Device.BatteryLevel.HasValue)
            {
                ApplyBatteryLevel(e.Device.BatteryLevel.Value);
            }
        });
    }

    private void OnBatteryLevelUpdated(object? sender, int batteryPercentage)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            ApplyBatteryLevel(batteryPercentage);
        });
    }

    private void ApplyBatteryLevel(int percentage)
    {
        DashboardVM.BatteryLevelText = $"%{percentage}";
        BatteryVM.CurrentPercentage = $"%{percentage}";
        BatteryVM.LastUpdatedText = $"{DateTime.Now:dd MMM yyyy HH:mm:ss} (Bluetooth GATT 0x180F Canlı)";
    }

    [RelayCommand]
    private void Navigate(string viewName)
    {
        CurrentView = viewName switch
        {
            "Dashboard" => DashboardVM,
            "Sessions" => SessionsVM,
            "Analytics" => AnalyticsVM,
            "Battery" => BatteryVM,
            "Settings" => SettingsVM,
            _ => DashboardVM
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _deviceWatcher.ConnectionChanged -= OnDeviceConnectionChanged;
            _deviceWatcher.BatteryLevelUpdated -= OnBatteryLevelUpdated;
            _deviceWatcher.Dispose();
        }
    }
}
