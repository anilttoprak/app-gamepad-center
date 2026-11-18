using System.Text;
using ControllerHub.Core.Models;

namespace ControllerHub.Infrastructure.Export;

public static class CsvExportService
{
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    public static string GenerateBatteryChangesCsv(IEnumerable<BatteryChange> changes)
    {
        var sb = new StringBuilder();
        // Excel uyumlu noktalı virgül ayracı ve Türkçe başlıklar
        sb.AppendLine("ID;Cihaz;Değişim Tarihi (Yerel);Eski Yüzde (%);Yeni Yüzde (%);Pil Modeli / Not;Kayıt Türü");

        foreach (var c in changes)
        {
            var localDate = c.ChangedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            var prev = c.PreviousPercentage.HasValue ? c.PreviousPercentage.Value.ToString() : "";
            var next = c.NewPercentage.HasValue ? c.NewPercentage.Value.ToString() : "";
            var note = EscapeCsvField(c.Note ?? "");
            var type = c.ChangeType;

            sb.AppendLine($"{c.Id};{c.DeviceKey};{localDate};{prev};{next};{note};{type}");
        }

        return sb.ToString();
    }

    public static string GenerateSessionsCsv(IEnumerable<ControllerSession> sessions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ID;Cihaz;Başlangıç (Yerel);Bitiş (Yerel);Süre (Saniye);Süre (Formatlı);Bitiş Nedeni;Oluşturulma");

        foreach (var s in sessions)
        {
            var deviceKey = EscapeCsvField(s.DeviceKey);
            var start = s.StartedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            var end = s.EndedAtUtc.HasValue ? s.EndedAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "Aktif";
            var duration = TimeSpan.FromSeconds(s.DurationSeconds);
            var durationFormatted = $"{(int)duration.TotalHours} sa {duration.Minutes} dk {duration.Seconds} sn";
            var endReason = EscapeCsvField(s.EndReason.ToString());

            sb.AppendLine($"{s.Id};{deviceKey};{start};{end};{s.DurationSeconds};{durationFormatted};{endReason};{s.CreatedAtUtc:yyyy-MM-dd HH:mm}");
        }

        return sb.ToString();
    }

    public static async Task SaveCsvFileAsync(string filePath, string csvContent)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllTextAsync(filePath, csvContent, Utf8WithBom);
    }

    private static string EscapeCsvField(string field)
    {
        if (string.IsNullOrEmpty(field)) return "";

        // Formula Injection (DDE) Koruması: =, +, -, @ ile başlıyorsa Excel çalıştırmasın diye tek tırnak ekle
        if (field.StartsWith('=') || field.StartsWith('+') || field.StartsWith('-') || field.StartsWith('@'))
        {
            field = "'" + field;
        }

        if (field.Contains(';') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }
}
