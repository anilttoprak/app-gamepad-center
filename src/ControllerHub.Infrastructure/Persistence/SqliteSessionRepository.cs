using ControllerHub.Core.Interfaces;
using ControllerHub.Core.Models;
using Microsoft.Data.Sqlite;

namespace ControllerHub.Infrastructure.Persistence;

public class SqliteSessionRepository : ISessionRepository
{
    private readonly SqliteDatabaseContext _context;

    public SqliteSessionRepository(SqliteDatabaseContext context)
    {
        _context = context;
    }

    public async Task<ControllerSession> StartSessionAsync(string deviceKey, DateTimeOffset startedAtUtc)
    {
        using var conn = _context.CreateConnection();
        using var trans = conn.BeginTransaction();

        // 1. Varsa bu cihaz için 0 saniyelik hayalet açık oturumları sil
        using var deleteGhostCmd = conn.CreateCommand();
        deleteGhostCmd.Transaction = trans;
        deleteGhostCmd.CommandText = @"
            DELETE FROM Sessions 
            WHERE DeviceKey = @deviceKey 
              AND EndedAtUtc IS NULL 
              AND CAST((strftime('%s', @endedAt) - strftime('%s', StartedAtUtc)) AS INTEGER) <= 0;";
        deleteGhostCmd.Parameters.AddWithValue("@endedAt", startedAtUtc.ToString("o"));
        deleteGhostCmd.Parameters.AddWithValue("@deviceKey", deviceKey);
        await deleteGhostCmd.ExecuteNonQueryAsync();

        // Varsa gerçek süresi olan önceki açık oturumları güvenli biçimde kurtar/kapat (FR-05)
        using var closePrevious = conn.CreateCommand();
        closePrevious.Transaction = trans;
        closePrevious.CommandText = @"
            UPDATE Sessions 
            SET EndedAtUtc = @endedAt,
                DurationSeconds = CAST((strftime('%s', @endedAt) - strftime('%s', StartedAtUtc)) AS INTEGER),
                EndReason = 'Recovery'
            WHERE DeviceKey = @deviceKey AND EndedAtUtc IS NULL;";
        closePrevious.Parameters.AddWithValue("@endedAt", startedAtUtc.ToString("o"));
        closePrevious.Parameters.AddWithValue("@deviceKey", deviceKey);
        await closePrevious.ExecuteNonQueryAsync();

        // 2. Yeni aktif oturumu ekle
        var session = new ControllerSession
        {
            DeviceKey = deviceKey,
            StartedAtUtc = startedAtUtc,
            EndedAtUtc = null,
            DurationSeconds = 0,
            EndReason = SessionEndReason.Unknown,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        using var insertCmd = conn.CreateCommand();
        insertCmd.Transaction = trans;
        insertCmd.CommandText = @"
            INSERT INTO Sessions (DeviceKey, StartedAtUtc, EndedAtUtc, DurationSeconds, EndReason, CreatedAtUtc)
            VALUES (@deviceKey, @startedAt, NULL, 0, 'Unknown', @createdAt);
            SELECT last_insert_rowid();";
        insertCmd.Parameters.AddWithValue("@deviceKey", session.DeviceKey);
        insertCmd.Parameters.AddWithValue("@startedAt", session.StartedAtUtc.ToString("o"));
        insertCmd.Parameters.AddWithValue("@createdAt", session.CreatedAtUtc.ToString("o"));

        var rowId = await insertCmd.ExecuteScalarAsync();
        session.Id = Convert.ToInt64(rowId);

        trans.Commit();
        return session;
    }

    public async Task<ControllerSession?> EndSessionAsync(string deviceKey, DateTimeOffset endedAtUtc, SessionEndReason reason)
    {
        using var conn = _context.CreateConnection();

        // 1. Önce aktif oturumu bul
        var active = await GetActiveSessionAsync(deviceKey);
        if (active == null) return null;

        var duration = endedAtUtc - active.StartedAtUtc;
        var durationSeconds = (int)Math.Max(0, duration.TotalSeconds);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE Sessions 
            SET EndedAtUtc = @endedAt,
                DurationSeconds = @duration,
                EndReason = @reason
            WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@endedAt", endedAtUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@duration", durationSeconds);
        cmd.Parameters.AddWithValue("@reason", reason.ToString());
        cmd.Parameters.AddWithValue("@id", active.Id);

        await cmd.ExecuteNonQueryAsync();

        active.EndedAtUtc = endedAtUtc;
        active.DurationSeconds = durationSeconds;
        active.EndReason = reason;

        return active;
    }

