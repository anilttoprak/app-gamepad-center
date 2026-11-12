using ControllerHub.Core.Models;

namespace ControllerHub.Core.Services;

/// <summary>
/// Kontrolcü pil seviyesindeki ani sıçramaları inceleyerek olası bir pil seti değişimini tespit eden,
/// ancak bunu otomatik kesin kayıt olarak kaydetmeyip kullanıcı onayına sunan tahmin motoru.
/// </summary>
public record BatteryChangeProposal(
    string DeviceKey,
    int PreviousPercentage,
    int NewPercentage,
    DateTimeOffset DetectedAtUtc,
    string ReasonMessage);

public class BatteryChangeDetector
{
    private readonly int _minJumpThreshold;
    private readonly int _minTargetPercentage;

    /// <summary>
    /// Pil değişim tespit motorunu başlatır.
    /// </summary>
    /// <param name="minJumpThreshold">Pil değişimini tetiklemek için gereken minimum yüzde artışı (Varsayılan: %40)</param>
    /// <param name="minTargetPercentage">Yeni pilin ulaşması gereken minimum seviye (Varsayılan: %75)</param>
    public BatteryChangeDetector(int minJumpThreshold = 40, int minTargetPercentage = 75)
    {
        _minJumpThreshold = minJumpThreshold;
        _minTargetPercentage = minTargetPercentage;
    }

    /// <summary>
    /// Gelen yeni pil okumasını önceki durumla karşılaştırarak olası bir pil değişimi önerisi üretir.
    /// Asla veritabanına otomatik kesin kayıt yazmaz.
    /// </summary>
    public BatteryChangeProposal? Evaluate(
        string deviceKey,
        int? previousPercentage,
        int newPercentage,
        bool isDeviceConnected,
        DateTimeOffset nowUtc)
    {
        // 1. Cihaz bağlı değilken veya önbellekten gelen okumalarda öneri üretme
        if (!isDeviceConnected)
        {
            return null;
        }

        // 2. Önceki bilinen bir seviye yoksa (ilk açılış/ilk okuma), bu yeni bir değişim değil kalibrasyondur
        if (!previousPercentage.HasValue)
        {
            return null;
        }

        // 3. Pil seviyesi düşüyorsa veya aynıysa değişim değildir
        if (newPercentage <= previousPercentage.Value)
        {
            return null;
        }

        int delta = newPercentage - previousPercentage.Value;

        // 4. Küçük voltaj toparlanması / dalgalanmaları (örn: %20 -> %25) elenmeli
        if (delta < _minJumpThreshold)
        {
            return null;
        }

        // 5. Yeni pil seviyesi makul bir yeni pil eşiğinde olmalıdır (örn: en az %75)
        if (newPercentage < _minTargetPercentage)
        {
            return null;
        }

        // Koşullar sağlandı: Kullanıcı onayı için öneri üret
        string reason = $"Pil seviyesinde %{previousPercentage.Value} seviyesinden %{newPercentage} seviyesine ani artış (+%{delta}) algılandı. Yeni pil seti takılmış olabilir.";

        return new BatteryChangeProposal(
            DeviceKey: deviceKey,
            PreviousPercentage: previousPercentage.Value,
            NewPercentage: newPercentage,
            DetectedAtUtc: nowUtc,
            ReasonMessage: reason);
    }
}
