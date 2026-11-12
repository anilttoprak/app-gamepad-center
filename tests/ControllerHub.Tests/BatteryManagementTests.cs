using System;
using System.IO;
using System.Threading.Tasks;
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
