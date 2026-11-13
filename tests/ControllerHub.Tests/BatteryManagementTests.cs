using System;
using System.IO;
using System.Threading.Tasks;
using ControllerHub.Core.Interfaces;
using ControllerHub.Core.Models;
using ControllerHub.Infrastructure.Export;
using ControllerHub.Infrastructure.Persistence;
using Xunit;

namespace ControllerHub.Tests;

public class BatteryManagementTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly SqliteDatabaseContext _context;
    private readonly SqliteBatteryRepository _repo;

    public BatteryManagementTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"battery_test_{Guid.NewGuid():N}.db");
        _context = new SqliteDatabaseContext(_tempDbPath);
        _repo = new SqliteBatteryRepository(_context);
    }

    [Fact]
    public async Task BatteryChange_ShouldPersistAndRetrieveCorrectly()
    {
        var change = new BatteryChange
        {
            DeviceKey = "XBOX_TEST",
            ChangedAtUtc = DateTimeOffset.UtcNow,
            PreviousPercentage = 12,
            NewPercentage = 100,
            Note = "Eneloop Pro 2500mAh",
            ChangeType = "Manuel"
        };

        await _repo.RecordBatteryChangeAsync(change);

        var list = await _repo.GetBatteryChangesAsync(10);
        Assert.Single(list);

        var saved = list[0];
        Assert.Equal("XBOX_TEST", saved.DeviceKey);
        Assert.Equal(12, saved.PreviousPercentage);
        Assert.Equal(100, saved.NewPercentage);
        Assert.Equal("Eneloop Pro 2500mAh", saved.Note);
        Assert.Equal("Manuel", saved.ChangeType);
    }

    [Fact]
    public async Task BatteryChange_Delete_ShouldRemoveRecordFromDatabase()
    {
        var change = new BatteryChange
        {
            DeviceKey = "XBOX_TEST",
            ChangedAtUtc = DateTimeOffset.UtcNow,
            PreviousPercentage = 50,
            NewPercentage = 100,
            Note = "Yanlış Kayıt",
            ChangeType = "Manuel"
        };

        await _repo.RecordBatteryChangeAsync(change);
        var list = await _repo.GetBatteryChangesAsync(10);
        Assert.Single(list);

        var success = await _repo.DeleteBatteryChangeAsync(list[0].Id);
        Assert.True(success);

        var afterDelete = await _repo.GetBatteryChangesAsync(10);
        Assert.Empty(afterDelete);
    }

    [Fact]
    public void BatteryCycleCalculator_ShouldCalculateAccurateCycleUsageAndSessions()
    {
        var nowUtc = new DateTimeOffset(2026, 11, 13, 15, 0, 0, TimeSpan.Zero);

        // 2 adet pil değişimi:
        // C1: 3 gün önce takıldı (Duracell Ultra)
        var c1 = new BatteryChange
        {
            Id = 1,
            DeviceKey = "XBOX_TEST",
            ChangedAtUtc = nowUtc.AddDays(-3),
            Note = "Duracell Ultra AA",
            NewPercentage = 100
        };

        // C2: 1 gün önce takıldı (Eneloop Pro - Aktif set)
        var c2 = new BatteryChange
        {
            Id = 2,
            DeviceKey = "XBOX_TEST",
            ChangedAtUtc = nowUtc.AddDays(-1),
            Note = "Eneloop Pro",
            NewPercentage = 100
        };

        // Oturumlar:
        // C1 döngüsünde 2 oturum: 2 saat + 1 saat = 3 saat
        var s1 = new ControllerSession
        {
            Id = 1,
            DeviceKey = "XBOX_TEST",
            StartedAtUtc = nowUtc.AddDays(-2).AddHours(-4),
            EndedAtUtc = nowUtc.AddDays(-2).AddHours(-2),
            DurationSeconds = 7200
        };
        var s2 = new ControllerSession
        {
            Id = 2,
            DeviceKey = "XBOX_TEST",
            StartedAtUtc = nowUtc.AddDays(-2),
            EndedAtUtc = nowUtc.AddDays(-2).AddHours(1),
            DurationSeconds = 3600
        };

        // C2 döngüsünde 1 oturum: 45 dk (2700 sn)
        var s3 = new ControllerSession
        {
            Id = 3,
            DeviceKey = "XBOX_TEST",
            StartedAtUtc = nowUtc.AddHours(-2),
            EndedAtUtc = nowUtc.AddHours(-1).AddMinutes(-15),
            DurationSeconds = 2700
        };

        var sessions = new List<ControllerSession> { s1, s2, s3 };
        var changes = new List<BatteryChange> { c1, c2 };

        var perf = ControllerHub.Core.Services.BatteryCycleCalculator.ComputePerformance(sessions, changes, null, nowUtc);

        Assert.Equal(2, perf.Cycles.Count);

        // C1 döngüsü (Tamamlanmış)
        var cycle1 = perf.Cycles.First(c => c.ChangeId == 1);
        Assert.False(cycle1.IsActiveCycle);
        Assert.Equal(TimeSpan.FromHours(3), cycle1.TotalUsageDuration);
        Assert.Equal(2, cycle1.SessionCount);

        // C2 döngüsü (Aktif)
        var cycle2 = perf.Cycles.First(c => c.ChangeId == 2);
        Assert.True(cycle2.IsActiveCycle);
        Assert.Equal(TimeSpan.FromMinutes(45), cycle2.TotalUsageDuration);
        Assert.Equal(1, cycle2.SessionCount);

        // Ortalama tamamlanan pil ömrü = 3 saat
        Assert.Equal(TimeSpan.FromHours(3), perf.AverageDurationPerCycle);

        // En iyi pil seti = Duracell Ultra (3 saat)
        Assert.NotNull(perf.BestCycle);
        Assert.Equal(1, perf.BestCycle.ChangeId);
    }

    [Fact]
    public async Task BatteryChanges_CsvExport_ShouldIncludeUtf8BomAndCorrectFormatting()
    {
        var changes = new[]
        {
            new BatteryChange
            {
                Id = 1,
                DeviceKey = "XBOX_TEST",
                ChangedAtUtc = new DateTimeOffset(2026, 11, 12, 14, 0, 0, TimeSpan.Zero),
                PreviousPercentage = 5,
                NewPercentage = 100,
                Note = "Duracell; \"Özel Seri\"",
                ChangeType = "Manuel"
            }
        };

        var csv = CsvExportService.GenerateBatteryChangesCsv(changes);

        // Başlık ve değer kontrolleri
        Assert.Contains("ID;Cihaz;Değişim Tarihi (Yerel);Eski Yüzde (%);Yeni Yüzde (%);Pil Modeli / Not;Kayıt Türü", csv);
        Assert.Contains("\"Duracell; \"\"Özel Seri\"\"\"", csv); // Kaçış karakteri testi

        var tempCsvFile = Path.Combine(Path.GetTempPath(), $"test_battery_{Guid.NewGuid():N}.csv");
        try
        {
            await CsvExportService.SaveCsvFileAsync(tempCsvFile, csv);

            // UTF-8 BOM varlığı testi (0xEF, 0xBB, 0xBF)
            var bytes = await File.ReadAllBytesAsync(tempCsvFile);
            Assert.True(bytes.Length >= 3);
            Assert.Equal(0xEF, bytes[0]);
            Assert.Equal(0xBB, bytes[1]);
            Assert.Equal(0xBF, bytes[2]);
        }
        finally
        {
            if (File.Exists(tempCsvFile)) File.Delete(tempCsvFile);
        }
    }

    [Fact]
    public async Task BatteryViewModel_FirstBatterySetup_ShouldBackdateToFirstSessionAndSetSpecialType()
    {
        var sessionRepo = new SqliteSessionRepository(_context);
        var firstSessionStart = DateTimeOffset.UtcNow.AddHours(-5);
        await sessionRepo.StartSessionAsync("XBOX_TEST", firstSessionStart);
        await sessionRepo.EndSessionAsync("XBOX_TEST", firstSessionStart.AddHours(2), SessionEndReason.Disconnected);

        var vm = new ControllerHub.App.ViewModels.BatteryViewModel();
        vm.Initialize(_repo, new DummyDeviceWatcher(), sessionRepo);
        await vm.RefreshDataAsync();

        Assert.True(vm.IsFirstBatterySetup);
        Assert.Contains("İlk Pil", vm.BatteryFormTitle);
        Assert.Contains("İlk Pil", vm.BatterySaveButtonText);

        vm.BatteryNote = "Kutu İçi Fabrika Pili";
        await vm.SaveBatteryChangeCommand.ExecuteAsync(null);

        var changes = await _repo.GetBatteryChangesAsync(10);
        Assert.Single(changes);
        var initial = changes[0];
        Assert.Equal("İlk Kurulum (Fabrika Çıkışı)", initial.ChangeType);
        Assert.Null(initial.PreviousPercentage);
        Assert.Equal(firstSessionStart.ToUniversalTime(), initial.ChangedAtUtc.ToUniversalTime());

        Assert.False(vm.IsFirstBatterySetup);
        Assert.Contains("Yeni Pil", vm.BatteryFormTitle);
    }

    private class DummyDeviceWatcher : IDeviceWatcherService
    {
        public event EventHandler<DeviceConnectionChangedEventArgs>? ConnectionChanged { add { } remove { } }
        public event EventHandler<int>? BatteryLevelUpdated { add { } remove { } }

        public DeviceInfo CurrentDevice { get; } = new() { DeviceKey = "XBOX_TEST", Name = "Xbox Controller" };
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
