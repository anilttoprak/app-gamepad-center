using System;
using System.Collections.Generic;
using ControllerHub.Core.Models;
using ControllerHub.Core.Services;
using Xunit;

namespace ControllerHub.Tests;

public class SessionTimeCalculatorTests
{
    [Fact]
    public void CalculateDayDuration_ShouldAccuratelySplit_WhenSessionCrossesMidnight()
    {
        // Senaryo: Oturum Pazartesi 23:30'da başladı, Salı 01:30'da bitti (Toplam 2 saat = 7200 sn)
        var localTz = TimeZoneInfo.CreateCustomTimeZone("CustomLocal", TimeSpan.FromHours(3), "CustomLocal", "CustomLocal"); // UTC+3
        var monday = new DateOnly(2026, 11, 9);
        var tuesday = new DateOnly(2026, 11, 10);

        // Yerel saatle 23:30 -> UTC 20:30
        var startedUtc = new DateTimeOffset(2026, 11, 9, 20, 30, 0, TimeSpan.Zero);
        // Yerel saatle 01:30 -> UTC 22:30
        var endedUtc = new DateTimeOffset(2026, 11, 9, 22, 30, 0, TimeSpan.Zero);

        var session = new ControllerSession
        {
            Id = 1,
            DeviceKey = "XBOX_TEST",
            StartedAtUtc = startedUtc,
            EndedAtUtc = endedUtc,
            DurationSeconds = 7200,
            EndReason = SessionEndReason.Disconnected
        };

        var sessions = new List<ControllerSession> { session };
        var nowUtc = endedUtc.AddHours(1);

        // Pazartesi günü için hesaplanan süre: Tam 30 dakika (1800 sn) olmalı
        var mondayDuration = SessionTimeCalculator.CalculateDayDuration(sessions, null, monday, localTz, nowUtc);
        Assert.Equal(TimeSpan.FromMinutes(30), mondayDuration);

        // Salı günü için hesaplanan süre: Tam 1 saat 30 dakika (5400 sn) olmalı
        var tuesdayDuration = SessionTimeCalculator.CalculateDayDuration(sessions, null, tuesday, localTz, nowUtc);
        Assert.Equal(TimeSpan.FromMinutes(90), tuesdayDuration);

        // Toplam süre: 2 saat (120 dk) olmalı
        var totalDuration = SessionTimeCalculator.CalculateTotalDuration(sessions, null, nowUtc);
        Assert.Equal(TimeSpan.FromHours(2), totalDuration);
    }

    [Fact]
    public void CalculateTotalDuration_ShouldIncludeActiveSessionElapsedSeconds()
    {
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-45);
        var activeSession = new ControllerSession
        {
            Id = 2,
            DeviceKey = "XBOX_TEST",
            StartedAtUtc = t0,
            EndedAtUtc = null // Aktif
        };

        var completedSession = new ControllerSession
        {
            Id = 1,
            DeviceKey = "XBOX_TEST",
            StartedAtUtc = t0.AddHours(-2),
            EndedAtUtc = t0.AddHours(-1),
            DurationSeconds = 3600
        };

        var sessions = new List<ControllerSession> { completedSession };
        var nowUtc = DateTimeOffset.UtcNow;

        var total = SessionTimeCalculator.CalculateTotalDuration(sessions, activeSession, nowUtc);

        // 1 saat (3600 sn) + 45 dk (2700 sn) = 1 saat 45 dk (~6300 sn)
        Assert.InRange(total.TotalMinutes, 104, 106);
    }

    [Fact]
    public void FormatDurationText_ShouldFormatCorrectly()
    {
        Assert.Equal("45 sn", SessionTimeCalculator.FormatDurationText(TimeSpan.FromSeconds(45)));
        Assert.Equal("15 dk", SessionTimeCalculator.FormatDurationText(TimeSpan.FromMinutes(15)));
        Assert.Equal("2 sa 30 dk", SessionTimeCalculator.FormatDurationText(TimeSpan.FromHours(2.5)));
    }
}
