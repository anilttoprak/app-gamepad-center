using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
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
    public IRelayCommand DeleteProfileCommand { get; }

    public ControllerNicknameItem(
        string deviceKey, 
        string defaultName, 
        string customName, 
        Func<string, string, Task> onSave,
        Func<ControllerNicknameItem, Task> onDelete)
    {
        DeviceKey = deviceKey;
        DefaultName = defaultName;
        _customName = customName;

        SaveCommand = new AsyncRelayCommand(async () =>
        {
            var trimmed = CustomName?.Trim() ?? "";
            if (trimmed.Length > 50) trimmed = trimmed[..50];
            CustomName = trimmed;

            await onSave(DeviceKey, CustomName);
            StatusMessage = "✅ Kaydedildi";
            await Task.Delay(2000);
            StatusMessage = "";
        });

        DeleteProfileCommand = new AsyncRelayCommand(async () =>
        {
            await onDelete(this);
        });
    }
}

public partial class SettingsViewModel : ViewModelBase
{
    private readonly object _knownControllersLock = new();
    private ISettingsRepository? _settingsRepository;
    private ISessionRepository? _sessionRepository;
    private IControllerMetadataRepository? _metadataRepository;
    private WindowsStartupService? _startupService;
    private bool _isInitializing = false;
    private CancellationTokenSource? _feedbackCts;

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

    [ObservableProperty]
    private bool _hasKnownControllers;

    [ObservableProperty]
    private string _feedbackMessage = string.Empty;

    [ObservableProperty]
    private bool _isFeedbackVisible;

    public ObservableCollection<ControllerNicknameItem> KnownControllers { get; } = new();

    public event EventHandler? ControllerNicknamesUpdated;
    public event EventHandler<string>? ControllerProfileDeleted;

    public SettingsViewModel()
    {
        BindingOperations.EnableCollectionSynchronization(KnownControllers, _knownControllersLock);
    }

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
                if (StartWithWindows)
                {
                    // Başlangıç kayıtlarının (--autostart ve kısayol) eksiksiz olduğundan emin ol
                    _startupService.SetStartup(true);
                }
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

        var items = new List<ControllerNicknameItem>();
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

            items.Add(new ControllerNicknameItem(key, defaultName, currentAlias, async (k, name) =>
            {
                if (_metadataRepository != null)
                {
                    await _metadataRepository.SetCustomNameAsync(k, name);
                    ControllerNicknamesUpdated?.Invoke(this, EventArgs.Empty);
                }
            }, OnDeleteProfileAsync));
        }

        void UpdateList()
        {
            KnownControllers.Clear();
            foreach (var it in items)
            {
                KnownControllers.Add(it);
            }
            HasKnownControllers = KnownControllers.Count > 0;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(UpdateList);
        }
        else
        {
            UpdateList();
        }
    }

    public void ShowFeedback(string message, int durationSeconds = 4)
    {
        _feedbackCts?.Cancel();
        _feedbackCts = new CancellationTokenSource();
        var token = _feedbackCts.Token;

        FeedbackMessage = message;
        IsFeedbackVisible = true;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(durationSeconds), token);
                if (!token.IsCancellationRequested)
                {
                    var dispatcher = Application.Current?.Dispatcher;
                    if (dispatcher != null && !dispatcher.CheckAccess())
                    {
                        dispatcher.Invoke(() => IsFeedbackVisible = false);
                    }
                    else
                    {
                        IsFeedbackVisible = false;
                    }
                }
            }
            catch (TaskCanceledException) { }
        }, token);
    }

    [RelayCommand]
    private void DismissFeedback()
    {
        _feedbackCts?.Cancel();
        IsFeedbackVisible = false;
    }

    private async Task OnDeleteProfileAsync(ControllerNicknameItem item)
    {
        var displayName = !string.IsNullOrWhiteSpace(item.CustomName) ? item.CustomName : item.DefaultName;
        var confirm = MessageBox.Show(
            Application.Current?.MainWindow,
            $"'{displayName}' ({item.DeviceKey}) kontrolcü profilini ve ilişkili tüm oturum/pil geçmişini silmek istediğinize emin misiniz?\n\nBu işlem geri alınamaz.",
            "Kontrolcü Profilini Kaldır",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        if (_metadataRepository != null)
        {
            await _metadataRepository.DeleteControllerProfileAsync(item.DeviceKey);
        }

        void RemoveItem()
        {
            KnownControllers.Remove(item);
            HasKnownControllers = KnownControllers.Count > 0;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(RemoveItem);
        }
        else
        {
            RemoveItem();
        }

        ControllerProfileDeleted?.Invoke(this, item.DeviceKey);
        ShowFeedback($"🗑️ '{displayName}' kontrolcü profili ve ilişkili tüm kayıtlar silindi.");
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
    private void CreateDesktopShortcut()
    {
        var success = ShortcutService.CreateDesktopShortcut();
        if (success)
        {
            ShowFeedback("🖥️ Gamepad Center masaüstü kısayolu başarıyla oluşturuldu.");
        }
        else
        {
            ShowFeedback("⚠️ Masaüstü kısayolu oluşturulamadı.");
        }
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