    public async Task<ControllerSession?> GetActiveSessionAsync(string deviceKey)
    {
        using var conn = _context.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, DeviceKey, StartedAtUtc, EndedAtUtc, DurationSeconds, EndReason, CreatedAtUtc
            FROM Sessions
            WHERE DeviceKey = @deviceKey AND EndedAtUtc IS NULL
            ORDER BY Id DESC LIMIT 1;";
        cmd.Parameters.AddWithValue("@deviceKey", deviceKey);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapReaderToSession(reader);
        }

        return null;
    }

    public async Task<IReadOnlyList<ControllerSession>> GetRecentSessionsAsync(int limit = 50)
    {
        var list = new List<ControllerSession>();
        using var conn = _context.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, DeviceKey, StartedAtUtc, EndedAtUtc, DurationSeconds, EndReason, CreatedAtUtc
            FROM Sessions
            ORDER BY StartedAtUtc DESC LIMIT @limit;";
        cmd.Parameters.AddWithValue("@limit", limit);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(MapReaderToSession(reader));
        }

        return list;
    }

    public async Task<IReadOnlyList<ControllerSession>> GetSessionsInRangeAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        var list = new List<ControllerSession>();
        using var conn = _context.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, DeviceKey, StartedAtUtc, EndedAtUtc, DurationSeconds, EndReason, CreatedAtUtc
            FROM Sessions
            WHERE StartedAtUtc >= @fromUtc AND StartedAtUtc <= @toUtc
            ORDER BY StartedAtUtc ASC;";
        cmd.Parameters.AddWithValue("@fromUtc", fromUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@toUtc", toUtc.ToString("o"));

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(MapReaderToSession(reader));
        }

        return list;
    }

    public async Task<int> RecoverOrphanedSessionsAsync(DateTimeOffset recoveryTimeUtc)
    {
        using var conn = _context.CreateConnection();
        using var trans = conn.BeginTransaction();

        // 1. Geçmişteki 0 saniyelik hayalet kurtarma artıklarını temizle
        using var cleanupCmd = conn.CreateCommand();
        cleanupCmd.Transaction = trans;
        cleanupCmd.CommandText = "DELETE FROM Sessions WHERE DurationSeconds <= 0 AND EndReason = 'Recovery';";
        await cleanupCmd.ExecuteNonQueryAsync();

        // 2. Beklenmeyen kapanışlardan kalan açık oturumları kapat
        using var cmd = conn.CreateCommand();
        cmd.Transaction = trans;
        cmd.CommandText = @"
            UPDATE Sessions 
            SET EndedAtUtc = @recoveryTime,
                DurationSeconds = CAST(MAX(0, (strftime('%s', @recoveryTime) - strftime('%s', StartedAtUtc))) AS INTEGER),
                EndReason = 'Recovery'
            WHERE EndedAtUtc IS NULL;";
        cmd.Parameters.AddWithValue("@recoveryTime", recoveryTimeUtc.ToString("o"));

        var result = await cmd.ExecuteNonQueryAsync();
        trans.Commit();
        return result;
    }

    public async Task<bool> DeleteSessionAsync(long id)
    {
        using var conn = _context.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Sessions WHERE Id = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        var rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public async Task<int> ClearCompletedSessionsAsync(string? deviceKey = null)
    {
        using var conn = _context.CreateConnection();
        using var cmd = conn.CreateCommand();
        if (string.IsNullOrWhiteSpace(deviceKey) || deviceKey == "ALL")
        {
            cmd.CommandText = "DELETE FROM Sessions WHERE EndedAtUtc IS NOT NULL;";
        }
        else
        {
            cmd.CommandText = "DELETE FROM Sessions WHERE EndedAtUtc IS NOT NULL AND DeviceKey = @deviceKey;";
            cmd.Parameters.AddWithValue("@deviceKey", deviceKey);
        }
        return await cmd.ExecuteNonQueryAsync();
    }

    private static ControllerSession MapReaderToSession(SqliteDataReader reader)
    {
        var session = new ControllerSession
        {
            Id = reader.GetInt64(0),
            DeviceKey = reader.GetString(1),
            StartedAtUtc = DateTimeOffset.Parse(reader.GetString(2)),
            DurationSeconds = reader.GetInt32(4),
            EndReason = Enum.TryParse<SessionEndReason>(reader.GetString(5), out var r) ? r : SessionEndReason.Unknown,
            CreatedAtUtc = DateTimeOffset.Parse(reader.GetString(6))
        };

        if (!reader.IsDBNull(3))
        {
            session.EndedAtUtc = DateTimeOffset.Parse(reader.GetString(3));
        }

        return session;
    }
}
