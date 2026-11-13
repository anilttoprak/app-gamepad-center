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

    [Fact]
    public async Task ControllerMetadataRepository_SaveAndRetrieveCustomName()
    {
        var metaRepo = new SqliteControllerMetadataRepository(_context);

        // Henüz kaydedilmemiş bir cihaz için null dönmeli
        var initial = await metaRepo.GetCustomNameAsync("XBOX_BLE_TEST_99");
        Assert.Null(initial);

        // Takma ad kaydet
        await metaRepo.SetCustomNameAsync("XBOX_BLE_TEST_99", "Siyah Xbox Kolum");

        var retrieved = await metaRepo.GetCustomNameAsync("XBOX_BLE_TEST_99");
        Assert.Equal("Siyah Xbox Kolum", retrieved);

        // Tüm takma adları getir
        var all = await metaRepo.GetAllCustomNamesAsync();
        Assert.True(all.ContainsKey("XBOX_BLE_TEST_99"));
        Assert.Equal("Siyah Xbox Kolum", all["XBOX_BLE_TEST_99"]);

        // Güncelle
        await metaRepo.SetCustomNameAsync("XBOX_BLE_TEST_99", "Oturma Odası Kolu");
        var updated = await metaRepo.GetCustomNameAsync("XBOX_BLE_TEST_99");
        Assert.Equal("Oturma Odası Kolu", updated);

        // Boş isim girildiğinde kaydı silmeli
        await metaRepo.SetCustomNameAsync("XBOX_BLE_TEST_99", "");
        var deleted = await metaRepo.GetCustomNameAsync("XBOX_BLE_TEST_99");
        Assert.Null(deleted);
    }

    [Fact]
    public void AnalyticsEngine_InitialSetupOnly_ReturnsZeroActualChanges()
    {
        var now = DateTimeOffset.UtcNow;
        var sessions = new List<ControllerSession>
        {
            new ControllerSession
            {
                Id = 1,
                DeviceKey = "XBOX_1",
                StartedAtUtc = now.AddMinutes(-30),
                EndedAtUtc = now.AddMinutes(-20),
                DurationSeconds = 600
            }
        };

        var changes = new List<BatteryChange>
        {
            new BatteryChange
            {
                Id = 1,
                DeviceKey = "XBOX_1",
                ChangedAtUtc = now.AddMinutes(-30),
                ChangeType = "İlk Kurulum (Fabrika Çıkışı)",
                NewPercentage = 100
            }
        };

        var report = AnalyticsEngine.ComputeAnalytics(sessions, changes, null, TimeZoneInfo.Local, now);

        // İlk kurulum bir değişim sayılmamalı
        Assert.Equal(0, report.TotalBatteryChangesCount);
        // Henüz tamamlanmış bir pil döngüsü olmadığı için ortalama süre yetersiz olmalı
        Assert.Null(report.EstimatedHoursPerBatterySet);
    }

    [Fact]
    public void BluetoothClassic_ExtractMacAddress_ValidatesFormats()
    {
        string pnpAepId = "Bluetooth#Bluetooth68:c6:ac:a2:e6:7a-8c:41:f2:27:e9:b0";
        string extracted1 = ControllerHub.Infrastructure.DeviceDetection.BluetoothClassicDeviceWatcher.ExtractMacAddress(pnpAepId);
        Assert.Equal("8C41F227E9B0", extracted1);

        string devId = @"BTHENUM\DEV_8C41F227E9B0\7&3B90B3AE&0&BLUETOOTHDEVICE_8C41F227E9B0";
        string extracted2 = ControllerHub.Infrastructure.DeviceDetection.BluetoothClassicDeviceWatcher.ExtractMacAddress(devId);
        Assert.Equal("8C41F227E9B0", extracted2);
    }

    [Fact]
    public void BluetoothClassic_DebounceTransitions_SessionStartedAndEnded()
    {
        var target = new ControllerHub.Infrastructure.DeviceDetection.TrackedClassicBtTarget(
            "Bluetooth#dev1",
            "8C41F227E9B0",
            "Wireless Controller");

        var now = DateTimeOffset.UtcNow;

        // 1. Başlangıçta Disconnected
        Assert.Equal(DeviceConnectionState.Disconnected, target.DeviceInfo.State);

        // 2. Ham sinyal geldi ama henüz 500ms dolmadı -> Connecting
        target.RawPresentSignal = true;
        var (state1, action1) = target.DebouncePolicy.Evaluate(target.RawPresentSignal, now);
        Assert.Equal(DeviceConnectionState.Connecting, state1);
        Assert.Equal(DebounceAction.None, action1);

        // 3. 500ms sonra -> Connected & SessionStarted
        var (state2, action2) = target.DebouncePolicy.Evaluate(target.RawPresentSignal, now.AddMilliseconds(501));
        Assert.Equal(DeviceConnectionState.Connected, state2);
        Assert.Equal(DebounceAction.SessionStarted, action2);

        // 4. Sinyal koptu -> 4 saniye grace period içinde Disconnecting
        target.RawPresentSignal = false;
        var (state3, action3) = target.DebouncePolicy.Evaluate(target.RawPresentSignal, now.AddSeconds(2));
        Assert.Equal(DeviceConnectionState.Disconnecting, state3);
        Assert.Equal(DebounceAction.None, action3);

        // 5. 4 saniye doldu -> Disconnected & SessionEnded
        var (state4, action4) = target.DebouncePolicy.Evaluate(target.RawPresentSignal, now.AddSeconds(6));
        Assert.Equal(DeviceConnectionState.Disconnected, state4);
        Assert.Equal(DebounceAction.SessionEnded, action4);
    }

    [Fact]
    public void CompositeDeviceWatcher_AggregatesBleAndClassicBtControllers()
    {
        var mockBle = new MockWatcherService();
        var mockClassic = new ControllerHub.Infrastructure.DeviceDetection.BluetoothClassicDeviceWatcher();
        var mockXinput = new ControllerHub.Infrastructure.DeviceDetection.XInputDeviceWatcher();

        using var composite = new ControllerHub.Infrastructure.DeviceDetection.CompositeDeviceWatcher(
            mockBle,
            mockClassic,
            mockXinput);

        // BLE cihazı bağla
        mockBle.SimulateDeviceConnected(new DeviceInfo
        {
            DeviceKey = "XBOX_BLE_112233",
            Name = "Xbox Wireless Controller",
            State = DeviceConnectionState.Connected,
            ConnectionType = ControllerConnectionType.Bluetooth
        });

        // Composite listesinde BLE cihazı bulunmalı
        var list = composite.ConnectedDevices;
        Assert.Single(list);
        Assert.Equal("XBOX_BLE_112233", list[0].DeviceKey);
    }

    [Fact]
    public async Task BluetoothClassic_LiveDiscovery_DiscoversConnectedWirelessController()
    {
        var watcher = new ControllerHub.Infrastructure.DeviceDetection.BluetoothClassicDeviceWatcher();
        await watcher.DiscoverTargetDevicesAsync();
        await watcher.CheckDeviceStatesAsync();

        // 500ms debounce için tick
        var now = DateTimeOffset.UtcNow;
        watcher.EvaluateDebounce(now);
        watcher.EvaluateDebounce(now.AddMilliseconds(600));

        Console.WriteLine($"Connected Classic Devices Count: {watcher.ConnectedDevices.Count}");
        foreach (var dev in watcher.ConnectedDevices)
        {
            Console.WriteLine($"LIVE CONNECTED: {dev.Name} [{dev.DeviceKey}] - State: {dev.State}");
        }

        // Eğer kullanıcı kolda bağlıysa doğrula
        if (watcher.ConnectedDevices.Count > 0)
        {
            var dev = watcher.ConnectedDevices[0];
            Assert.Contains("Wireless Controller", dev.Name);
            Assert.StartsWith("BT_", dev.DeviceKey);
            Assert.Equal(DeviceConnectionState.Connected, dev.State);
        }
    }
}
