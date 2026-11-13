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
}
