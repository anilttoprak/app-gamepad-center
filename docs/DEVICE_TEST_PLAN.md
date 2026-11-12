# Controller Hub — Cihaz Algılama ve Doğrulama Test Planı (Aşama 0)

Bu belge, **Microsoft Xbox Wireless Controller (9. Nesil / Model 1914)** ve uyumlu Xbox denetleyicilerinin Windows 11 üzerinde bağlantı, kopma, oturum takibi ve pil seviyesi sinyallerinin doğrulanması için hazırlanmış test ve mimari strateji dokümanıdır.

---

## 1. Donanım ve Sinyal Analizi

### 1.1 Hedef Cihaz Kimlikleri (PnP & Bluetooth)
Xbox Wireless Controller Bluetooth üzerinden bağlandığında Windows PnP katmanında şu hiyerarşiyi oluşturur:

| Katman / Protokol | Donanım Kimliği / Sınıf | Beklenen Değer | Rolü |
| :--- | :--- | :--- | :--- |
| **Üst Düğüm (Parent)** | `BTHLE\DEV_<MAC>` | Bluetooth LE Ana Aygıtı | Kontrolcünün fiziksel radyo bağlantısı |
| **Giriş Cihazı (XInput)** | `BTHLEDEVICE\{00001812-...}_DEV_VID&02045E_PID&0B13` | Bluetooth LE XINPUT HID Aygıtı | Windows oyun girdileri ve XInput köprüsü |
| **Pil Servisi (GATT)** | `BTHLEDEVICE\{0000180F-...}` | GATT Service 0x180F | Donanımsal Bluetooth pil telemetrisi |
| **Oyun Denetleyicisi** | `HID\{00001812-...}&DEV&VID_045E&PID_0B13` | HID Oyun Denetleyicisi | DirectInput / Raw Input katmanı |

> **Önemli Kural:** Windows'ta bir cihazın Bluetooth ayarlarında "Eşleştirildi (Paired)" olması, o an **bağlı** olduğu anlamına gelmez. Sadece eşleşme listesine bakarak oturum başlatılamaz.

---

## 2. Bağlantı Algılama Stratejisi (ADR — Mimari Karar)

### 2.1 Değerlendirilen Yöntemler ve Karşılaştırma

1. **Yalnızca XInput Polling (`XInputGetState`):**
   - *Sorun:* XInput kontrolcünün bağlı olup olmadığını (`ERROR_SUCCESS` vs `ERROR_DEVICE_NOT_CONNECTED`) bildirir ancak kontrolcünün hangi bağlantı türüyle (USB mi, Xbox Wireless Adapter mı, Bluetooth mu) ve hangi cihaz kimliğiyle bağlı olduğunu söylemez. Ayrıca 0-3 arası slot indeksi verir.
2. **Yalnızca PnP Registry / WMI Sorgusu:**
   - *Sorun:* WMI sorguları ağırdır, saniyede bir çalıştırmak CPU tüketir ve gecikmelidir.
3. **Seçilen Strateji: Hibrit Windows PnP Event + XInput Doğrulama:**
   - **Adım 1 (Event Dinleme):** `Windows.Devices.Enumeration.DeviceWatcher` veya Win32 `RegisterDeviceNotification` (`WM_DEVICECHANGE`) ile işletim sistemi düzeyinde donanım takılma/çıkarılma event'leri sıfır CPU tüketimiyle dinlenir.
   - **Adım 2 (Filtreleme & Tanıma):** Gelen olay Microsoft Vendor ID (`045E`) ve Xbox Controller Product ID ailesi (`0B13`, `02EA`, vb.) ile eşleştirilir.
   - **Adım 3 (Debounce & Grace Period):** Kontrolcü açılırken veya kapanırken Bluetooth sinyallerinde 2-3 saniyelik geçici kesintiler yaşanabilir. Oturumun bölünmemesi için **4 saniyelik debounce (onaylama) penceresi** uygulanır.
   - **Adım 4 (Durum Makinesi):**
     ```text
     [Disconnected] ---> (Cihaz Bildirimi Geldi) ---> [Connecting]
                                                            |
                                                 (4 sn doğrulandı)
                                                            v
     [Disconnected] <--- (4 sn sinyal yok) <--- [Disconnecting] <--- [Connected]
     ```

---

## 3. Pil Telemetrisi Doğrulama Planı

9. Nesil Xbox kontrolcüsü standart Bluetooth LE Battery Service (`0x180F`) ve Battery Level Characteristic (`0x2A19`) destekler.

- **Kaynak 1 (WinRT BLE GATT):** Windows 10/11 WinRT `GattCharacteristic.ReadValueAsync()` ile %0-%100 arası doğrudan pil değeri sorgulanır.
- **Kaynak 2 (XInput Battery):** `XInputGetBatteryInformation` API'si üzerinden kaba pil durumu (`BATTERY_LEVEL_FULL`, `MEDIUM`, `LOW`, `EMPTY`) alınır.
- **Yedek Mekanizma:** API'den okuma başarısız olursa veya yetki kısıtı varsa arayüzde "Pil bilgisi alınamıyor" gösterilir ve kullanıcı dilediğinde **manuel yüzde** veya **"Pil Değiştirdim"** kaydı girebilir.

---

## 4. Gerçek Cihaz Test Matrisi

Kullanıcı yeni kontrolcüsünü bağladığında şu senaryolar test edilecektir:

| Test No | Test Senaryosu | Beklenen Davranış | Doğrulama Yöntemi |
| :---: | :--- | :--- | :--- |
| **TC-01** | Kontrolcü kapalıyken uygulamanın açılması | Durum: `Disconnected`. Oturum açılmaz. | PnP `Present: False`, XInput boş |
| **TC-02** | Kontrolcünün açılması (Xbox logosuna basılması) | 3-5 sn içinde durum `Connected` olur. Oturum başlar. | PnP Event tetiklenir, timestamp kaydedilir |
| **TC-03** | Kontrolcünün elle kapatılması (Xbox tuşuna basılı tutulması) | 4 sn debounce sonrası durum `Disconnected` olur. Oturum kapanır. | PnP `Present: False`, bitiş timestamp'i yazılır |
| **TC-04** | Windows Bluetooth'unun kapatılıp açılması | Oturum kapanır; Bluetooth açılıp kontrolcü bağlanınca yeni oturum başlar. | 2 ayrı oturum kaydı oluşmalı |
| **TC-05** | Mikro kopmalar (sinyal zayıflığı / 2 sn gecikme) | Debounce sayesinde oturum bölünmez, süre devam eder. | Zaman damgaları kesintisiz kalır |
| **TC-06** | Kontrolcü bağlıyken uygulamanın kapatılıp açılması | Kapanış anı kaydedilir veya açılışta recovery politikasıyla kapatılıp yeni oturum başlar. | Çift oturum oluşmaz |
| **TC-07** | Pil seviyesi okuma | Pil yüzdesi okunabiliyorsa kaynak belirtilir (ör. WinRT GATT %85). | Log çıktısında doğrulanır |
