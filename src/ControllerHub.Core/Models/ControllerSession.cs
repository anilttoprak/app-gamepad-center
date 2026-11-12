namespace ControllerHub.Core.Models;

public class ControllerSession
{
    public long Id { get; set; }
    public string DeviceKey { get; set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }
    public int DurationSeconds { get; set; }
    public SessionEndReason EndReason { get; set; } = SessionEndReason.Unknown;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public bool IsActive => EndedAtUtc == null;

    public TimeSpan GetEffectiveDuration(DateTimeOffset nowUtc)
    {
        if (EndedAtUtc.HasValue)
        {
            return TimeSpan.FromSeconds(DurationSeconds);
        }

        var duration = nowUtc - StartedAtUtc;
        return duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
    }
}
