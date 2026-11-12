# Controller Hub

Windows 11 üzerinde Microsoft Xbox Wireless Controller (9. Nesil / Model 1914) ve uyumlu denetleyicilerin Bluetooth bağlantı oturumlarını, kullanım sürelerini ve pil durumunu otomatik takip eden yerel masaüstü kontrol merkezi.

---

## 🎯 Ürün Özellikleri

- **Bağlantı Süresi Takibi:** Kontrolcünün bilgisayara bağlı kaldığı net oturum süresini otomatik başlatır ve bitirir.
- **Güvenilir Bağlantı Algılama:** Eşleştirilmiş ama kapalı olan cihazları bağlı saymaz; Windows PnP ve WinRT Bluetooth LE sinyalleriyle gerçek zamanlı doğrular.
- **Pil Yönetimi:** Bluetooth LE GATT Pil Servisi (`0x180F`) üzerinden gerçek donanımsal pil seviyesini okur; manuel değişim ve seviye kaydı imkânı sunar.
- **Kullanım Analitiği:** Günlük, haftalık, aylık ve yıllık bağlantı süreleri, ortalama oturum ve rekor süre analizleri.
- **Fluent Tasarım:** Modern, sade, beyaz temalı (#F7F8FA arka plan) profesyonel gösterge paneli (gaming/RGB stili içermez).
- **Yerel & Güvenli:** Tüm veriler kullanıcının bilgisayarında yerel SQLite veritabanında saklanır.

---

## 🛠 Teknoloji Yığını

- **Platform:** C# / .NET 8 (LTS) & WPF (Windows Presentation Foundation)
- **Mimari:** MVVM (`CommunityToolkit.Mvvm`)
- **Veritabanı:** SQLite (`Microsoft.Data.Sqlite`)
- **Donanım API'leri:** Windows PnP (`DeviceWatcher`), WinRT Bluetooth LE (`Windows.Devices.Bluetooth`), Win32 XInput
- **Grafikler:** LiveCharts2 (WPF)

---

## 📊 Proje Durumu

| Aşama | Başlık | Durum |
| :---: | :--- | :---: |
| **0** | **Keşif ve Teknik Doğrulama** | **Tamamlandı** ✅ |
| **1** | **Proje İskeleti ve Tasarım Prototipi** | **Tamamlandı** ✅ |
| **2** | **Gerçek Xbox Bağlantı Algılama Prototipi** | **Tamamlandı** ✅ |
| **3** | **Oturum Kaydı ve Zaman Hesaplama** | **Tamamlandı** ✅ |
| **4** | **Dashboard'u Gerçek Veriye Bağlama** | **Tamamlandı** ✅ |
| 5 | Pil Yönetimi (Manuel İlk Sürüm) | Beklemede |
| 6 | Analizler ve Raporlama | Beklemede |
| 7 | Pil Telemetrisi Entegrasyonu | Beklemede |
| 8 | Sistem Tepsisi ve Windows Başlangıcı | Beklemede |
| 9 | Paketleme ve Yayın Hazırlığı | Beklemede |

---

## 🚀 Aşama 0 Doğrulama Bulguları

- **.NET 8 SDK (8.0.428)** kullanıcı ortamına kuruldu ve yapılandırıldı.
- `tools/detect_controller.ps1` ve `tools/GattProbe` araçlarıyla gerçek Xbox Wireless Controller (Model 1914, VID `045E`, PID `0B13`) üzerinde test yapıldı:
  - Donanım dinamik olarak keşfedildi (sabit MAC adresi bağımlılığı yok).
  - Bluetooth GATT 0x180F servisinden **%100** canlı pil seviyesi okundu.
  - PnP `Present: True/False` ve WinRT `IsEnabled: True/False` ayrımları başarıyla doğrulandı.
