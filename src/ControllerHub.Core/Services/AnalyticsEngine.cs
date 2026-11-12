using ControllerHub.Core.Models;

namespace ControllerHub.Core.Services;

public record AnalyticsReport(
    TimeSpan AverageSessionDuration,
    ControllerSession? LongestSession,
    DayOfWeek? MostActiveDay,
    double MostActiveDayPercentage,
    string MostActiveHourRange,
    TimeSpan ThisWeekTotal,
    TimeSpan ThisMonthTotal,
    TimeSpan ThisYearTotal,
    int TotalSessionsCount,
    int TotalBatteryChangesCount,
    double? EstimatedHoursPerBatterySet);

public static class AnalyticsEngine
{
    public static AnalyticsReport ComputeAnalytics(
        IReadOnlyList<ControllerSession> sessions,
        IReadOnlyList<BatteryChange> batteryChanges,
        ControllerSession? activeSession,
        TimeZoneInfo localTimeZone,
        DateTimeOffset nowUtc)
    {
        var completedSessions = sessions.Where(s => s.EndedAtUtc.HasValue && s.DurationSeconds > 0).ToList();

        // 1. Ortalama Oturum Süresi
        var avgSeconds = completedSessions.Count > 0 ?
            completedSessions.Average(s => s.DurationSeconds) : 0;
        var avgDuration = TimeSpan.FromSeconds(avgSeconds);

        // 2. En Uzun Oturum (Rekor)
        ControllerSession? longest = null;
        if (completedSessions.Count > 0)
        {
            longest = completedSessions.MaxBy(s => s.DurationSeconds);
        }

        // Aktif oturum şu an rekoru geçmişse
        if (activeSession != null && activeSession.IsActive)
        {
            var activeDuration = (int)activeSession.GetEffectiveDuration(nowUtc).TotalSeconds;
            if (longest == null || activeDuration > longest.DurationSeconds)
            {
                longest = activeSession;
            }
        }

        // 3. En Aktif Gün (Haftanın günlerine göre toplam süre dağılımı)
        var dayTotals = new Dictionary<DayOfWeek, double>();
        foreach (DayOfWeek d in Enum.GetValues<DayOfWeek>())
        {
            dayTotals[d] = 0;
        }

        foreach (var s in completedSessions)
        {
            var localDay = s.StartedAtUtc.ToLocalTime().DayOfWeek;
            dayTotals[localDay] += s.DurationSeconds;
        }

        var totalAllSeconds = dayTotals.Values.Sum();
        DayOfWeek? mostActiveDay = null;
        double mostActivePct = 0;

        if (totalAllSeconds > 0)
        {
            var topDay = dayTotals.MaxBy(kv => kv.Value);
            mostActiveDay = topDay.Key;
            mostActivePct = Math.Round((topDay.Value / totalAllSeconds) * 100, 1);
        }

        // 4. En Aktif Saat Aralığı (Günün 3 saatlik dilimleri)
        var hourBuckets = new Dictionary<string, double>
        {
            ["06:00 - 09:00 (Sabah)"] = 0,
            ["09:00 - 13:00 (Öğle)"] = 0,
            ["13:00 - 18:00 (Öğleden Sonra)"] = 0,
            ["18:00 - 21:00 (Akşamüstü)"] = 0,
            ["21:00 - 00:00 (Gece)"] = 0,
            ["00:00 - 06:00 (Gece Yarısı)"] = 0
        };

        foreach (var s in completedSessions)
        {
            var hour = s.StartedAtUtc.ToLocalTime().Hour;
            var bucketKey = hour switch
            {
                >= 6 and < 9 => "06:00 - 09:00 (Sabah)",
                >= 9 and < 13 => "09:00 - 13:00 (Öğle)",
                >= 13 and < 18 => "13:00 - 18:00 (Öğleden Sonra)",
                >= 18 and < 21 => "18:00 - 21:00 (Akşamüstü)",
                >= 21 and <= 23 => "21:00 - 00:00 (Gece)",
                _ => "00:00 - 06:00 (Gece Yarısı)"
            };
            hourBuckets[bucketKey] += s.DurationSeconds;
        }

        var mostActiveHour = hourBuckets.Values.Sum() > 0 ?
            hourBuckets.MaxBy(kv => kv.Value).Key : "Henüz veri yok";

        // 5. Dönemsel Toplamlar: Bu Hafta, Bu Ay, Bu Yıl
        var todayLocal = DateOnly.FromDateTime(DateTime.Now);

        // Bu hafta (Pazartesi başlangıçlı)
        int diffToMonday = ((int)todayLocal.DayOfWeek + 6) % 7;
        var monday = todayLocal.AddDays(-diffToMonday);
        double weekSeconds = 0;
        for (int i = 0; i <= diffToMonday; i++)
        {
            weekSeconds += SessionTimeCalculator.CalculateDayDuration(sessions, activeSession, monday.AddDays(i), localTimeZone, nowUtc).TotalSeconds;
        }

        // Bu ay (1. günden bugüne)
        var firstDayOfMonth = new DateOnly(todayLocal.Year, todayLocal.Month, 1);
        double monthSeconds = 0;
        for (var d = firstDayOfMonth; d <= todayLocal; d = d.AddDays(1))
        {
            monthSeconds += SessionTimeCalculator.CalculateDayDuration(sessions, activeSession, d, localTimeZone, nowUtc).TotalSeconds;
        }

        // Bu yıl
        var thisYearStartUtc = new DateTimeOffset(new DateTime(todayLocal.Year, 1, 1), TimeSpan.Zero);
        var yearSeconds = sessions
            .Where(s => s.StartedAtUtc >= thisYearStartUtc && s.EndedAtUtc.HasValue)
            .Sum(s => s.DurationSeconds);
        if (activeSession != null && activeSession.IsActive)
        {
            yearSeconds += (int)activeSession.GetEffectiveDuration(nowUtc).TotalSeconds;
        }

        // 6. Pil Başına Düşen Ortalama Bağlantı Süresi (Tahmin)
        double? hoursPerSet = null;
        if (batteryChanges.Count > 0 && totalAllSeconds > 0)
        {
            var totalHours = totalAllSeconds / 3600.0;
            hoursPerSet = Math.Round(totalHours / batteryChanges.Count, 1);
        }

        return new AnalyticsReport(
            AverageSessionDuration: avgDuration,
            LongestSession: longest,
            MostActiveDay: mostActiveDay,
            MostActiveDayPercentage: mostActivePct,
            MostActiveHourRange: mostActiveHour,
            ThisWeekTotal: TimeSpan.FromSeconds(weekSeconds),
            ThisMonthTotal: TimeSpan.FromSeconds(monthSeconds),
            ThisYearTotal: TimeSpan.FromSeconds(yearSeconds),
            TotalSessionsCount: sessions.Count + (activeSession != null ? 1 : 0),
            TotalBatteryChangesCount: batteryChanges.Count,
            EstimatedHoursPerBatterySet: hoursPerSet);
    }
}
