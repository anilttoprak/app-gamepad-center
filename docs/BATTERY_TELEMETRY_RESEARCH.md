# Controller Hub — Pil Telemetrisi Araştırması ve Otomasyon Raporu (Aşama 7)

Bu doküman, Windows 11 ortamında **Microsoft Xbox Wireless Controller 9. Nesil (Model 1914, VID: 045E, PID: 0B13)** için pil telemetrisi kaynaklarını, doğruluk/hassasiyet seviyelerini, Windows önbellek risklerini ve otomatik pil değişimi tahmin politikasını belgeler.

---

## 1. Windows API'leri Telemetri Karşılaştırması

| API / Kaynak | Erişim Yolu | Hassasiyet / Doğruluk | Güvenilirlik Seviyesi | Kısıtlar ve Notlar |
| :--- | :--- | :--- | :--- | :--- |
| **Bluetooth LE GATT Battery Service (Seçilen Yöntem)** | WinRT `Windows.Devices.Bluetooth.GenericAttributeProfile`<br>Servis: `0x180F`<br>Karakteristik: `0x2A19` | **Tam Hassasiyet (%0 - %100, 1 bayt)** | **Yüksek (Exact)** | Yalnızca kontrolcü Bluetooth LE ile fiilen bağlıyken taze okunabilir. Kontrolcü kapandığında Windows GATT önbelleğinde eski değer kalır; bu yüzden `BatteryFreshnessPolicy` ile filtrelenir. Gerçek cihazda %100 değeri başarıyla okundu. |
| **XInput API** | `XInputGetBatteryInformation` | **Kaba (4 Kademeli)**<br>`EMPTY`, `LOW`, `MEDIUM`, `FULL` | **Orta (Coarse)** | Yüzde (%1'lik) değer vermez, yalnızca 4 genel durum bildirir. Pil ömrü takibi ve tüketim oranı hesaplama için yetersizdir. |
| **Windows Gaming / GameInput API** | Windows 11 SDK `IGameInputDevice` | Kaba / Durumsal | Orta | XInput benzeri seviye döndürür; BLE GATT kadar granüler yüzde sağlamaz. |
| **USB Kablolu Bağlantı** | USB PnP Aygıtı | Harici Güç (Bus Powered) | N/A | Kontrolcü kabloyla bağlandığında pil devresi by-pass edilir veya harici güç raporlanır. |
| **Kullanıcı Manuel Girişi** | Controller Hub UI Formu | %0 - %100 (Kullanıcı beyanı) | **Yüksek (Exact - Manuel)** | Kullanıcının şarj edilebilir pilleri taktığında veya ölçüm yaptığında girdiği kesin değerdir. |

---

## 2. Windows Bluetooth Önbelleği (Stale Cache) Riski ve Çözümü

### Sorun Tanımı:
Windows 11 Bluetooth yığını, eşleştirilmiş bir cihaz kapandığında GATT servislerini ve karakteristik değerlerini hafızada tutmaya devam edebilir. Kontrolcü kapalıyken bile `ReadValueAsync()` çağrısı bazen son okunan değeri hata vermeden döndürebilir.

### Uygulanan Güvenlik Kalkanı (`BatteryFreshnessPolicy`):
1. `isDeviceConnected == false` iken gelen tüm GATT pil okumaları istisnasız reddedilir (`IsStale = true`).
2. Pil seviyesi yalnızca `WindowsBluetoothDeviceWatcher` tarafından 4 saniyelik debounce onayından geçmiş **aktif `Connected` durumunda** okunur ve SQLite'a yazılır.
3. Kontrolcü kapandığında arayüzdeki pil durumu anında dondurulur veya son doğrulanmış zaman damgasıyla açıkça gösterilir.

---

## 3. Otomatik Pil Değişimi Tahmini (Auto-Change Proposal Engine)

### Felsefe ve Güvenlik İlkesi:
> **Kabul Kriteri:** "Gerçek cihazda doğrulanmayan pil yüzdesi veya değişim olayı otomatik gerçekmiş gibi kaydedilmez."

Kullanıcının pillerini ne zaman değiştirdiğini tahmin etmek için pil yüzdesindeki ani sıçramalar izlenir. Ancak bu işlem **asla otomatik olarak veritabanına kesin kayıt yazmaz**.

### Yanlış Pozitif (False Positive) Riskleri ve Yönetimi (`BatteryChangeDetector`):
1. **Voltaj Dinlenme Toparlanması (Resting Voltage Rise):**
   - Kontrolcü bir süre kullanılmadığında pil kimyasından ötürü voltaj hafif yükselebilir (örn: %15'ten %20'ye).
   - **Kural:** `delta < %40` olan artışlar kesinlikle elenir; öneri üretilmez.
2. **Kapanma ve Tekrar Bağlanma:**
   - Aynı pil setiyle yeniden bağlanıldığında seviye aynı veya benzer kalır.
   - **Kural:** `newPercentage <= previousPercentage` veya küçük farklarda öneri üretilmez.
3. **İlk Çalıştırma / Kalibrasyon:**
   - Uygulama ilk kez açıldığında kontrolcü %100 görünebilir. Önceki geçmiş yoksa bu yeni bir değişim değil, cihazın sisteme tanıtılmasıdır.
   - **Kural:** `previousPercentage == null` ise öneri üretilmez.
4. **Yetersiz Yeni Seviye:**
   - %5'ten %40'a çıkan bir değer (örn. bitmiş başka bir pil takılması) yeni bir tam set sayılmaz.
   - **Kural:** `newPercentage >= %75` şartı aranır.

---

## 4. Kullanıcı Arayüzü ve Onay Akışı

1. **Öneri Banner'ı:**
   - Bir değişim şüphesi doğduğunda (`%15 ➔ %100`), `BatteryView` ve `DashboardView` üzerinde kibar ve net bir bildirim belirir:
     > *"Olası Pil Değişimi Algılandı: Pil seviyesinde %15'ten %100'e ani artış algılandı. Yeni pil seti takılmış olabilir."*
2. **Kullanıcı Kararı:**
   - **[✓ Evet, Kaydet]:** Kullanıcı onaylarsa `BatteryChanges` tablosuna `ChangeType = "Otomatik (Onaylandı)"` türünde kalıcı kayıt eklenir.
   - **[✕ Yoksay]:** Kullanıcı reddederse bildirim kapatılır; veritabanına **hiçbir satır yazılmaz** ve veri bütünlüğü korunur.
3. **Telemetri Şeffaflığı:**
   - Arayüzde pil yüzdesinin yanında veri kaynağı (`Bluetooth GATT 0x180F - Donanımsal` veya `Kullanıcı Girişi`) ve hassasiyet derecesi (`Tam Hassasiyet %1`) açıkça gösterilir.
