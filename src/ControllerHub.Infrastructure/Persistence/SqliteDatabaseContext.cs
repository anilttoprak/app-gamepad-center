using Microsoft.Data.Sqlite;

namespace ControllerHub.Infrastructure.Persistence;

public class SqliteDatabaseContext
{
    private readonly string _connectionString;

    public string DatabasePath { get; }

    public SqliteDatabaseContext(string? databasePath = null)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ControllerHub");
            Directory.CreateDirectory(appDataDir);
            DatabasePath = Path.Combine(appDataDir, "controllerhub.db");
        }
        else
        {
            DatabasePath = databasePath;
            var dir = Path.GetDirectoryName(DatabasePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        InitializeDatabase();
    }

    public SqliteConnection CreateConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    private void InitializeDatabase()
    {
        using var conn = CreateConnection();

        using var walCmd = conn.CreateCommand();
        walCmd.CommandText = @"
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA foreign_keys = ON;";
        walCmd.ExecuteNonQuery();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Sessions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DeviceKey TEXT NOT NULL,
                StartedAtUtc TEXT NOT NULL,
                EndedAtUtc TEXT,
                DurationSeconds INTEGER NOT NULL DEFAULT 0,
                EndReason TEXT NOT NULL DEFAULT 'Unknown',
                CreatedAtUtc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_Sessions_DeviceKey ON Sessions(DeviceKey);
            CREATE INDEX IF NOT EXISTS IX_Sessions_StartedAtUtc ON Sessions(StartedAtUtc);
            CREATE INDEX IF NOT EXISTS IX_Sessions_EndedAtUtc ON Sessions(EndedAtUtc);

            CREATE TABLE IF NOT EXISTS BatteryReadings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DeviceKey TEXT NOT NULL,
                RecordedAtUtc TEXT NOT NULL,
                Percentage INTEGER,
                Source TEXT NOT NULL,
                Confidence TEXT NOT NULL,
                Note TEXT
            );

            CREATE INDEX IF NOT EXISTS IX_BatteryReadings_DeviceKey ON BatteryReadings(DeviceKey);
            CREATE INDEX IF NOT EXISTS IX_BatteryReadings_RecordedAtUtc ON BatteryReadings(RecordedAtUtc);

            CREATE TABLE IF NOT EXISTS BatteryChanges (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DeviceKey TEXT NOT NULL,
                ChangedAtUtc TEXT NOT NULL,
                PreviousPercentage INTEGER,
                NewPercentage INTEGER,
                Note TEXT,
                ChangeType TEXT NOT NULL DEFAULT 'Manual'
            );

            CREATE TABLE IF NOT EXISTS Settings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ControllerMetadata (
                DeviceKey TEXT PRIMARY KEY,
                CustomName TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );";
        cmd.ExecuteNonQuery();
    }
}
