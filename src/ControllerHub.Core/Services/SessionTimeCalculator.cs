using ControllerHub.Core.Models;

namespace ControllerHub.Core.Services;

/// <summary>
/// Oturum sürelerini, toplam bağlantı süresini ve gece yarısını aşan oturumların
/// gün bazlı dağılımını yüksek hassasiyetle hesaplayan motor (FR-04, FR-06).
/// </summary>
public static class SessionTimeCalculator
{
    /// <summary>
    /// Verilen oturum listesinin ve varsa aktif oturumun toplam süresini hesaplar.
    /// </summary>
    public static TimeSpan CalculateTotalDuration(
        IEnumerable<ControllerSession> sessions,
        ControllerSession? activeSession,
        DateTimeOffset nowUtc)
    {
        long totalSeconds = 0;

        foreach (var session in sessions)
        {
            if (session.EndedAtUtc.HasValue)
            {
                totalSeconds += session.DurationSeconds;
            }
        }

        if (activeSession != null && activeSession.IsActive)
        {
            var activeDuration = activeSession.GetEffectiveDuration(nowUtc);
            totalSeconds += (long)activeDuration.TotalSeconds;
        }

        return TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
    }

    /// <summary>
    /// Verilen oturum listesinin ve birden fazla aktif oturumun toplam süresini hesaplar.
    /// </summary>
    public static TimeSpan CalculateTotalDurationMulti(
        IEnumerable<ControllerSession> sessions,
        IEnumerable<ControllerSession>? activeSessions,
        DateTimeOffset nowUtc)
    {
        long totalSeconds = 0;

        foreach (var session in sessions)
        {
            if (session.EndedAtUtc.HasValue)
            {
                totalSeconds += session.DurationSeconds;
            }
        }

        if (activeSessions != null)
        {
            foreach (var act in activeSessions)
            {
                if (act != null && act.IsActive)
                {
                    var activeDuration = act.GetEffectiveDuration(nowUtc);
                    totalSeconds += (long)activeDuration.TotalSeconds;
                }
            }
        }

        return TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
    }

    /// <summary>
    /// Belirtilen takvim gününde (yerel saat 00:00 - 23:59:59) gerçekleşen bağlantı süresini hesaplar.
    /// Gece yarısını aşan oturumların yalnızca o güne denk gelen dilimini alır.
    /// </summary>
    public static TimeSpan CalculateDayDuration(
        IEnumerable<ControllerSession> sessions,
        ControllerSession? activeSession,
        DateOnly targetDate,
        TimeZoneInfo localTimeZone,
        DateTimeOffset nowUtc)
    {
        // Hedef günün yerel başlangıç ve bitişini belirle (00:00:00 ile ertesi gün 00:00:00 arası tam 24 saat)
        var dayStartLocal = targetDate.ToDateTime(TimeOnly.MinValue);
        var dayEndLocal = dayStartLocal.AddDays(1);

        var dayStartUtc = new DateTimeOffset(dayStartLocal, localTimeZone.GetUtcOffset(dayStartLocal)).ToUniversalTime();
        var dayEndUtc = new DateTimeOffset(dayEndLocal, localTimeZone.GetUtcOffset(dayEndLocal)).ToUniversalTime();

        double totalDaySeconds = 0;

        // 1. Kapanmış oturumların o güne düşen payını hesapla
        foreach (var s in sessions)
        {
            if (!s.EndedAtUtc.HasValue) continue;

            totalDaySeconds += GetOverlapSeconds(s.StartedAtUtc, s.EndedAtUtc.Value, dayStartUtc, dayEndUtc);
        }

        // 2. Varsa aktif oturumun o güne düşen payını hesapla
        if (activeSession != null && activeSession.IsActive)
        {
            var effectiveEndUtc = nowUtc < activeSession.StartedAtUtc ? activeSession.StartedAtUtc : nowUtc;
            totalDaySeconds += GetOverlapSeconds(activeSession.StartedAtUtc, effectiveEndUtc, dayStartUtc, dayEndUtc);
        }

        return TimeSpan.FromSeconds(Math.Max(0, totalDaySeconds));
    }

    /// <summary>
    /// Belirtilen takvim gününde (yerel saat 00:00 - 23:59:59) gerçekleşen bağlantı süresini çoklu aktif oturum desteğiyle hesaplar.
    /// </summary>
    public static TimeSpan CalculateDayDurationMulti(
        IEnumerable<ControllerSession> sessions,
        IEnumerable<ControllerSession>? activeSessions,
        DateOnly targetDate,
        TimeZoneInfo localTimeZone,
        DateTimeOffset nowUtc)
    {
        // Hedef günün yerel başlangıç ve bitişini belirle (00:00:00 ile ertesi gün 00:00:00 arası tam 24 saat)
        var dayStartLocal = targetDate.ToDateTime(TimeOnly.MinValue);
        var dayEndLocal = dayStartLocal.AddDays(1);

        var dayStartUtc = new DateTimeOffset(dayStartLocal, localTimeZone.GetUtcOffset(dayStartLocal)).ToUniversalTime();
        var dayEndUtc = new DateTimeOffset(dayEndLocal, localTimeZone.GetUtcOffset(dayEndLocal)).ToUniversalTime();

        double totalDaySeconds = 0;

        // 1. Kapanmış oturumların o güne düşen payını hesapla
        foreach (var s in sessions)
        {
            if (!s.EndedAtUtc.HasValue) continue;

            totalDaySeconds += GetOverlapSeconds(s.StartedAtUtc, s.EndedAtUtc.Value, dayStartUtc, dayEndUtc);
        }

        // 2. Varsa aktif oturumların o güne düşen payını hesapla
        if (activeSessions != null)
        {
            foreach (var act in activeSessions)
            {
                if (act != null && act.IsActive)
                {
                    var effectiveEndUtc = nowUtc < act.StartedAtUtc ? act.StartedAtUtc : nowUtc;
                    totalDaySeconds += GetOverlapSeconds(act.StartedAtUtc, effectiveEndUtc, dayStartUtc, dayEndUtc);
                }
            }
        }

        return TimeSpan.FromSeconds(Math.Max(0, totalDaySeconds));
    }

    /// <summary>
    /// İki zaman aralığı ([startA, endA] ve [startB, endB]) arasındaki kesişim süresini saniye olarak döner.
    /// </summary>
    private static double GetOverlapSeconds(
        DateTimeOffset startA,
        DateTimeOffset endA,
        DateTimeOffset startB,
        DateTimeOffset endB)
    {
        var overlapStart = startA > startB ? startA : startB;
        var overlapEnd = endA < endB ? endA : endB;

        if (overlapEnd > overlapStart)
        {
            return (overlapEnd - overlapStart).TotalSeconds;
        }

        return 0;
    }

    /// <summary>
    /// Süreyi kullanıcı dostu Türkçe metne dönüştürür (Örn: "2 sa 45 dk", "35 dk", "12 sn", "0 dk").
    /// </summary>
    public static string FormatDurationText(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return "0 dk";
        }

        if (duration.TotalMinutes < 1)
        {
            return $"{Math.Max(1, duration.Seconds)} sn";
        }

        var hours = (int)duration.TotalHours;
        var minutes = duration.Minutes;

        if (hours == 0)
        {
            return $"{minutes} dk";
        }

        return $"{hours} sa {minutes} dk";
    }
}
