using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControllerHub.App.Views;
using ControllerHub.Core.Interfaces;
using ControllerHub.Core.Models;
using ControllerHub.Core.Services;
using ControllerHub.Infrastructure.DeviceDetection;
using ControllerHub.Infrastructure.Persistence;
using ControllerHub.Infrastructure.Services;

namespace ControllerHub.App.ViewModels;

public partial class ConnectedDeviceCardViewModel : ObservableObject
{
    public string DeviceKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string HardwareName { get; set; } = string.Empty;
    public string ConnectionBadge { get; set; } = "BT";
    public string BatteryText { get; set; } = "%100";
    public string Icon { get; set; } = "🎮";
    public bool IsWired { get; set; }
}

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly SqliteDatabaseContext _dbContext;
    private readonly ISessionRepository _sessionRepository;
    private readonly IBatteryRepository _batteryRepository;
    private readonly ISettingsRepository _settingsRepository;
    private readonly IControllerMetadataRepository _metadataRepository;
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

    public ObservableCollection<ConnectedDeviceCardViewModel> ConnectedControllers { get; } = new();

    [ObservableProperty]
    private bool _hasConnectedControllers = false;

    [ObservableProperty]
    private bool _hasNoConnectedControllers = true;

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
        _metadataRepository = new SqliteControllerMetadataRepository(_dbContext);
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
            DashboardVM.Initialize(_sessionRepository, _batteryRepository, _sessionTracker, _metadataRepository);
            DashboardVM.NavigationRequested += async (_, target) => await NavigateAsync(target);
            BatteryVM.Initialize(_batteryRepository, _deviceWatcher, _sessionRepository, _sessionTracker);
            SessionsVM.Initialize(_sessionRepository, _sessionTracker);
            AnalyticsVM.Initialize(_sessionRepository, _batteryRepository, _sessionTracker);
            SettingsVM.Initialize(_settingsRepository, _startupService, _dbContext.DatabasePath, _sessionRepository, _metadataRepository);

            SettingsVM.ControllerNicknamesUpdated += async (_, _) =>
            {
                await UpdateConnectedControllersUiAsync();
                await SyncControllerFiltersAsync();
            };

            DashboardVM.ControllerFilterChanged += (_, opt) => OnFilterChangedFromView(opt.DeviceKey, DashboardVM);
            SessionsVM.ControllerFilterChanged += (_, opt) => OnFilterChangedFromView(opt.DeviceKey, SessionsVM);
            AnalyticsVM.ControllerFilterChanged += (_, opt) => OnFilterChangedFromView(opt.DeviceKey, AnalyticsVM);
            BatteryVM.ControllerFilterChanged += (_, opt) => OnFilterChangedFromView(opt.DeviceKey, BatteryVM);

            await SyncControllerFiltersAsync();
            await UpdateConnectedControllersUiAsync();
            await DashboardVM.RefreshDataAsync();

            HardwareDiagnosticsText = "SQLite (WAL) Hazır | 4s Debounce Aktif";
        }
        catch (Exception ex)
        {
            HardwareDiagnosticsText = $"Başlatma Hatası: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RenameControllerAsync(ConnectedDeviceCardViewModel? item)
    {
        if (item == null) return;

        var dialog = new RenameControllerDialog(item.HardwareName, item.DisplayName);
        dialog.Owner = Application.Current?.MainWindow;
        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.CustomName))
        {
            await _metadataRepository.SetCustomNameAsync(item.DeviceKey, dialog.CustomName);
            await UpdateConnectedControllersUiAsync();
            await DashboardVM.RefreshDataAsync();
            await SessionsVM.RefreshDataAsync();
            await SettingsVM.LoadKnownControllersAsync();
        }
    }

    private async Task UpdateConnectedControllersUiAsync()
    {
        var connectedList = _deviceWatcher.ConnectedDevices;
        var customNames = await _metadataRepository.GetAllCustomNamesAsync();

        ConnectedControllers.Clear();
        foreach (var dev in connectedList)
        {
            string displayName = customNames.TryGetValue(dev.DeviceKey, out var alias) && !string.IsNullOrWhiteSpace(alias)
                ? alias
                : dev.Name;

            string badge = dev.ConnectionType switch
            {
                ControllerConnectionType.WiredUsb => "USB",
                ControllerConnectionType.WirelessAdapter => "Alıcı",
                _ => "BT"
            };

            string battery = dev.ConnectionType == ControllerConnectionType.WiredUsb
                ? "🔌 Sürekli Güç"
                : (dev.BatteryLevel.HasValue ? $"🔋 %{dev.BatteryLevel.Value}" : "🔋 Bağlı");

            string icon = dev.ConnectionType == ControllerConnectionType.WiredUsb ? "🔌" : "🎮";

            ConnectedControllers.Add(new ConnectedDeviceCardViewModel
            {
                DeviceKey = dev.DeviceKey,
                DisplayName = displayName,
                HardwareName = dev.Name,
                ConnectionBadge = badge,
                BatteryText = battery,
                Icon = icon,
                IsWired = dev.ConnectionType == ControllerConnectionType.WiredUsb
            });
        }

        HasConnectedControllers = ConnectedControllers.Count > 0;
        HasNoConnectedControllers = !HasConnectedControllers;
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
        Application.Current?.Dispatcher?.BeginInvoke(() =>
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

            _ = UpdateConnectedControllersUiAsync();

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
            else
            {
                DashboardVM.BatteryLevelText = "🎮";
                DashboardVM.BatterySourceText = "Bluetooth Classic / Standart HID";
                DashboardVM.BatteryConfidenceBadge = "Kablosuz Bağlantı";
            }
        });
    }

    private void OnActiveSessionTicked(object? sender, TimeSpan activeDuration)
    {
        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            var formatted = SessionTimeCalculator.FormatDurationText(activeDuration);
            TrayStatusUpdated?.Invoke(this, $"🟢 Bağlı ({formatted})");
        });
    }

    private void OnSessionStarted(object? sender, ControllerSession session)
    {
        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            HardwareDiagnosticsText = $"Oturum #{session.Id} Başlatıldı ({session.StartedAtUtc.ToLocalTime():HH:mm:ss})";

            if (SettingsVM.NotificationsEnabled)
            {
                NotificationRequested?.Invoke(this, (
                    "🎮 Kontrolcü Bağlandı",
                    $"{ActiveControllerName} bağlandı. Oturum kaydı başlatıldı."));
            }
            TrayStatusUpdated?.Invoke(this, "🟢 Bağlı (Aktif)");
            _ = SyncControllerFiltersAsync();
        });
    }

    private void OnSessionEnded(object? sender, ControllerSession session)
    {
        Application.Current?.Dispatcher?.BeginInvoke(() =>
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
            _ = SyncControllerFiltersAsync();
        });
    }

    private void OnBatteryLevelUpdated(object? sender, int batteryPercentage)
    {
        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            ApplyBatteryLevel(batteryPercentage);
        });
    }

    private bool _isUserExplicitSelection = false;
    private string _currentSelectedDeviceKey = "XBOX_BLE_78862EE89F4C";

    private async Task<List<ControllerFilterOption>> BuildFilterOptionsAsync()
    {
        var customNames = _metadataRepository != null
            ? await _metadataRepository.GetAllCustomNamesAsync()
            : new Dictionary<string, string>();

        var activeKeys = _sessionTracker?.ActiveSessions.Select(a => a.DeviceKey).ToHashSet() ?? new HashSet<string>();
        var sessions = _sessionRepository != null ? await _sessionRepository.GetRecentSessionsAsync(500) : Array.Empty<ControllerSession>();

        var deviceKeys = sessions
            .Where(s => s.DurationSeconds > 10 || activeKeys.Contains(s.DeviceKey))
            .Select(s => s.DeviceKey)
            .Concat(activeKeys)
            .Concat(customNames.Keys)
            .Where(k => !string.IsNullOrEmpty(k) && k != "XBOX_CONTROLLER_DYNAMIC")
            .Distinct()
            .ToList();

        var list = new List<ControllerFilterOption>
        {
            new ControllerFilterOption("ALL", "🔘 Tüm Kontrolcüler (Genel Özet)")
        };

        foreach (var key in deviceKeys)
        {
            string name;
            if (customNames.TryGetValue(key, out var alias) && !string.IsNullOrWhiteSpace(alias))
            {
                name = alias;
            }
            else if (key.StartsWith("XINPUT_SLOT_"))
            {
                var slotStr = key["XINPUT_SLOT_".Length..];
                name = int.TryParse(slotStr, out var slot)
                    ? $"USB Kablolu Kol (Oyuncu {slot + 1})"
                    : "USB Kablolu Oyun Kolu";
            }
            else if (key.StartsWith("XBOX_BLE_") && key.Length > 9)
            {
                var shortMac = key[9..];
                name = $"Xbox Wireless Controller ({shortMac})";
            }
            else if (key.StartsWith("BT_") && key.Length > 3)
            {
                var shortMac = key[3..];
                var displayMac = shortMac.Length > 4 ? shortMac[^4..] : shortMac;
                name = $"Wireless Controller ({displayMac})";
            }
            else
            {
                name = "Kablosuz Kontrolcü";
            }

            list.Add(new ControllerFilterOption(key, $"🎮 {name}"));
        }

        return list;
    }

    public async Task SyncControllerFiltersAsync()
    {
        var options = await BuildFilterOptionsAsync();

        // Varsayılan olarak "Anıl Xbox" kontrolcüsü seçilir
        var anilOption = options.FirstOrDefault(o =>
            o.DeviceKey == "XBOX_BLE_78862EE89F4C" ||
            o.DisplayName.Contains("Anıl Xbox", StringComparison.OrdinalIgnoreCase) ||
            o.DisplayName.Contains("Anil Xbox", StringComparison.OrdinalIgnoreCase));

        // Kullanıcı bu oturumda elle başka bir seçim yapmadıysa varsayılan Anıl Xbox'tır
        if (!_isUserExplicitSelection && anilOption != null)
        {
            _currentSelectedDeviceKey = anilOption.DeviceKey;
        }
        else if (!options.Any(o => o.DeviceKey == _currentSelectedDeviceKey))
        {
            _currentSelectedDeviceKey = anilOption?.DeviceKey ?? options.FirstOrDefault()?.DeviceKey ?? "ALL";
        }

        DashboardVM.UpdateFilterOptions(options, _currentSelectedDeviceKey);
        SessionsVM.UpdateFilterOptions(options, _currentSelectedDeviceKey);
        AnalyticsVM.UpdateFilterOptions(options, _currentSelectedDeviceKey);
        BatteryVM.UpdateFilterOptions(options, _currentSelectedDeviceKey);
    }

    private void OnFilterChangedFromView(string deviceKey, ViewModelBase source)
    {
        _isUserExplicitSelection = true;
        _currentSelectedDeviceKey = deviceKey;

        if (source != DashboardVM) DashboardVM.SelectedControllerFilter = DashboardVM.ControllerFilterOptions.FirstOrDefault(o => o.DeviceKey == deviceKey);
        if (source != SessionsVM) SessionsVM.SelectedControllerFilter = SessionsVM.ControllerFilterOptions.FirstOrDefault(o => o.DeviceKey == deviceKey);
        if (source != AnalyticsVM) AnalyticsVM.SelectedControllerFilter = AnalyticsVM.ControllerFilterOptions.FirstOrDefault(o => o.DeviceKey == deviceKey);
        if (source != BatteryVM) BatteryVM.SelectedControllerFilter = BatteryVM.ControllerFilterOptions.FirstOrDefault(o => o.DeviceKey == deviceKey);
    }

    private void ApplyBatteryLevel(int percentage)
    {
        var activeDev = _deviceWatcher.CurrentDevice;
        var deviceKey = activeDev?.DeviceKey ?? "XBOX_CONTROLLER_DYNAMIC";

        BatteryVM.UpdateCurrentPercentage(
            percentage,
            DateTimeOffset.UtcNow,
            BatterySource.WindowsApiGatt,
            BatteryConfidence.Exact,
            IsDeviceConnected,
            deviceKey);

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

    public async Task ShutdownGracefullyAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        _disposed = true;

        _deviceWatcher.ConnectionChanged -= OnDeviceConnectionChanged;
        _deviceWatcher.BatteryLevelUpdated -= OnBatteryLevelUpdated;
        _sessionTracker.ActiveSessionTicked -= OnActiveSessionTicked;
        _sessionTracker.SessionStarted -= OnSessionStarted;
        _sessionTracker.SessionEnded -= OnSessionEnded;

        try
        {
            await _sessionTracker.ShutdownAsync();
        }
        catch { }

        try
        {
            _sessionTracker.Dispose();
        }
        catch { }

        try
        {
            await _deviceWatcher.StopAsync();
            _deviceWatcher.Dispose();
        }
        catch { }
    }

    public void Dispose()
    {
        _ = ShutdownGracefullyAsync();
    }
}
