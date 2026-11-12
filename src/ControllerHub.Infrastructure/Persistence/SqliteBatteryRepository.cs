using ControllerHub.Core.Interfaces;
using ControllerHub.Core.Models;
using Microsoft.Data.Sqlite;

namespace ControllerHub.Infrastructure.Persistence;

public class SqliteBatteryRepository : IBatteryRepository
{
    private readonly SqliteDatabaseContext _context;

    public SqliteBatteryRepository(SqliteDatabaseContext context)
    {
        _context = context;
    }

    public async Task RecordBatteryReadingAsync(BatteryReading reading)
    {
        using var conn = _context.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO BatteryReadings (DeviceKey, RecordedAtUtc, Percentage, Source, Confidence, Note)
            VALUES (@deviceKey, @recordedAt, @pct, @source, @conf, @note);";
        cmd.Parameters.AddWithValue("@deviceKey", reading.DeviceKey);
        cmd.Parameters.AddWithValue("@recordedAt", reading.RecordedAtUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@pct", reading.Percentage.HasValue ? reading.Percentage.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@source", reading.Source.ToString());
        cmd.Parameters.AddWithValue("@conf", reading.Confidence.ToString());
        cmd.Parameters.AddWithValue("@note", string.IsNullOrEmpty(reading.Note) ? DBNull.Value : reading.Note);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RecordBatteryChangeAsync(BatteryChange change)
    {
        using var conn = _context.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO BatteryChanges (DeviceKey, ChangedAtUtc, PreviousPercentage, NewPercentage, Note, ChangeType)
            VALUES (@deviceKey, @changedAt, @prevPct, @newPct, @note, @changeType);";
        cmd.Parameters.AddWithValue("@deviceKey", change.DeviceKey);
        cmd.Parameters.AddWithValue("@changedAt", change.ChangedAtUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@prevPct", change.PreviousPercentage.HasValue ? change.PreviousPercentage.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@newPct", change.NewPercentage.HasValue ? change.NewPercentage.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@note", string.IsNullOrEmpty(change.Note) ? DBNull.Value : change.Note);
        cmd.Parameters.AddWithValue("@changeType", change.ChangeType);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<BatteryReading?> GetLatestReadingAsync(string? deviceKey = null)
    {
        using var conn = _context.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, DeviceKey, RecordedAtUtc, Percentage, Source, Confidence, Note
            FROM BatteryReadings
            WHERE (@deviceKey IS NULL OR DeviceKey = @deviceKey)
            ORDER BY RecordedAtUtc DESC LIMIT 1;";
        cmd.Parameters.AddWithValue("@deviceKey", string.IsNullOrEmpty(deviceKey) ? DBNull.Value : deviceKey);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var r = new BatteryReading
            {
                Id = reader.GetInt64(0),
                DeviceKey = reader.GetString(1),
                RecordedAtUtc = DateTimeOffset.Parse(reader.GetString(2)),
                Percentage = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                Source = Enum.TryParse<BatterySource>(reader.GetString(4), out var s) ? s : BatterySource.WindowsApiGatt,
                Confidence = Enum.TryParse<BatteryConfidence>(reader.GetString(5), out var c) ? c : BatteryConfidence.Exact,
                Note = reader.IsDBNull(6) ? null : reader.GetString(6)
            };
            return r;
        }

        return null;
    }

    public async Task<IReadOnlyList<BatteryReading>> GetRecentReadingsAsync(string deviceKey, int limit = 20)
    {
        var list = new List<BatteryReading>();
        using var conn = _context.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, DeviceKey, RecordedAtUtc, Percentage, Source, Confidence, Note
            FROM BatteryReadings
            WHERE DeviceKey = @deviceKey
            ORDER BY RecordedAtUtc DESC LIMIT @limit;";
        cmd.Parameters.AddWithValue("@deviceKey", deviceKey);
        cmd.Parameters.AddWithValue("@limit", limit);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new BatteryReading
            {
                Id = reader.GetInt64(0),
                DeviceKey = reader.GetString(1),
                RecordedAtUtc = DateTimeOffset.Parse(reader.GetString(2)),
                Percentage = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                Source = Enum.TryParse<BatterySource>(reader.GetString(4), out var s) ? s : BatterySource.WindowsApiGatt,
                Confidence = Enum.TryParse<BatteryConfidence>(reader.GetString(5), out var c) ? c : BatteryConfidence.Exact,
                Note = reader.IsDBNull(6) ? null : reader.GetString(6)
            });
        }

        return list;
    }

    public async Task<IReadOnlyList<BatteryChange>> GetBatteryChangesAsync(int limit = 50)
    {
        var list = new List<BatteryChange>();
        using var conn = _context.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, DeviceKey, ChangedAtUtc, PreviousPercentage, NewPercentage, Note, ChangeType
            FROM BatteryChanges
            ORDER BY ChangedAtUtc DESC LIMIT @limit;";
        cmd.Parameters.AddWithValue("@limit", limit);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new BatteryChange
            {
                Id = reader.GetInt64(0),
                DeviceKey = reader.GetString(1),
                ChangedAtUtc = DateTimeOffset.Parse(reader.GetString(2)),
                PreviousPercentage = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                NewPercentage = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                Note = reader.IsDBNull(5) ? null : reader.GetString(5),
                ChangeType = reader.GetString(6)
            });
        }

        return list;
    }
}
