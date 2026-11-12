using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControllerHub.Core.Interfaces;
using ControllerHub.Core.Models;
using ControllerHub.Core.Services;
using ControllerHub.Infrastructure.DeviceDetection;
using ControllerHub.Infrastructure.Persistence;
using ControllerHub.Infrastructure.Services;

namespace ControllerHub.App.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly SqliteDatabaseContext _dbContext;
    private readonly ISessionRepository _sessionRepository;
    private readonly IBatteryRepository _batteryRepository;
    private readonly IDeviceWatcherService _deviceWatcher;
    private readonly SessionTrackerService _sessionTracker;

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
    private string _hardwareDiagnosticsText = "SQLite ve PnP başlatılıyor...";

    [ObservableProperty]
    private string _statusColorBrush = "#667085";

    public MainViewModel()
    {
        _currentView = DashboardVM;

        // 1. SQLite Veritabanı ve Repository Katmanı
        _dbContext = new SqliteDatabaseContext();
        _sessionRepository = new SqliteSessionRepository(_dbContext);
        _batteryRepository = new SqliteBatteryRepository(_dbContext);

        // 2. Donanım İzleyici ve Oturum Takip Servisi
        _deviceWatcher = new WindowsBluetoothDeviceWatcher();
        _sessionTracker = new SessionTrackerService(_deviceWatcher, _sessionRepository, _batteryRepository);

        _deviceWatcher.ConnectionChanged += OnDeviceConnectionChanged;
        _deviceWatcher.BatteryLevelUpdated += OnBatteryLevelUpdated;

        _sessionTracker.ActiveSessionTicked += OnActiveSessionTicked;
        _sessionTracker.SessionStarted += OnSessionStarted;
        _sessionTracker.SessionEnded += OnSessionEnded;

        // 3. Servisleri Başlat
        _ = InitializeServicesAsync();
    }

    private async Task InitializeServicesAsync()
    {
        try
        {
            await _sessionTracker.InitializeAsync();
            DashboardVM.Initialize(_sessionRepository, _batteryRepository, _sessionTracker);
            await DashboardVM.RefreshDataAsync();
            HardwareDiagnosticsText = "SQLite (WAL) Hazır | 4s Debounce Aktif";
        }
        catch (Exception ex)
        {
            HardwareDiagnosticsText = $"Başlatma Hatası: {ex.Message}";
        }
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

            HardwareDiagnosticsText = $"PnP: {e.State} | Aksiyon: {e.Action} | Saat: {e.TimestampUtc.ToLocalTime():HH:mm:ss}";

            DashboardVM.IsConnected = IsDeviceConnected;
            DashboardVM.ConnectionStateText = IsDeviceConnected ? "🟢 Bağlı (Canlı Süre İşliyor)" : "⚪ Bağlı Değil";

            if (e.Device.BatteryLevel.HasValue)
            {
                ApplyBatteryLevel(e.Device.BatteryLevel.Value);
            }
        });
    }

    private void OnActiveSessionTicked(object? sender, TimeSpan activeDuration)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            // FR-04: Aktif oturum süresini ekranda canlı güncelle
            var formatted = SessionTimeCalculator.FormatDurationText(activeDuration);
            DashboardVM.TodayConnectionTime = $"{formatted} (Aktif Canlı)";
        });
    }

    private void OnSessionStarted(object? sender, ControllerSession session)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            HardwareDiagnosticsText = $"Oturum #{session.Id} Başlatıldı ({session.StartedAtUtc.ToLocalTime():HH:mm:ss})";
        });
    }

    private void OnSessionEnded(object? sender, ControllerSession session)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            var formatted = SessionTimeCalculator.FormatDurationText(TimeSpan.FromSeconds(session.DurationSeconds));
            HardwareDiagnosticsText = $"Oturum #{session.Id} Kapatıldı (Süre: {formatted})";
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
        BatteryVM.LastUpdatedText = $"{DateTime.Now:dd MMM yyyy HH:mm:ss} (Bluetooth GATT 0x180F)";
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
            _sessionTracker.ActiveSessionTicked -= OnActiveSessionTicked;
            _sessionTracker.SessionStarted -= OnSessionStarted;
            _sessionTracker.SessionEnded -= OnSessionEnded;

            _sessionTracker.ShutdownAsync().GetAwaiter().GetResult();
            _sessionTracker.Dispose();
            _deviceWatcher.Dispose();
        }
    }
}
