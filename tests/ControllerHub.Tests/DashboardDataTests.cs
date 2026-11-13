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

public class DashboardDataTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly SqliteDatabaseContext _context;
    private readonly SqliteSessionRepository _sessionRepo;
    private readonly SqliteBatteryRepository _batteryRepo;

    public DashboardDataTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"dashboard_test_{Guid.NewGuid():N}.db");
        _context = new SqliteDatabaseContext(_tempDbPath);
        _sessionRepo = new SqliteSessionRepository(_context);
        _batteryRepo = new SqliteBatteryRepository(_context);
    }

    [Fact]
    public async Task Dashboard_ShouldShowEmptyState_WhenNoSessionsExist()
    {
        var vm = new DashboardViewModel();
        // Null watcher / dummy tracker for unit test
        var dummyWatcher = new DummyDeviceWatcher();
        var tracker = new SessionTrackerService(dummyWatcher, _sessionRepo, _batteryRepo);

        vm.Initialize(_sessionRepo, _batteryRepo, tracker);
        await vm.RefreshDataAsync();

        Assert.Equal("0 dk", vm.TotalConnectionTime);
        Assert.Equal("0 dk", vm.TodayConnectionTime);
        Assert.Equal("0", vm.TotalSessionsCount);
        Assert.False(vm.HasSessions);
        Assert.True(vm.IsEmptyState);
        Assert.Empty(vm.RecentSessions);
    }

    [Fact]
    public async Task Dashboard_MetricsMustBeConsistentWithSessionHistory()
    {
        // 2 adet tamamlanmış oturum ekle:
        // Oturum 1: 1 saat önce başladı, 30 dk sürdü (1800 sn)
        var s1Start = DateTimeOffset.UtcNow.AddHours(-3);
        var s1 = await _sessionRepo.StartSessionAsync("XBOX_TEST", s1Start);
        await _sessionRepo.EndSessionAsync("XBOX_TEST", s1Start.AddMinutes(30), SessionEndReason.Disconnected);

        // Oturum 2: 1 saat önce başladı, 45 dk sürdü (2700 sn)
        var s2Start = DateTimeOffset.UtcNow.AddHours(-1);
        var s2 = await _sessionRepo.StartSessionAsync("XBOX_TEST", s2Start);
        await _sessionRepo.EndSessionAsync("XBOX_TEST", s2Start.AddMinutes(45), SessionEndReason.Disconnected);

        var vm = new DashboardViewModel();
        var dummyWatcher = new DummyDeviceWatcher();
        var tracker = new SessionTrackerService(dummyWatcher, _sessionRepo, _batteryRepo);

        vm.Initialize(_sessionRepo, _batteryRepo, tracker);
        await vm.RefreshDataAsync();

        // 30 dk + 45 dk = 75 dk = 1 sa 15 dk
        Assert.Equal("1 sa 15 dk", vm.TotalConnectionTime);
        Assert.Equal("2", vm.TotalSessionsCount);
        Assert.True(vm.HasSessions);
        Assert.False(vm.IsEmptyState);
        Assert.Equal(2, vm.RecentSessions.Count);

        // Haftalık 7 gün toplamı da tam olarak 1.25 saate eşit olmalı
        double weeklyHoursSum = 0;
        foreach (var bar in vm.WeeklyActivity)
        {
            weeklyHoursSum += bar.Hours;
        }
        Assert.Equal(1.25, Math.Round(weeklyHoursSum, 2));
    }

    [Fact]
    public async Task Dashboard_ActiveSession_ShouldNotDuplicateAsGhostSessionOrInflateTotalCount()
    {
        // 2 adet tamamlanmış oturum ekle
        var s1Start = DateTimeOffset.UtcNow.AddHours(-3);
        await _sessionRepo.StartSessionAsync("XBOX_TEST", s1Start);
        await _sessionRepo.EndSessionAsync("XBOX_TEST", s1Start.AddMinutes(30), SessionEndReason.Disconnected);

        var s2Start = DateTimeOffset.UtcNow.AddHours(-2);
        await _sessionRepo.StartSessionAsync("XBOX_TEST", s2Start);
        await _sessionRepo.EndSessionAsync("XBOX_TEST", s2Start.AddMinutes(45), SessionEndReason.Disconnected);

        var dummyWatcher = new DummyDeviceWatcher();
        var tracker = new SessionTrackerService(dummyWatcher, _sessionRepo, _batteryRepo);
        await tracker.InitializeAsync();

        // 1 adet aktif oturum başlat (Veritabanına EndedAtUtc = null olarak yazılır ve tracker'da aktiftir)
        dummyWatcher.TriggerConnection(new DeviceConnectionChangedEventArgs(
            DeviceConnectionState.Connected,
            new DeviceInfo { DeviceKey = "XBOX_TEST", Name = "Xbox Controller" },
            DebounceAction.SessionStarted,
            DateTimeOffset.UtcNow));

        // SQLite insert ve event akışının tamamlanması için kısa bekleme
        await Task.Delay(100);

        var dashboardVm = new DashboardViewModel();
        dashboardVm.Initialize(_sessionRepo, _batteryRepo, tracker);
        await dashboardVm.RefreshDataAsync();

        // Toplam oturum sayısı 2 tamamlanmış + 1 aktif = 3 olmalı (Asla 4 olmamalı)
        Assert.Equal("3", dashboardVm.TotalSessionsCount);

        // Son oturumlar tablosunda tam 3 satır olmalı (1 aktif + 2 tamamlanmış)
        Assert.Equal(3, dashboardVm.RecentSessions.Count);

        // İlk satır aktif oturum olmalı
        Assert.True(dashboardVm.RecentSessions[0].IsActive);
        Assert.Equal("Aktif Oturum", dashboardVm.RecentSessions[0].Reason);

        // İkinci ve üçüncü satırlar tamamlanmış oturumlar olmalı
        Assert.False(dashboardVm.RecentSessions[1].IsActive);
        Assert.False(dashboardVm.RecentSessions[2].IsActive);

        // "Bilinmiyor" şeklinde hayalet / çift oturum satırı OLMAMALI
        Assert.DoesNotContain(dashboardVm.RecentSessions, s => s.Reason == "Bilinmiyor");
        Assert.DoesNotContain(dashboardVm.RecentSessions, s => s.TimeRange.EndsWith("Bilinmiyor"));

        // SessionsViewModel için de kontrol et
        var sessionsVm = new SessionsViewModel();
        sessionsVm.Initialize(_sessionRepo, tracker);
        await sessionsVm.RefreshDataAsync();

        Assert.Equal(3, sessionsVm.Sessions.Count);
        Assert.Equal("Toplam 3 kayıt bulundu", sessionsVm.TotalRecordedSessionsText);
        Assert.DoesNotContain(sessionsVm.Sessions, s => s.EndTime == "Bilinmiyor");
        Assert.DoesNotContain(sessionsVm.Sessions, s => s.EndReason == "Bilinmiyor");
    }

    [Fact]
    public void Dashboard_QuickActionCommands_ShouldTriggerNavigationAndHandleEmptyExport()
    {
        var vm = new DashboardViewModel();
        string? navigatedTarget = null;
        vm.NavigationRequested += (_, target) => navigatedTarget = target;

        // 1. Tüm Oturumları Gör
        vm.ViewAllSessionsCommand.Execute(null);
        Assert.Equal("Sessions", navigatedTarget);

        // 2. Pil Değiştirdim
        vm.NavigateBatteryCommand.Execute(null);
        Assert.Equal("Battery", navigatedTarget);

        // 3. Oturum yokken CSV Dışa Aktar basıldığında uyarı geri bildirimi vermeli
        vm.ExportCsvCommand.Execute(null);
        Assert.True(vm.HasFeedback);
        Assert.Contains("bulunamadı", vm.StatusFeedback);
    }

    [Fact]
    public async Task Dashboard_WeeklyActivity_ShouldGenerateLineAndAreaPoints_ForSevenDays()
    {
        var s1Start = DateTimeOffset.UtcNow.AddHours(-2);
        await _sessionRepo.StartSessionAsync("XBOX_TEST", s1Start);
        await _sessionRepo.EndSessionAsync("XBOX_TEST", s1Start.AddMinutes(40), SessionEndReason.Disconnected);

        var vm = new DashboardViewModel();
        var dummyWatcher = new DummyDeviceWatcher();
        var tracker = new SessionTrackerService(dummyWatcher, _sessionRepo, _batteryRepo);

        vm.Initialize(_sessionRepo, _batteryRepo, tracker);
        await vm.RefreshDataAsync();

        // 7 gün noktası oluşturulmalı
        Assert.Equal(7, vm.WeeklyActivity.Count);
        Assert.Equal(7, vm.ActivityLinePoints.Count);
        // Alan grafiği poligonu: sol alt taban + 7 nokta + sağ alt taban = 9 nokta
        Assert.Equal(9, vm.ActivityAreaPoints.Count);

        var todayItem = vm.WeeklyActivity[6];
        Assert.Equal("Bugün", todayItem.DayName);
        Assert.Equal("#2563EB", todayItem.DayBrushHex);
        Assert.Equal("Bold", todayItem.DayFontWeight);
        Assert.True(todayItem.PointX > 0);
        Assert.True(todayItem.PointY > 0);
    }

    [Fact]
    public void Dashboard_ShouldDefaultToAnilXbox_WhenAvailableInOptions()
    {
        var vm = new DashboardViewModel();
        var options = new List<ControllerFilterOption>
        {
            new("ALL", "🔘 Tüm Kontrolcüler (Genel Özet)"),
            new("XBOX_BLE_686CE6985F0F", "🎮 Yusuf Xbox"),
            new("XBOX_BLE_78862EE89F4C", "🎮 Anıl Xbox")
        };

        vm.UpdateFilterOptions(options, "XBOX_BLE_78862EE89F4C");

        Assert.NotNull(vm.SelectedControllerFilter);
        Assert.Equal("XBOX_BLE_78862EE89F4C", vm.SelectedControllerFilter.DeviceKey);
        Assert.Contains("Anıl Xbox", vm.SelectedControllerFilter.DisplayName);
    }

    private class DummyDeviceWatcher : IDeviceWatcherService
    {
        public event EventHandler<DeviceConnectionChangedEventArgs>? ConnectionChanged;
        public event EventHandler<int>? BatteryLevelUpdated { add { } remove { } }

        public DeviceInfo CurrentDevice { get; } = new() { DeviceKey = "XBOX_TEST", Name = "Xbox Controller" };
        public IReadOnlyList<DeviceInfo> ConnectedDevices => new[] { CurrentDevice };
        public bool IsRunning => false;

        public void TriggerConnection(DeviceConnectionChangedEventArgs args)
        {
            ConnectionChanged?.Invoke(this, args);
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public void Dispose() { }
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
}
