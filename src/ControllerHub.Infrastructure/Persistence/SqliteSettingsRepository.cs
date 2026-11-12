using ControllerHub.Core.Interfaces;
using ControllerHub.Core.Models;
using Microsoft.Data.Sqlite;

namespace ControllerHub.Infrastructure.Persistence;

public class SqliteSettingsRepository : ISettingsRepository
{
    private readonly SqliteDatabaseContext _context;

    public SqliteSettingsRepository(SqliteDatabaseContext context)
    {
        _context = context;
    }

    public async Task<AppSettings> LoadSettingsAsync()
    {
        var settings = new AppSettings();
        using var conn = _context.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Key, Value FROM Settings;";

        using var reader = await cmd.ExecuteReaderAsync();
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
        {
            dict[reader.GetString(0)] = reader.GetString(1);
        }

        if (dict.TryGetValue(nameof(AppSettings.StartWithWindows), out var sww) && bool.TryParse(sww, out var bSww))
            settings.StartWithWindows = bSww;

        if (dict.TryGetValue(nameof(AppSettings.MinimizeToTray), out var mtt) && bool.TryParse(mtt, out var bMtt))
            settings.MinimizeToTray = bMtt;

        if (dict.TryGetValue(nameof(AppSettings.NotificationsEnabled), out var ne) && bool.TryParse(ne, out var bNe))
            settings.NotificationsEnabled = bNe;

        if (dict.TryGetValue(nameof(AppSettings.SelectedDeviceKey), out var sdk))
            settings.SelectedDeviceKey = sdk;

        if (dict.TryGetValue(nameof(AppSettings.Theme), out var theme))
            settings.Theme = theme;

        return settings;
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        using var conn = _context.CreateConnection();
        using var tx = conn.BeginTransaction();

        var pairs = new (string Key, string Value)[]
        {
            (nameof(AppSettings.StartWithWindows), settings.StartWithWindows.ToString()),
            (nameof(AppSettings.MinimizeToTray), settings.MinimizeToTray.ToString()),
            (nameof(AppSettings.NotificationsEnabled), settings.NotificationsEnabled.ToString()),
            (nameof(AppSettings.SelectedDeviceKey), settings.SelectedDeviceKey ?? string.Empty),
            (nameof(AppSettings.Theme), settings.Theme ?? "Light")
        };

        foreach (var (k, v) in pairs)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO Settings (Key, Value) VALUES (@k, @v)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;";
            cmd.Parameters.AddWithValue("@k", k);
            cmd.Parameters.AddWithValue("@v", v);
            await cmd.ExecuteNonQueryAsync();
        }

        tx.Commit();
    }
}
