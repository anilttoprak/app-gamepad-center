using System;
using System.IO;
using System.Threading.Tasks;
using ControllerHub.App.ViewModels;
using ControllerHub.Core.Interfaces;
using ControllerHub.Core.Models;
using ControllerHub.Core.Policies;
using ControllerHub.Infrastructure.Persistence;
using ControllerHub.Infrastructure.Services;
using Xunit;

namespace ControllerHub.Tests;

public class SettingsAndLifecycleTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly SqliteDatabaseContext _context;
    private readonly SqliteSettingsRepository _settingsRepo;
    private readonly SqliteSessionRepository _sessionRepo;
    private readonly SqliteBatteryRepository _batteryRepo;

    public SettingsAndLifecycleTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"settings_test_{Guid.NewGuid():N}.db");
        _context = new SqliteDatabaseContext(_tempDbPath);
        _settingsRepo = new SqliteSettingsRepository(_context);
        _sessionRepo = new SqliteSessionRepository(_context);
        _batteryRepo = new SqliteBatteryRepository(_context);
    }

    [Fact]
    public async Task SettingsRepository_ShouldReturnDefaults_WhenTableIsEmpty()
    {
        var settings = await _settingsRepo.LoadSettingsAsync();

        Assert.NotNull(settings);
        Assert.True(settings.StartWithWindows);
        Assert.True(settings.MinimizeToTray);
        Assert.True(settings.NotificationsEnabled);
        Assert.Equal("Light", settings.Theme);
    }

    [Fact]
    public async Task SettingsRepository_ShouldSaveAndReloadSettings()
    {
        var modified = new AppSettings
        {
            StartWithWindows = false,
            MinimizeToTray = false,
            NotificationsEnabled = false,
            SelectedDeviceKey = "XBOX_CUSTOM_KEY",
            Theme = "Light"
        };

        await _settingsRepo.SaveSettingsAsync(modified);

        // Yeni bir repository örneğiyle veritabanından oku
        var reloadedRepo = new SqliteSettingsRepository(_context);
        var loaded = await reloadedRepo.LoadSettingsAsync();

        Assert.False(loaded.StartWithWindows);
        Assert.False(loaded.MinimizeToTray);
        Assert.False(loaded.NotificationsEnabled);
        Assert.Equal("XBOX_CUSTOM_KEY", loaded.SelectedDeviceKey);
        Assert.Equal("Light", loaded.Theme);
    }

    [Fact]
    public async Task SessionTracker_CleanAppExit_ShouldEndActiveSessionWithAppExitReason()
    {
        var dummyWatcher = new MockDeviceWatcher();
        var tracker = new SessionTrackerService(dummyWatcher, _sessionRepo, _batteryRepo);

        await tracker.InitializeAsync();

        // 1. Oturum başlat sinyali simüle et
        dummyWatcher.RaiseConnectionChanged(new DeviceConnectionChangedEventArgs(
            DeviceConnectionState.Connected,
            dummyWatcher.CurrentDevice,
            DebounceAction.SessionStarted,
            DateTimeOffset.UtcNow.AddMinutes(-10)));

        Assert.NotNull(tracker.ActiveSession);
        Assert.True(tracker.ActiveSession.IsActive);

        // 2. Uygulama temiz kapatılıyor (Kabul Kriteri: AppExit ile düzgün sonlandırma)
        await tracker.ShutdownAsync();

        Assert.Null(tracker.ActiveSession);

        // 3. Veritabanındaki oturumun AppExit ile kapatıldığını doğrula
        var sessions = await _sessionRepo.GetRecentSessionsAsync(10);
        Assert.Single(sessions);
        var closedSession = sessions[0];
        Assert.False(closedSession.IsActive);
        Assert.Equal(SessionEndReason.AppExit, closedSession.EndReason);
        Assert.True(closedSession.DurationSeconds >= 0);
    }

    [Fact]
    public async Task SettingsViewModel_ShouldPersistChangesToRepository()
    {
        var vm = new SettingsViewModel();
        vm.Initialize(_settingsRepo, new MockStartupService(), _context.DatabasePath);

        // Ayar yüklenmesini bekle
        await vm.LoadSettingsAsync();
        Assert.True(vm.MinimizeToTray);

        // Kullanıcı tepsiye küçültmeyi kapatır
        vm.MinimizeToTray = false;

        // Veritabanını kontrol et
        var saved = await _settingsRepo.LoadSettingsAsync();
        Assert.False(saved.MinimizeToTray);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_tempDbPath))
            {
                File.Delete(_tempDbPath);
            }
        }
        catch { }
    }

    private class MockStartupService : WindowsStartupService
    {
        private bool _enabled = false;
        public override bool IsStartupEnabled() => _enabled;
        public override void SetStartup(bool enable) => _enabled = enable;
    }

    private class MockDeviceWatcher : IDeviceWatcherService
    {
        public event EventHandler<DeviceConnectionChangedEventArgs>? ConnectionChanged;
        public event EventHandler<int>? BatteryLevelUpdated { add { } remove { } }

        public DeviceInfo CurrentDevice { get; } = new()
        {
            DeviceKey = "XBOX_BLE_LIFECYCLE_TEST",
            Name = "Xbox Wireless Controller",
            State = DeviceConnectionState.Disconnected
        };

        public bool IsRunning => true;
        public Task StartAsync(System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public void Dispose() { }

        public void RaiseConnectionChanged(DeviceConnectionChangedEventArgs args)
        {
            ConnectionChanged?.Invoke(this, args);
        }
    }
}
