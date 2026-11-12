using System;
using System.IO;
using System.Threading.Tasks;
using ControllerHub.Core.Models;
using ControllerHub.Infrastructure.Persistence;
using Xunit;

namespace ControllerHub.Tests;

public class SqliteSessionRepositoryTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly SqliteDatabaseContext _context;
    private readonly SqliteSessionRepository _repo;

    public SqliteSessionRepositoryTests()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), $"controllerhub_test_{Guid.NewGuid():N}.db");
        _context = new SqliteDatabaseContext(_tempDbPath);
        _repo = new SqliteSessionRepository(_context);
    }

    [Fact]
    public async Task StartAndEndSession_ShouldPersistCorrectly()
    {
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-30);
        var session = await _repo.StartSessionAsync("XBOX_TEST", startedAt);

        Assert.True(session.Id > 0);
        Assert.True(session.IsActive);
        Assert.Null(session.EndedAtUtc);

        var active = await _repo.GetActiveSessionAsync("XBOX_TEST");
        Assert.NotNull(active);
        Assert.Equal(session.Id, active.Id);

        var endedAt = DateTimeOffset.UtcNow;
        var ended = await _repo.EndSessionAsync("XBOX_TEST", endedAt, SessionEndReason.Disconnected);

        Assert.NotNull(ended);
        Assert.False(ended.IsActive);
        Assert.NotNull(ended.EndedAtUtc);
        Assert.Equal(SessionEndReason.Disconnected, ended.EndReason);
        Assert.InRange(ended.DurationSeconds, 1790, 1810); // ~1800 sn

        var activeAfterEnd = await _repo.GetActiveSessionAsync("XBOX_TEST");
        Assert.Null(activeAfterEnd);
    }

    [Fact]
    public async Task StartSession_ShouldRecoverPreviousOrphanedSession_IfAlreadyOpen()
    {
        var t1 = DateTimeOffset.UtcNow.AddHours(-2);
        var first = await _repo.StartSessionAsync("XBOX_TEST", t1);

        // Kapatılmadan ikinci bir oturum başlatıldı
        var t2 = DateTimeOffset.UtcNow.AddHours(-1);
        var second = await _repo.StartSessionAsync("XBOX_TEST", t2);

        Assert.NotEqual(first.Id, second.Id);

        var recent = await _repo.GetRecentSessionsAsync(10);
        Assert.Equal(2, recent.Count);

        // İlki otomatik olarak 'Recovery' ile kapatılmış olmalı (FR-05)
        var recoveredFirst = recent[1];
        Assert.Equal(SessionEndReason.Recovery, recoveredFirst.EndReason);
        Assert.NotNull(recoveredFirst.EndedAtUtc);
    }

    [Fact]
    public async Task RecoverOrphanedSessionsAsync_ShouldCloseAllUnfinishedSessions()
    {
        await _repo.StartSessionAsync("XBOX_A", DateTimeOffset.UtcNow.AddMinutes(-10));
        await _repo.StartSessionAsync("XBOX_B", DateTimeOffset.UtcNow.AddMinutes(-5));

        var recoveryTime = DateTimeOffset.UtcNow;
        var count = await _repo.RecoverOrphanedSessionsAsync(recoveryTime);

        Assert.True(count >= 2);

        var activeA = await _repo.GetActiveSessionAsync("XBOX_A");
        var activeB = await _repo.GetActiveSessionAsync("XBOX_B");

        Assert.Null(activeA);
        Assert.Null(activeB);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_tempDbPath))
            {
                File.Delete(_tempDbPath);
            }
        }
        catch { }
    }
}
