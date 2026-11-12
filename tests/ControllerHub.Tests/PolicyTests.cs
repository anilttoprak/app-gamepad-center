using System;
using ControllerHub.Core.Models;
using ControllerHub.Core.Policies;
using Xunit;

namespace ControllerHub.Tests;

public class PolicyTests
{
    [Fact]
    public void ConnectionDebouncePolicy_ShouldNotDisconnect_WhenTemporarySignalLossWithinGracePeriod()
    {
        // 4 saniyelik grace period
        var policy = new ConnectionDebouncePolicy(TimeSpan.FromSeconds(4), TimeSpan.FromMilliseconds(100));
        var t0 = DateTimeOffset.UtcNow;

        // 1. Cihaz bağlandı
        var r1 = policy.Evaluate(true, t0);
        var r2 = policy.Evaluate(true, t0.AddMilliseconds(200));
        Assert.Equal(DeviceConnectionState.Connected, r2.State);
        Assert.Equal(DebounceAction.SessionStarted, r2.Action);

        // 2. Sinyal aniden 1 saniyeliğine kayboldu (mikro kesinti)
        var r3 = policy.Evaluate(false, t0.AddSeconds(1));
        Assert.Equal(DeviceConnectionState.Disconnecting, r3.State);
        Assert.Equal(DebounceAction.None, r3.Action); // Oturum bitirilmedi!

        // 3. Sinyal 2 saniye sonra geri geldi (Grace period içinde)
        var r4 = policy.Evaluate(true, t0.AddSeconds(2));
        Assert.Equal(DeviceConnectionState.Connected, r4.State);
        Assert.Equal(DebounceAction.None, r4.Action); // Oturum bölünmedi, kesintisiz devam ediyor!
    }

    [Fact]
    public void ConnectionDebouncePolicy_ShouldDisconnectAndEndSession_WhenSignalLossExceedsGracePeriod()
    {
        var policy = new ConnectionDebouncePolicy(TimeSpan.FromSeconds(4), TimeSpan.FromMilliseconds(100));
        var t0 = DateTimeOffset.UtcNow;

        // Bağlandı
        policy.Evaluate(true, t0);
        policy.Evaluate(true, t0.AddMilliseconds(200));

        // Sinyal koptu
        policy.Evaluate(false, t0.AddSeconds(1));

        // 4 saniyeden fazla süre geçti (t0 + 5.5s)
        var finalResult = policy.Evaluate(false, t0.AddSeconds(5.5));
        Assert.Equal(DeviceConnectionState.Disconnected, finalResult.State);
        Assert.Equal(DebounceAction.SessionEnded, finalResult.Action); // Oturum güvenli şekilde kapatıldı!
    }

    [Fact]
    public void BatteryFreshnessPolicy_ShouldRejectReading_WhenDeviceIsDisconnected()
    {
        var policy = new BatteryFreshnessPolicy();
        var now = DateTimeOffset.UtcNow;

        // Cihaz bağlı değilken Windows önbelleğinden %100 geldi
        var result = policy.ValidateReading(isDeviceConnected: false, rawPercentage: 100, nowUtc: now);

        Assert.False(result.IsValid);
        Assert.True(result.IsStale);
        Assert.NotNull(result.RejectionReason);
    }

    [Fact]
    public void BatteryFreshnessPolicy_ShouldAcceptReading_WhenDeviceIsConnectedAndValueValid()
    {
        var policy = new BatteryFreshnessPolicy();
        var now = DateTimeOffset.UtcNow;

        // Cihaz bağlıyken gelen gerçek %85 pil okuması
        var result = policy.ValidateReading(isDeviceConnected: true, rawPercentage: 85, nowUtc: now);

        Assert.True(result.IsValid);
        Assert.Equal(85, result.Percentage);
        Assert.False(result.IsStale);
    }
}
