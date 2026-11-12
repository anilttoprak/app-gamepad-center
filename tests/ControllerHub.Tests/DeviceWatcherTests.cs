using System;
using System.Collections.Generic;
using ControllerHub.Core.Models;
using ControllerHub.Core.Policies;
using Xunit;

namespace ControllerHub.Tests;

public class DeviceWatcherTests
{
    [Fact]
    public void DebouncePolicy_MustProduceExactlyOneStartAndOneEnd_AcrossTenConnectionCycles()
    {
        // Kabul Kriteri: En az 10 bağlanma/kopma döngüsünde yanlış oturum açma veya çift oturum olmadan doğru algılama
        var policy = new ConnectionDebouncePolicy(
            disconnectGracePeriod: TimeSpan.FromSeconds(4),
            connectDebouncePeriod: TimeSpan.FromMilliseconds(500));

        var startedCount = 0;
        var endedCount = 0;
        var timeline = DateTimeOffset.UtcNow;

        for (int cycle = 1; cycle <= 10; cycle++)
        {
            // 1. Cihaz açıldı (Bağlantı sinyali geldi)
            var (s1, a1) = policy.Evaluate(true, timeline);
            Assert.Equal(DeviceConnectionState.Connecting, s1);
            Assert.Equal(DebounceAction.None, a1);

            // 500 ms sonra bağlantı kararlı hale geldi -> Oturum başlamalı
            timeline = timeline.AddMilliseconds(600);
            var (s2, a2) = policy.Evaluate(true, timeline);
            Assert.Equal(DeviceConnectionState.Connected, s2);
            Assert.Equal(DebounceAction.SessionStarted, a2);
            startedCount++;

            // Oyun/kullanım süresi (örn: 30 dakika bağlı kaldı)
            timeline = timeline.AddMinutes(30);
            var (s3, a3) = policy.Evaluate(true, timeline);
            Assert.Equal(DeviceConnectionState.Connected, s3);
            Assert.Equal(DebounceAction.None, a3);

            // Mikro sinyal dalgalanması (1 saniye koptu, 1 saniye sonra geri geldi) -> Oturum BÖLÜNMEMELİ!
            timeline = timeline.AddSeconds(1);
            var (sFlap1, aFlap1) = policy.Evaluate(false, timeline);
            Assert.Equal(DeviceConnectionState.Disconnecting, sFlap1);
            Assert.Equal(DebounceAction.None, aFlap1);

            timeline = timeline.AddSeconds(1);
            var (sFlap2, aFlap2) = policy.Evaluate(true, timeline);
            Assert.Equal(DeviceConnectionState.Connected, sFlap2);
            Assert.Equal(DebounceAction.None, aFlap2);

            // 2. Kontrolcü kapatıldı (Kalıcı kopma)
            timeline = timeline.AddMinutes(15);
            var (s4, a4) = policy.Evaluate(false, timeline);
            Assert.Equal(DeviceConnectionState.Disconnecting, s4);
            Assert.Equal(DebounceAction.None, a4);

            // Grace period (4 saniye) doldu -> Oturum güvenli şekilde bitmeli
            timeline = timeline.AddSeconds(4.5);
            var (s5, a5) = policy.Evaluate(false, timeline);
            Assert.Equal(DeviceConnectionState.Disconnected, s5);
            Assert.Equal(DebounceAction.SessionEnded, a5);
            endedCount++;

            // Kapalı durumda bekleme (örn: 5 dakika)
            timeline = timeline.AddMinutes(5);
            var (s6, a6) = policy.Evaluate(false, timeline);
            Assert.Equal(DeviceConnectionState.Disconnected, s6);
            Assert.Equal(DebounceAction.None, a6);
        }

        // Doğrulama: 10 döngü boyunca kesinlikle 10 Başlangıç ve 10 Bitiş üretilmeli; çift veya sahte oturum olmamalı!
        Assert.Equal(10, startedCount);
        Assert.Equal(10, endedCount);
    }
}
