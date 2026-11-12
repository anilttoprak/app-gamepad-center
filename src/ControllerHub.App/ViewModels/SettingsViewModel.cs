using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControllerHub.Core.Interfaces;
using ControllerHub.Core.Models;
using ControllerHub.Infrastructure.Services;

namespace ControllerHub.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private ISettingsRepository? _settingsRepository;
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
    private string _trackedControllerName = "Microsoft Xbox Wireless Controller (Model 1914)";

    public void Initialize(
        ISettingsRepository settingsRepository,
        WindowsStartupService startupService,
        string databasePath)
    {
        _settingsRepository = settingsRepository;
        _startupService = startupService;
        DatabasePath = databasePath;

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
        }
        finally
        {
            _isInitializing = false;
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
