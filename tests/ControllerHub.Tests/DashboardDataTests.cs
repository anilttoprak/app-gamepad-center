using System;
using System.IO;
using System.Threading.Tasks;
using ControllerHub.App.ViewModels;
using ControllerHub.Core.Interfaces;
using ControllerHub.Core.Models;
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

    private class DummyDeviceWatcher : IDeviceWatcherService
    {
        public event EventHandler<DeviceConnectionChangedEventArgs>? ConnectionChanged { add { } remove { } }
        public event EventHandler<int>? BatteryLevelUpdated { add { } remove { } }

        public DeviceInfo CurrentDevice { get; } = new() { DeviceKey = "DUMMY", Name = "Dummy" };
        public bool IsRunning => false;

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
