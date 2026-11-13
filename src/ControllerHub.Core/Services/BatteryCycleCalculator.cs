using ControllerHub.Core.Models;

namespace ControllerHub.Core.Services;

public record BatteryCycleStats(
    long? ChangeId,
    string BatteryName,
    DateTimeOffset InstalledAtUtc,
    DateTimeOffset? ReplacedAtUtc,
    TimeSpan TotalUsageDuration,
    int SessionCount,
    bool IsActiveCycle)
{
    public string DurationFormatted => SessionTimeCalculator.FormatDurationText(TotalUsageDuration);
}

public record BatteryOverallPerformance(
    IReadOnlyList<BatteryCycleStats> Cycles,
    BatteryCycleStats? ActiveCycle,
    TimeSpan AverageDurationPerCycle,
    BatteryCycleStats? BestCycle,
    int TotalChangesCount);

public static class BatteryCycleCalculator
{
    public static BatteryOverallPerformance ComputePerformance(
        IEnumerable<ControllerSession> sessions,
        IReadOnlyList<BatteryChange> batteryChanges,
        ControllerSession? activeSession,
        DateTimeOffset nowUtc)
    {
        var completedSessions = sessions.Where(s => s.EndedAtUtc.HasValue && s.DurationSeconds > 0).ToList();

        if (batteryChanges.Count == 0)
        {
            // Hiç pil değişimi kaydedilmemiş: Tüm oturumlar mevcut varsayılan pile aittir
            var totalDuration = SessionTimeCalculator.CalculateTotalDuration(completedSessions, activeSession, nowUtc);
            int totalSessions = completedSessions.Count + (activeSession != null && activeSession.IsActive ? 1 : 0);

            var defaultCycle = new BatteryCycleStats(
                ChangeId: null,
                BatteryName: "Varsayılan Pil Seti",
                InstalledAtUtc: completedSessions.Count > 0 ? completedSessions.Min(s => s.StartedAtUtc) : nowUtc,
                ReplacedAtUtc: null,
                TotalUsageDuration: totalDuration,
                SessionCount: totalSessions,
                IsActiveCycle: true);

            return new BatteryOverallPerformance(
                Cycles: new List<BatteryCycleStats> { defaultCycle },
                ActiveCycle: defaultCycle,
                AverageDurationPerCycle: TimeSpan.Zero,
                BestCycle: totalDuration > TimeSpan.Zero ? defaultCycle : null,
                TotalChangesCount: 0);
        }

        // Değişimleri kronolojik olarak sırala (en eskiden en yeniye)
        var sortedChanges = batteryChanges.OrderBy(c => c.ChangedAtUtc).ToList();
        var cycleList = new List<BatteryCycleStats>();

        for (int i = 0; i < sortedChanges.Count; i++)
        {
            var change = sortedChanges[i];
            var installedAt = change.ChangedAtUtc;
            bool isActive = i == sortedChanges.Count - 1;
            DateTimeOffset cycleEnd = isActive ? nowUtc : sortedChanges[i + 1].ChangedAtUtc;

            double totalSeconds = 0;
            int cycleSessionCount = 0;

            foreach (var s in completedSessions)
            {
                var overlap = GetOverlapSeconds(s.StartedAtUtc, s.EndedAtUtc!.Value, installedAt, cycleEnd);
                if (overlap > 0)
                {
                    totalSeconds += overlap;
                    cycleSessionCount++;
                }
            }

            if (isActive && activeSession != null && activeSession.IsActive)
            {
                var effectiveEnd = nowUtc < activeSession.StartedAtUtc ? activeSession.StartedAtUtc : nowUtc;
                var activeOverlap = GetOverlapSeconds(activeSession.StartedAtUtc, effectiveEnd, installedAt, cycleEnd);
                if (activeOverlap > 0)
                {
                    totalSeconds += activeOverlap;
                    cycleSessionCount++;
                }
            }

            var batteryName = string.IsNullOrWhiteSpace(change.Note) ? "Pil Seti #" + change.Id : change.Note;
            var cycle = new BatteryCycleStats(
                ChangeId: change.Id,
                BatteryName: batteryName,
                InstalledAtUtc: installedAt,
                ReplacedAtUtc: isActive ? null : cycleEnd,
                TotalUsageDuration: TimeSpan.FromSeconds(Math.Max(0, totalSeconds)),
                SessionCount: cycleSessionCount,
                IsActiveCycle: isActive);

            cycleList.Add(cycle);
        }

        var activeCycle = cycleList.FirstOrDefault(c => c.IsActiveCycle);
        var completedCycles = cycleList.Where(c => !c.IsActiveCycle && c.TotalUsageDuration.TotalSeconds > 0).ToList();

        TimeSpan avgDuration = TimeSpan.Zero;
        if (completedCycles.Count > 0)
        {
            var avgSeconds = completedCycles.Average(c => c.TotalUsageDuration.TotalSeconds);
            avgDuration = TimeSpan.FromSeconds(avgSeconds);
        }

        var bestCycle = cycleList
            .Where(c => c.TotalUsageDuration.TotalSeconds > 0)
            .MaxBy(c => c.TotalUsageDuration.TotalSeconds);

        return new BatteryOverallPerformance(
            Cycles: cycleList,
            ActiveCycle: activeCycle,
            AverageDurationPerCycle: avgDuration,
            BestCycle: bestCycle,
            TotalChangesCount: batteryChanges.Count);
    }

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
}
