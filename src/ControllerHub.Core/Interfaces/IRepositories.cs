using ControllerHub.Core.Models;

namespace ControllerHub.Core.Interfaces;

public interface ISessionRepository
{
    Task<ControllerSession> StartSessionAsync(string deviceKey, DateTimeOffset startedAtUtc);
    Task<ControllerSession?> EndSessionAsync(string deviceKey, DateTimeOffset endedAtUtc, SessionEndReason reason);
    Task<ControllerSession?> GetActiveSessionAsync(string deviceKey);
    Task<IReadOnlyList<ControllerSession>> GetRecentSessionsAsync(int limit = 50);
    Task<IReadOnlyList<ControllerSession>> GetSessionsInRangeAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc);
    Task<int> RecoverOrphanedSessionsAsync(DateTimeOffset recoveryTimeUtc);
}

public interface IBatteryRepository
{
    Task RecordBatteryReadingAsync(BatteryReading reading);
    Task RecordBatteryChangeAsync(BatteryChange change);
    Task<BatteryReading?> GetLatestReadingAsync(string? deviceKey = null);
    Task<IReadOnlyList<BatteryReading>> GetRecentReadingsAsync(string deviceKey, int limit = 20);
    Task<IReadOnlyList<BatteryChange>> GetBatteryChangesAsync(int limit = 50, string? deviceKey = null);
    Task<bool> DeleteBatteryChangeAsync(long id);
}

public interface ISettingsRepository
{
    Task<AppSettings> LoadSettingsAsync();
    Task SaveSettingsAsync(AppSettings settings);
}
