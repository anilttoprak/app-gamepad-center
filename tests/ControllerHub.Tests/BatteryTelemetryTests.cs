using System;
using System.IO;
using System.Threading.Tasks;
using ControllerHub.App.ViewModels;
using ControllerHub.Core.Interfaces;
using ControllerHub.Core.Models;
using ControllerHub.Core.Services;
using ControllerHub.Infrastructure.Persistence;
using Xunit;

namespace ControllerHub.Tests;

public class BatteryTelemetryTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly SqliteDatabaseContext _context;
    private readonly SqliteBatteryRepository _repo;

    public BatteryTelemetryTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"telemetry_test_{Guid.NewGuid():N}.db");
        _context = new SqliteDatabaseContext(_tempDbPath);
        _repo = new SqliteBatteryRepository(_context);
    }

    [Fact]
    public void BatteryChangeDetector_ShouldGenerateProposal_WhenBatteryJumpsFromLowToFull()
    {
        var detector = new BatteryChangeDetector(minJumpThreshold: 40, minTargetPercentage: 75);
        var now = DateTimeOffset.UtcNow;

        var proposal = detector.Evaluate(
            deviceKey: "XBOX_BLE_1914",
            previousPercentage: 15,
            newPercentage: 100,
            isDeviceConnected: true,
            nowUtc: now);

        Assert.NotNull(proposal);
        Assert.Equal("XBOX_BLE_1914", proposal.DeviceKey);
        Assert.Equal(15, proposal.PreviousPercentage);
        Assert.Equal(100, proposal.NewPercentage);
        Assert.Contains("%15", proposal.ReasonMessage);
        Assert.Contains("%100", proposal.ReasonMessage);
    }

    [Fact]
    public void BatteryChangeDetector_ShouldReject_WhenVoltageFluctuatesSlightly()
    {
        var detector = new BatteryChangeDetector(minJumpThreshold: 40, minTargetPercentage: 75);
        var now = DateTimeOffset.UtcNow;

        // Dinlenme voltajı toparlanması (%15 -> %20 gibi) pil değişimi olarak önerilmemelidir
        var proposal = detector.Evaluate(
            deviceKey: "XBOX_BLE_1914",
            previousPercentage: 15,
            newPercentage: 20,
            isDeviceConnected: true,
            nowUtc: now);

        Assert.Null(proposal);
    }

    [Fact]
    public void BatteryChangeDetector_ShouldReject_WhenBatteryDecreasesOrRemainsConstant()
    {
        var detector = new BatteryChangeDetector(minJumpThreshold: 40, minTargetPercentage: 75);
        var now = DateTimeOffset.UtcNow;

        var proposalDrain = detector.Evaluate("XBOX_BLE_1914", 80, 70, true, now);
        var proposalSame = detector.Evaluate("XBOX_BLE_1914", 90, 90, true, now);

        Assert.Null(proposalDrain);
        Assert.Null(proposalSame);
    }

    [Fact]
    public void BatteryChangeDetector_ShouldReject_WhenNoPreviousReadingExists()
    {
        var detector = new BatteryChangeDetector(minJumpThreshold: 40, minTargetPercentage: 75);
        var now = DateTimeOffset.UtcNow;

        // İlk açılışta %100 okunması yeni bir değişim değil, cihazın ilk kalibrasyonudur
        var proposal = detector.Evaluate("XBOX_BLE_1914", null, 100, true, now);

        Assert.Null(proposal);
    }

    [Fact]
    public void BatteryChangeDetector_ShouldReject_WhenDeviceIsDisconnected()
    {
        var detector = new BatteryChangeDetector(minJumpThreshold: 40, minTargetPercentage: 75);
        var now = DateTimeOffset.UtcNow;

        // Bağlantı yokken gelen değerler asla öneri tetiklememelidir
        var proposal = detector.Evaluate("XBOX_BLE_1914", 10, 100, false, now);

        Assert.Null(proposal);
    }

    [Fact]
    public void BatteryChangeDetector_ShouldReject_WhenTargetPercentageIsTooLow()
    {
        var detector = new BatteryChangeDetector(minJumpThreshold: 40, minTargetPercentage: 75);
        var now = DateTimeOffset.UtcNow;

        // %5'ten %48'e çıkış (+%43) olsa bile yeni tam dolu pil eşiği (%75) aşılmamıştır
        var proposal = detector.Evaluate("XBOX_BLE_1914", 5, 48, true, now);

        Assert.Null(proposal);
    }

    [Fact]
    public async Task BatteryViewModel_AcceptProposal_ShouldCommitConfirmedChangeToDatabase()
    {
        var vm = new BatteryViewModel();
        vm.Initialize(_repo, new DummyDeviceWatcher());

        // Önce düşük pil seviyesi ile simüle et
        vm.UpdateCurrentPercentage(12, DateTimeOffset.UtcNow.AddMinutes(-10), BatterySource.WindowsApiGatt, BatteryConfidence.Exact, true);

        // Sonra taze %100 pil takıldığında öneri üretilmesini sağla
        vm.UpdateCurrentPercentage(100, DateTimeOffset.UtcNow, BatterySource.WindowsApiGatt, BatteryConfidence.Exact, true);

        Assert.True(vm.HasPendingProposal);
        Assert.NotNull(vm.ActiveProposal);

        // Kullanıcı "Evet, Kaydet" butonuna basar
        await vm.AcceptProposalAsync();

        Assert.False(vm.HasPendingProposal);
        Assert.Null(vm.ActiveProposal);

        // Veritabanında kesin onaylı kaydın oluştuğunu doğrula
        var changes = await _repo.GetBatteryChangesAsync(10);
        Assert.Single(changes);
        Assert.Equal(12, changes[0].PreviousPercentage);
        Assert.Equal(100, changes[0].NewPercentage);
        Assert.Equal("Otomatik (Onaylandı)", changes[0].ChangeType);
    }

    [Fact]
    public async Task BatteryViewModel_DismissProposal_ShouldNotTouchDatabase()
    {
        var vm = new BatteryViewModel();
        vm.Initialize(_repo, new DummyDeviceWatcher());

        vm.UpdateCurrentPercentage(10, DateTimeOffset.UtcNow.AddMinutes(-5), BatterySource.WindowsApiGatt, BatteryConfidence.Exact, true);
        vm.UpdateCurrentPercentage(100, DateTimeOffset.UtcNow, BatterySource.WindowsApiGatt, BatteryConfidence.Exact, true);

        Assert.True(vm.HasPendingProposal);

        // Kullanıcı "Yoksay" butonuna basar
        vm.DismissProposal();

        Assert.False(vm.HasPendingProposal);
        Assert.Null(vm.ActiveProposal);

        // Veritabanı kesinlikle temiz kalmalıdır (Kabul Kriteri: doğrulanmayan/onaylanmayan veri yazılmaz)
        var changes = await _repo.GetBatteryChangesAsync(10);
        Assert.Empty(changes);
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

    private class DummyDeviceWatcher : IDeviceWatcherService
    {
        public event EventHandler<DeviceConnectionChangedEventArgs>? ConnectionChanged { add { } remove { } }
        public event EventHandler<int>? BatteryLevelUpdated { add { } remove { } }

        public DeviceInfo CurrentDevice { get; } = new()
        {
            DeviceKey = "XBOX_BLE_DUMMY",
            Name = "Xbox Wireless Controller",
            State = DeviceConnectionState.Connected
        };

        public IReadOnlyList<DeviceInfo> ConnectedDevices => new[] { CurrentDevice };

        public bool IsRunning => true;
        public Task StartAsync(System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public void Dispose() { }
    }
}
