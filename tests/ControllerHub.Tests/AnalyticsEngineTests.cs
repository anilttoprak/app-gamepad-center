using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ControllerHub.Core.Models;
using ControllerHub.Core.Services;
using ControllerHub.Infrastructure.Export;
using Xunit;

namespace ControllerHub.Tests;

public class AnalyticsEngineTests
{
    [Fact]
    public void ComputeAnalytics_ShouldCalculateAccurateStats()
    {
        var localTz = TimeZoneInfo.CreateCustomTimeZone("CustomLocal", TimeSpan.FromHours(3), "CustomLocal", "CustomLocal");
        var nowUtc = new DateTimeOffset(2026, 11, 12, 18, 0, 0, TimeSpan.Zero); // Perşembe

        // 3 Oturum:
        // 1. 60 dk (3600 sn) Cumartesi saat 21:00
        var s1 = new ControllerSession
        {
            Id = 1,
            DeviceKey = "XBOX_TEST",
            StartedAtUtc = new DateTimeOffset(2026, 11, 7, 18, 0, 0, TimeSpan.Zero), // Yerel 21:00 Cumartesi
            EndedAtUtc = new DateTimeOffset(2026, 11, 7, 19, 0, 0, TimeSpan.Zero),
            DurationSeconds = 3600,
            EndReason = SessionEndReason.Disconnected
        };

        // 2. 120 dk (7200 sn) Cumartesi saat 22:00 (En uzun oturum)
        var s2 = new ControllerSession
        {
            Id = 2,
            DeviceKey = "XBOX_TEST",
            StartedAtUtc = new DateTimeOffset(2026, 11, 7, 19, 0, 0, TimeSpan.Zero), // Yerel 22:00 Cumartesi
            EndedAtUtc = new DateTimeOffset(2026, 11, 7, 21, 0, 0, TimeSpan.Zero),
            DurationSeconds = 7200,
            EndReason = SessionEndReason.Disconnected
        };

        // 3. 60 dk (3600 sn) Çarşamba saat 14:00
        var s3 = new ControllerSession
        {
            Id = 3,
            DeviceKey = "XBOX_TEST",
            StartedAtUtc = new DateTimeOffset(2026, 11, 11, 11, 0, 0, TimeSpan.Zero), // Yerel 14:00 Çarşamba
            EndedAtUtc = new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero),
            DurationSeconds = 3600,
            EndReason = SessionEndReason.Disconnected
        };

        var sessions = new List<ControllerSession> { s1, s2, s3 };
        var batteryChanges = new List<BatteryChange>
        {
            new() { Id = 1, DeviceKey = "XBOX_TEST", ChangedAtUtc = new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero), ChangeType = "İlk Kurulum (Fabrika Çıkışı)" },
            new() { Id = 2, DeviceKey = "XBOX_TEST", ChangedAtUtc = new DateTimeOffset(2026, 11, 11, 15, 0, 0, TimeSpan.Zero), ChangeType = "Manual" }
        };

        var report = AnalyticsEngine.ComputeAnalytics(sessions, batteryChanges, null, localTz, nowUtc);

        // Ortalama süre: (3600 + 7200 + 3600) / 3 = 4800 sn = 80 dk (1 sa 20 dk)
        Assert.Equal(TimeSpan.FromMinutes(80), report.AverageSessionDuration);

        // En uzun oturum: s2 (7200 sn = 2 saat)
        Assert.NotNull(report.LongestSession);
        Assert.Equal(2, report.LongestSession.Id);
        Assert.Equal(7200, report.LongestSession.DurationSeconds);

        // En aktif gün: Cumartesi (3600 + 7200 = 10800 sn / 14400 sn = %75)
        Assert.Equal(DayOfWeek.Saturday, report.MostActiveDay);
        Assert.Equal(75.0, report.MostActiveDayPercentage);

        // Toplam 3 oturum, 1 gerçek pil değişimi (ilk kurulum hariç)
        Assert.Equal(3, report.TotalSessionsCount);
        Assert.Equal(1, report.TotalBatteryChangesCount);

        // Pil başına bağlantı tahmini: 1. tamamlanmış pil seti için 14400 sn = 4 saat / 1 döngü = 4.0 saat
        Assert.Equal(4.0, report.EstimatedHoursPerBatterySet);
    }

    [Fact]
    public async Task SessionsCsvExport_ShouldGenerateValidCsvWithUtf8Bom()
    {
        var sessions = new List<ControllerSession>
        {
            new()
            {
                Id = 1,
                DeviceKey = "XBOX_TEST",
                StartedAtUtc = new DateTimeOffset(2026, 11, 12, 10, 0, 0, TimeSpan.Zero),
                EndedAtUtc = new DateTimeOffset(2026, 11, 12, 11, 30, 0, TimeSpan.Zero),
                DurationSeconds = 5400,
                EndReason = SessionEndReason.Disconnected,
                CreatedAtUtc = DateTimeOffset.UtcNow
            }
        };

        var csv = CsvExportService.GenerateSessionsCsv(sessions);
        Assert.Contains("ID;Cihaz;Başlangıç (Yerel);Bitiş (Yerel);Süre (Saniye);Süre (Formatlı);Bitiş Nedeni;Oluşturulma", csv);
        Assert.Contains("1 sa 30 dk 0 sn", csv);

        var tempCsvFile = Path.Combine(Path.GetTempPath(), $"test_sessions_{Guid.NewGuid():N}.csv");
        try
        {
            await CsvExportService.SaveCsvFileAsync(tempCsvFile, csv);
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
}
