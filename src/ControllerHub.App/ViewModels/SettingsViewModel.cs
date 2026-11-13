using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControllerHub.Core.Interfaces;
using ControllerHub.Core.Models;
using ControllerHub.Infrastructure.Services;

namespace ControllerHub.App.ViewModels;

public partial class ControllerNicknameItem : ObservableObject
{
    public string DeviceKey { get; }
    public string DefaultName { get; }

    [ObservableProperty]
    private string _customName = "";

    [ObservableProperty]
    private string _statusMessage = "";

    public IRelayCommand SaveCommand { get; }

    public ControllerNicknameItem(string deviceKey, string defaultName, string customName, Func<string, string, Task> onSave)
    {
        DeviceKey = deviceKey;
        DefaultName = defaultName;
        _customName = customName;
        SaveCommand = new AsyncRelayCommand(async () =>
        {
            await onSave(DeviceKey, CustomName);
            StatusMessage = "✅ Kaydedildi";
            await Task.Delay(2000);
            StatusMessage = "";
        });
    }
}

public partial class SettingsViewModel : ViewModelBase
{
    private ISettingsRepository? _settingsRepository;
    private ISessionRepository? _sessionRepository;
    private IControllerMetadataRepository? _metadataRepository;
    private WindowsStartupService? _startupService;
    private bool _isInitializing = false;

    public override string Title => "Ayarlar";

    [ObservableProperty]
    private bool _startWithWindows = true;

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private bool _notificationsEnabled = true;

    [ObservableProperty]
    private string _databasePath = @"%LocalAppData%\ControllerHub\controllerhub.db";

    [ObservableProperty]
    private string _appVersion = "1.0.0 (Windows 11 x64 / .NET 8)";

    [ObservableProperty]
    private string _trackedControllerName = "Evrensel Oyun Kolu (Tüm Cihazlar)";

    public ObservableCollection<ControllerNicknameItem> KnownControllers { get; } = new();

    public event EventHandler? ControllerNicknamesUpdated;

    public void Initialize(
        ISettingsRepository settingsRepository,
        WindowsStartupService startupService,
        string databasePath,
        ISessionRepository? sessionRepository = null,
        IControllerMetadataRepository? metadataRepository = null)
    {
        _settingsRepository = settingsRepository;
        _startupService = startupService;
        DatabasePath = databasePath;
        _sessionRepository = sessionRepository;
        _metadataRepository = metadataRepository;

        _ = LoadSettingsAsync();
    }

    public async Task LoadSettingsAsync()
    {
        if (_settingsRepository == null) return;

        try
        {
            _isInitializing = true;
            var settings = await _settingsRepository.LoadSettingsAsync();

            MinimizeToTray = settings.MinimizeToTray;
            NotificationsEnabled = settings.NotificationsEnabled;

            // Kayıt defterindeki güncel durumu kontrol et
            if (_startupService != null)
            {
                StartWithWindows = _startupService.IsStartupEnabled();
            }
            else
            {
                StartWithWindows = settings.StartWithWindows;
            }

            // Tanımlı kontrolcüleri ve takma adlarını yükle
            await LoadKnownControllersAsync();
        }
        finally
        {
            _isInitializing = false;
        }
    }

    public async Task LoadKnownControllersAsync()
    {
        KnownControllers.Clear();

        var customNames = _metadataRepository != null
            ? await _metadataRepository.GetAllCustomNamesAsync()
            : new Dictionary<string, string>();

        var keys = new HashSet<string>(customNames.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).Select(kv => kv.Key));

        if (_sessionRepository != null)
        {
            var sessions = await _sessionRepository.GetRecentSessionsAsync(500);
            foreach (var s in sessions.Where(s => s.EndedAtUtc == null || s.DurationSeconds > 10))
            {
                if (!string.IsNullOrEmpty(s.DeviceKey) && s.DeviceKey != "XBOX_CONTROLLER_DYNAMIC")
                {
                    keys.Add(s.DeviceKey);
                }
            }
        }

        if (keys.Count == 0)
        {
            // Varsayılan Xbox kolu anahtarı ekle
            keys.Add("XBOX_CONTROLLER_DYNAMIC");
        }

        foreach (var key in keys)
        {
            string defaultName;
            if (key.StartsWith("XINPUT_SLOT_"))
            {
                var slotStr = key["XINPUT_SLOT_".Length..];
                defaultName = int.TryParse(slotStr, out var slot)
                    ? $"USB Kablolu Oyun Kolu (Oyuncu {slot + 1})"
                    : "USB Kablolu Oyun Kolu";
            }
            else if (key.StartsWith("XBOX_BLE_") && key.Length > 9)
            {
                var shortMac = key[9..];
                defaultName = $"Xbox Wireless Controller ({shortMac})";
            }
            else if (key.StartsWith("BT_") && key.Length > 3)
            {
                var shortMac = key[3..];
                var displayMac = shortMac.Length > 4 ? shortMac[^4..] : shortMac;
                defaultName = $"Wireless Controller ({displayMac})";
            }
            else
            {
                defaultName = "Kablosuz Kontrolcü";
            }

            string currentAlias = customNames.TryGetValue(key, out var alias) ? alias : "";

            KnownControllers.Add(new ControllerNicknameItem(key, defaultName, currentAlias, async (k, name) =>
            {
                if (_metadataRepository != null)
                {
                    await _metadataRepository.SetCustomNameAsync(k, name);
                    ControllerNicknamesUpdated?.Invoke(this, EventArgs.Empty);
                }
            }));
        }
    }

    async partial void OnStartWithWindowsChanged(bool value)
    {
        if (_isInitializing) return;

        _startupService?.SetStartup(value);
        await PersistCurrentSettingsAsync();
    }

    async partial void OnMinimizeToTrayChanged(bool value)
    {
        if (_isInitializing) return;
        await PersistCurrentSettingsAsync();
    }

    async partial void OnNotificationsEnabledChanged(bool value)
    {
        if (_isInitializing) return;
        await PersistCurrentSettingsAsync();
    }

    private async Task PersistCurrentSettingsAsync()
    {
        if (_settingsRepository == null) return;

        var settings = new AppSettings
        {
            StartWithWindows = StartWithWindows,
            MinimizeToTray = MinimizeToTray,
            NotificationsEnabled = NotificationsEnabled,
            Theme = "Light"
        };

        await _settingsRepository.SaveSettingsAsync(settings);
    }

    [RelayCommand]
    private void OpenDatabaseFolder()
    {
        try
        {
            var folder = Path.GetDirectoryName(DatabasePath);
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", folder)
                {
                    UseShellExecute = true
                });
            }
        }
        catch { }
    }
}
