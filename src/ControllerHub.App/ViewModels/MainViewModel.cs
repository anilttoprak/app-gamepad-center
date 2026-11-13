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
    private readonly ISettingsRepository _settingsRepository;
    private readonly WindowsStartupService _startupService;
    private readonly IDeviceWatcherService _deviceWatcher;
    private readonly SessionTrackerService _sessionTracker;

    private bool _disposed;

    public event EventHandler<(string Title, string Message)>? NotificationRequested;
    public event EventHandler<string>? TrayStatusUpdated;

    public DashboardViewModel DashboardVM { get; } = new();
    public SessionsViewModel SessionsVM { get; } = new();
    public AnalyticsViewModel AnalyticsVM { get; } = new();
    public BatteryViewModel BatteryVM { get; } = new();
    public SettingsViewModel SettingsVM { get; } = new();

    public bool MinimizeToTray => SettingsVM.MinimizeToTray;

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
        _settingsRepository = new SqliteSettingsRepository(_dbContext);
        _startupService = new WindowsStartupService();

        // 2. Donanım İzleyici (Hibrit BLE + XInput) ve Oturum Takip Servisi
        _deviceWatcher = new CompositeDeviceWatcher();
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
            DashboardVM.NavigationRequested += async (_, target) => await NavigateAsync(target);
            BatteryVM.Initialize(_batteryRepository, _deviceWatcher, _sessionRepository, _sessionTracker);
            SessionsVM.Initialize(_sessionRepository, _sessionTracker);
            AnalyticsVM.Initialize(_sessionRepository, _batteryRepository, _sessionTracker);
            SettingsVM.Initialize(_settingsRepository, _startupService, _dbContext.DatabasePath);

            await DashboardVM.RefreshDataAsync();
            await BatteryVM.RefreshDataAsync();
            await SessionsVM.RefreshDataAsync();
            await AnalyticsVM.RefreshDataAsync();

            HardwareDiagnosticsText = "SQLite (WAL) Hazır | 4s Debounce Aktif";
        }
        catch (Exception ex)
        {
            HardwareDiagnosticsText = $"Başlatma Hatası: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task NavigateAsync(string viewName)
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

        if (CurrentView is DashboardViewModel db) await db.RefreshDataAsync();
        else if (CurrentView is SessionsViewModel sv) await sv.RefreshDataAsync();
        else if (CurrentView is AnalyticsViewModel av) await av.RefreshDataAsync();
        else if (CurrentView is BatteryViewModel bv) await bv.RefreshDataAsync();
        else if (CurrentView is SettingsViewModel st) await st.LoadSettingsAsync();
    }

    private void OnDeviceConnectionChanged(object? sender, DeviceConnectionChangedEventArgs e)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            var connectedList = _deviceWatcher.ConnectedDevices;
            IsDeviceConnected = connectedList.Count > 0;

            if (connectedList.Count == 0)
            {
                ActiveControllerName = "Bağlı Kontrolcü Yok";
                ConnectionStatusBadge = "⚪ Bağlı Değil";
                StatusColorBrush = "#DC4C64";
                DashboardVM.IsConnected = false;
                DashboardVM.ConnectionStateText = "⚪ Bağlı Değil";
                TrayStatusUpdated?.Invoke(this, "⚪ Bağlı Değil");
            }
            else if (connectedList.Count == 1)
            {
                var dev = connectedList[0];
                ActiveControllerName = dev.Name;
                var connTypeStr = dev.ConnectionType == ControllerConnectionType.WiredUsb ? "Kablolu" : "BT";
                ConnectionStatusBadge = $"🟢 Bağlı ({connTypeStr})";
                StatusColorBrush = "#16A36A";
                DashboardVM.IsConnected = true;
                DashboardVM.ConnectionStateText = $"🟢 Bağlı ({dev.Name})";
                DashboardVM.ActiveControllerName = dev.Name;
                TrayStatusUpdated?.Invoke(this, $"🟢 {dev.Name}");
            }
            else
            {
                // Çoklu kontrolcü bağlı
                ActiveControllerName = $"{connectedList.Count} Kontrolcü Bağlı";
                ConnectionStatusBadge = $"🟢 {connectedList.Count} Kol Aktif";
                StatusColorBrush = "#16A36A";
                DashboardVM.IsConnected = true;
                DashboardVM.ConnectionStateText = $"🟢 {connectedList.Count} Kol Bağlı ({string.Join(" + ", connectedList.Select(c => c.Name.Split(' ')[0]))})";
                DashboardVM.ActiveControllerName = $"{connectedList.Count} Kontrolcü Bağlı";
                TrayStatusUpdated?.Invoke(this, $"🟢 {connectedList.Count} Kol Aktif");
            }

            HardwareDiagnosticsText = $"Donanım: {connectedList.Count} Cihaz | PnP: {e.State} | Aksiyon: {e.Action} | Saat: {e.TimestampUtc.ToLocalTime():HH:mm:ss}";

            if (e.Device.BatteryLevel.HasValue)
            {
                ApplyBatteryLevel(e.Device.BatteryLevel.Value);
            }
            else if (e.Device.ConnectionType == ControllerConnectionType.WiredUsb)
            {
                DashboardVM.BatteryLevelText = "🔌";
                DashboardVM.BatterySourceText = "USB Sürekli Güç (Kablolu)";
                DashboardVM.BatteryConfidenceBadge = "Kablolu Bağlantı";
            }
        });
    }

    private void OnActiveSessionTicked(object? sender, TimeSpan activeDuration)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            var formatted = SessionTimeCalculator.FormatDurationText(activeDuration);
            TrayStatusUpdated?.Invoke(this, $"🟢 Bağlı ({formatted})");
        });
    }

    private void OnSessionStarted(object? sender, ControllerSession session)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            HardwareDiagnosticsText = $"Oturum #{session.Id} Başlatıldı ({session.StartedAtUtc.ToLocalTime():HH:mm:ss})";

            if (SettingsVM.NotificationsEnabled)
            {
                NotificationRequested?.Invoke(this, (
                    "🎮 Xbox Kontrolcüsü Bağlandı",
                    $"{ActiveControllerName} bağlandı. Oturum kaydı başlatıldı."));
            }
            TrayStatusUpdated?.Invoke(this, "🟢 Bağlı (Aktif)");
        });
    }

    private void OnSessionEnded(object? sender, ControllerSession session)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            var formatted = SessionTimeCalculator.FormatDurationText(TimeSpan.FromSeconds(session.DurationSeconds));
            HardwareDiagnosticsText = $"Oturum #{session.Id} Kapatıldı (Süre: {formatted})";

            if (SettingsVM.NotificationsEnabled)
            {
                NotificationRequested?.Invoke(this, (
                    "⚪ Kontrolcü Bağlantısı Kesildi",
                    $"Oturum tamamlandı. Süre: {formatted}"));
            }
            TrayStatusUpdated?.Invoke(this, "⚪ Bağlı Değil");
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
        BatteryVM.UpdateCurrentPercentage(
            percentage,
            DateTimeOffset.UtcNow,
            BatterySource.WindowsApiGatt,
            BatteryConfidence.Exact,
            IsDeviceConnected);

        DashboardVM.BatteryLevelText = $"%{percentage}";
        DashboardVM.BatterySourceText = "Bluetooth GATT (0x180F)";
        DashboardVM.BatteryConfidenceBadge = "Kademeli Donanım Ölçümü";

        if (BatteryVM.HasPendingProposal)
        {
            DashboardVM.HasBatteryProposal = true;
            DashboardVM.BatteryProposalText = BatteryVM.PendingProposalText;
        }
        else
        {
            DashboardVM.HasBatteryProposal = false;
        }
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
