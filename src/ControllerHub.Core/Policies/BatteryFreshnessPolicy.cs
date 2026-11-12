using ControllerHub.Core.Models;

namespace ControllerHub.Core.Policies;

/// <summary>
/// Windows'un Bluetooth cihazı kapandığında önbellekte tuttuğu eski/hatalı GATT pil değerlerini
/// reddeden ve pil okumasının yalnızca cihaz fiilen bağlıyken kabul edilmesini garanti eden politika.
/// </summary>
public class BatteryFreshnessPolicy
{
    private readonly TimeSpan _maxStaleness;

    public BatteryFreshnessPolicy(TimeSpan? maxStaleness = null)
    {
        _maxStaleness = maxStaleness ?? TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Gelen pil okumasının geçerliliğini kontrol eder.
    /// </summary>
    /// <param name="isDeviceConnected">Cihazın o anda doğrulanmış olarak bağlı olup olmadığı</param>
    /// <param name="rawPercentage">Okunan pil yüzdesi (0-100)</param>
    /// <param name="nowUtc">Okuma anındaki zaman damgası</param>
    /// <param name="lastKnownReading">Varsa önceki son bilinen okuma</param>
    /// <returns>Onaylanan pil okuması veya geçersizlik durumu</returns>
    public BatteryValidationResult ValidateReading(
        bool isDeviceConnected,
        int? rawPercentage,
        DateTimeOffset nowUtc,
        BatteryReading? lastKnownReading = null)
    {
        if (!isDeviceConnected)
        {
            // Cihaz bağlı değilken gelen önbellek değerlerini KESİNLİKLE reddet!
            return new BatteryValidationResult(
                IsValid: false,
                Percentage: lastKnownReading?.Percentage,
                IsStale: true,
                RejectionReason: "Cihaz bağlı değilken Windows önbelleğinden gelen pil okumaları reddedildi.");
        }

        if (!rawPercentage.HasValue || rawPercentage < 0 || rawPercentage > 100)
        {
            return new BatteryValidationResult(
                IsValid: false,
                Percentage: null,
                IsStale: true,
                RejectionReason: "Pil yüzdesi 0-100 aralığında geçerli bir değer değil.");
        }

        // Cihaz aktif ve değer geçerli
        return new BatteryValidationResult(
            IsValid: true,
            Percentage: rawPercentage.Value,
            IsStale: false,
            RejectionReason: null);
    }
}

public record BatteryValidationResult(
    bool IsValid,
    int? Percentage,
    bool IsStale,
    string? RejectionReason);
