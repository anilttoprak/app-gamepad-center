using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ControllerHub.Core.Interfaces;
using ControllerHub.Core.Models;
using ControllerHub.Core.Policies;
using ControllerHub.Core.Services;
using ControllerHub.Infrastructure.DeviceDetection;
using ControllerHub.Infrastructure.Persistence;
using ControllerHub.Infrastructure.Services;
using Xunit;

namespace ControllerHub.Tests;

public class MultiDeviceArchitectureTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly SqliteDatabaseContext _context;
    private readonly SqliteSessionRepository _sessionRepo;
    private readonly SqliteBatteryRepository _batteryRepo;

    public MultiDeviceArchitectureTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"controller_hub_multitest_{Guid.NewGuid():N}.db");
        _context = new SqliteDatabaseContext(_testDbPath);
        _sessionRepo = new SqliteSessionRepository(_context);
        _batteryRepo = new SqliteBatteryRepository(_context);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_testDbPath))
            {
                File.Delete(_testDbPath);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    private class MockWatcherService : IDeviceWatcherService
    {
        public bool IsRunning { get; private set; }
        public DeviceInfo CurrentDevice => _devices.Count > 0 ? _devices[0] : new DeviceInfo();
        public IReadOnlyList<DeviceInfo> ConnectedDevices => _devices;

        private readonly List<DeviceInfo> _devices = new();

        public event EventHandler<DeviceConnectionChangedEventArgs>? ConnectionChanged;
        public event EventHandler<int>? BatteryLevelUpdated
        {
            add { }
            remove { }
        }

        public Task StartAsync(System.Threading.CancellationToken cancellationToken = default)
        {
            IsRunning = true;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            IsRunning = false;
            return Task.CompletedTask;
        }

        public void Dispose() { }

        public void SimulateDeviceConnected(DeviceInfo dev)
        {
            _devices.Add(dev);
            ConnectionChanged?.Invoke(this, new DeviceConnectionChangedEventArgs(
                DeviceConnectionState.Connected,
                dev,
                DebounceAction.SessionStarted,
                DateTimeOffset.UtcNow));
        }

        public void SimulateDeviceDisconnected(DeviceInfo dev)
        {
            _devices.RemoveAll(d => d.DeviceKey == dev.DeviceKey);
            ConnectionChanged?.Invoke(this, new DeviceConnectionChangedEventArgs(
                DeviceConnectionState.Disconnected,
                dev,
                DebounceAction.SessionEnded,
                DateTimeOffset.UtcNow));
        }
    }

    [Fact]
    public async Task SessionTrackerService_SimultaneousControllers_TracksBothIndependently()
    {
        // 1. Arrange
        var mockWatcher = new MockWatcherService();
        using var tracker = new SessionTrackerService(mockWatcher, _sessionRepo, _batteryRepo);
        await tracker.InitializeAsync();

        var controller1 = new DeviceInfo
        {
            DeviceKey = "BLE:XBOX_PAD_1",
            Name = "Xbox Wireless Controller",
            State = DeviceConnectionState.Connected,
            ConnectionType = ControllerConnectionType.Bluetooth,
            PlayerIndex = 0
        };

        var controller2 = new DeviceInfo
        {
            DeviceKey = "XINPUT:1",
            Name = "USB Kablolu Oyun Kolu",
            State = DeviceConnectionState.Connected,
            ConnectionType = ControllerConnectionType.WiredUsb,
            PlayerIndex = 1
        };

        // 2. Act: Cihaz 1 bağlanır
        mockWatcher.SimulateDeviceConnected(controller1);
        await Task.Delay(50);

        Assert.Single(tracker.ActiveSessions);
        Assert.NotNull(tracker.ActiveSession);
        Assert.Equal("BLE:XBOX_PAD_1", tracker.ActiveSession.DeviceKey);

        // Act: Cihaz 2 bağlanır (2 kol aynı anda aktif!)
        mockWatcher.SimulateDeviceConnected(controller2);
        await Task.Delay(50);

        Assert.Equal(2, tracker.ActiveSessions.Count);
        Assert.Contains(tracker.ActiveSessions, s => s.DeviceKey == "BLE:XBOX_PAD_1");
        Assert.Contains(tracker.ActiveSessions, s => s.DeviceKey == "XINPUT:1");

        // Act: Cihaz 1 bağlantısı kesilir (Cihaz 2 devam etmeli)
        mockWatcher.SimulateDeviceDisconnected(controller1);
        await Task.Delay(50);

        Assert.Single(tracker.ActiveSessions);
        Assert.Equal("XINPUT:1", tracker.ActiveSessions[0].DeviceKey);
        Assert.Equal("XINPUT:1", tracker.ActiveSession?.DeviceKey);

        // Act: Cihaz 2 bağlantısı kesilir
        mockWatcher.SimulateDeviceDisconnected(controller2);
        await Task.Delay(50);

        Assert.Empty(tracker.ActiveSessions);
        Assert.Null(tracker.ActiveSession);

        // Veritabanında 2 oturumun da başarıyla kaydedildiğini doğrula
        var savedSessions = await _sessionRepo.GetRecentSessionsAsync(10);
        Assert.Equal(2, savedSessions.Count);
    }

    [Fact]
    public void SessionTimeCalculator_WithMultipleActiveSessions_CalculatesCombinedDurationCorrectly()
    {
        var now = DateTimeOffset.UtcNow;
        var completedSessions = new List<ControllerSession>
        {
            new ControllerSession
            {
                Id = 1,
                DeviceKey = "DEV1",
                StartedAtUtc = now.AddHours(-3),
                EndedAtUtc = now.AddHours(-2),
                DurationSeconds = 3600
            }
        };

        var active1 = new ControllerSession
        {
            Id = 2,
            DeviceKey = "DEV1",
            StartedAtUtc = now.AddMinutes(-30)
        };

        var active2 = new ControllerSession
        {
            Id = 3,
            DeviceKey = "DEV2",
            StartedAtUtc = now.AddMinutes(-15)
        };

        var activeList = new[] { active1, active2 };

        // Toplam Süre = 3600 sn (1 sa) + 1800 sn (30 dk) + 900 sn (15 dk) = 6300 sn (1 sa 45 dk)
        var total = SessionTimeCalculator.CalculateTotalDurationMulti(completedSessions, activeList, now);
        Assert.Equal(6300, (int)total.TotalSeconds);

        var formatted = SessionTimeCalculator.FormatDurationText(total);
        Assert.Equal("1 sa 45 dk", formatted);
    }

    [Fact]
    public void DeviceInfo_WiredAndBluetoothProperties_DisplayCorrectDescriptions()
    {
        var btDevice = new DeviceInfo
        {
            DeviceKey = "BLE:XBOX",
            Name = "Xbox Wireless Controller",
            State = DeviceConnectionState.Connected,
            ConnectionType = ControllerConnectionType.Bluetooth,
            PlayerIndex = 0
        };

        Assert.Equal(ControllerConnectionType.Bluetooth, btDevice.ConnectionType);
        Assert.Equal("Bluetooth", btDevice.ConnectionTypeDescription);

        var wiredDevice = new DeviceInfo
        {
            DeviceKey = "XINPUT:0",
            Name = "USB Kablolu Oyun Kolu",
            State = DeviceConnectionState.Connected,
            ConnectionType = ControllerConnectionType.WiredUsb,
            PlayerIndex = 0
        };

        Assert.Equal(ControllerConnectionType.WiredUsb, wiredDevice.ConnectionType);
        Assert.Equal("USB Kablolu", wiredDevice.ConnectionTypeDescription);
    }

    [Theory]
    [InlineData("Xbox Wireless Controller", true)]
    [InlineData("Wireless Controller", true)] // DualShock / DualSense generic name on Windows
    [InlineData("DualSense Wireless Controller", true)]
    [InlineData("DualShock 4 USB Wireless Adaptor", true)]
    [InlineData("Pro Controller", true)] // Nintendo Switch Pro
    [InlineData("8BitDo Ultimate Wireless Controller", true)]
    [InlineData("Bluetooth Gamepad", true)]
    [InlineData("Logitech MX Master 3", false)]
    [InlineData("AirPods Pro", false)]
    public void WindowsBluetoothDeviceWatcher_SupportedNames_MatchesExpectedControllers(string deviceName, bool expectedSupported)
    {
        bool isSupported = WindowsBluetoothDeviceWatcher.IsSupportedControllerName(deviceName);
        Assert.Equal(expectedSupported, isSupported);
    }
}
