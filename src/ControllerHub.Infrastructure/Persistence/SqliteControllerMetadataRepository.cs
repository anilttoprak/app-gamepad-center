using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ControllerHub.Core.Interfaces;
using Microsoft.Data.Sqlite;

namespace ControllerHub.Infrastructure.Persistence;

public class SqliteControllerMetadataRepository : IControllerMetadataRepository
{
    private readonly SqliteDatabaseContext _context;

    public SqliteControllerMetadataRepository(SqliteDatabaseContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<string?> GetCustomNameAsync(string deviceKey)
    {
        using var conn = _context.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CustomName FROM ControllerMetadata WHERE DeviceKey = @key LIMIT 1;";
        cmd.Parameters.AddWithValue("@key", deviceKey);

        var result = await cmd.ExecuteScalarAsync();
        return result as string;
    }

    public async Task SetCustomNameAsync(string deviceKey, string customName)
    {
        using var conn = _context.CreateConnection();
        using var cmd = conn.CreateCommand();

        if (string.IsNullOrWhiteSpace(customName))
        {
            cmd.CommandText = "DELETE FROM ControllerMetadata WHERE DeviceKey = @key;";
            cmd.Parameters.AddWithValue("@key", deviceKey);
            await cmd.ExecuteNonQueryAsync();
            return;
        }

        cmd.CommandText = @"
            INSERT INTO ControllerMetadata (DeviceKey, CustomName, UpdatedAtUtc)
            VALUES (@key, @name, @updatedAt)
            ON CONFLICT(DeviceKey) DO UPDATE SET
                CustomName = @name,
                UpdatedAtUtc = @updatedAt;";
        cmd.Parameters.AddWithValue("@key", deviceKey);
        cmd.Parameters.AddWithValue("@name", customName.Trim());
        cmd.Parameters.AddWithValue("@updatedAt", DateTimeOffset.UtcNow.ToString("o"));

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllCustomNamesAsync()
    {
        var dict = new Dictionary<string, string>();
        using var conn = _context.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DeviceKey, CustomName FROM ControllerMetadata;";

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var key = reader.GetString(0);
            var name = reader.GetString(1);
            dict[key] = name;
        }

        return dict;
    }

    public async Task DeleteControllerProfileAsync(string deviceKey)
    {
        if (string.IsNullOrWhiteSpace(deviceKey)) return;

        using var conn = _context.CreateConnection();
        using var tx = conn.BeginTransaction();

        using var cmdMeta = conn.CreateCommand();
        cmdMeta.Transaction = tx;
        cmdMeta.CommandText = "DELETE FROM ControllerMetadata WHERE DeviceKey = @key;";
        cmdMeta.Parameters.AddWithValue("@key", deviceKey);
        await cmdMeta.ExecuteNonQueryAsync();

        using var cmdSessions = conn.CreateCommand();
        cmdSessions.Transaction = tx;
        cmdSessions.CommandText = "DELETE FROM Sessions WHERE DeviceKey = @key;";
        cmdSessions.Parameters.AddWithValue("@key", deviceKey);
        await cmdSessions.ExecuteNonQueryAsync();

        using var cmdBattery = conn.CreateCommand();
        cmdBattery.Transaction = tx;
        cmdBattery.CommandText = "DELETE FROM BatteryReadings WHERE DeviceKey = @key;";
        cmdBattery.Parameters.AddWithValue("@key", deviceKey);
        await cmdBattery.ExecuteNonQueryAsync();

        using var cmdChanges = conn.CreateCommand();
        cmdChanges.Transaction = tx;
        cmdChanges.CommandText = "DELETE FROM BatteryChanges WHERE DeviceKey = @key;";
        cmdChanges.Parameters.AddWithValue("@key", deviceKey);
        await cmdChanges.ExecuteNonQueryAsync();

        await tx.CommitAsync();
    }
}
