# 🎮 Gamepad Center — Evrensel Kontrol Merkezi

[![.NET 8](https://img.shields.io/badge/.NET-8.0%20(LTS)-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-0078D6?logo=windows&logoColor=white)](https://www.microsoft.com/windows)
[![UI Framework](https://img.shields.io/badge/UI-WPF%20%7C%20Fluent%20Design-005FB8)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)
[![Database](https://img.shields.io/badge/Database-SQLite%20(WAL%20Mode)-003B57?logo=sqlite&logoColor=white)](https://www.sqlite.org/)
[![Tests](https://img.shields.io/badge/Tests-60%2F60%20Passing-brightgreen?logo=xunit)](https://github.com/)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

**Gamepad Center**, Windows 10 ve 11 üzerinde Xbox Wireless Controller ve uyumlu tüm Bluetooth/USB oyun kollarının bağlantı oturumlarını, gerçek zamanlı kullanım sürelerini ve Bluetooth LE donanımsal pil durumunu arka planda otomatik olarak takip eden modern, hafif ve gizlilik odaklı bir masaüstü kontrol merkezidir.

> **English Summary:** Gamepad Center is a modern Windows desktop application built with C# and .NET 8 (WPF) that automatically tracks connection sessions, playtime statistics, and real-time Bluetooth LE GATT battery telemetry for Xbox Wireless Controllers and compatible gamepads, storing everything locally in a private SQLite database.

---

## ✨ Temel Özellikler

- **⚡ Otomatik Oturum & Süre Takibi:**
  - Kontrolcü bilgisayara bağlandığı anda oturumu otomatik başlatır; bağlantı koptuğunda veya kol kapatıldığında oturumu sonlandırıp net süreyi saniyesi saniyesine kaydeder.
  - Eşleştirilmiş fakat kapalı olan cihazları bağlı saymaz; Windows PnP (`DeviceWatcher`) ve WinRT Bluetooth LE sinyalleriyle gerçek bağlantı durumunu doğrular.
  - Ani kapanma veya elektrik kesintilerine karşı **otomatik kurtarma (Self-Healing)** motoruna sahiptir.

- **🔋 Donanımsal Bluetooth LE Pil Telemetrisi:**
  - Standart Bluetooth GATT Pil Servisi (`0x180F`) üzerinden kontrolcünün gerçek donanımsal pil yüzdesini kablosuz olarak okur.
  - Pil seti değişimlerini otomatik algılar (örn: %15'ten %100'e ani sıçrama algılandığında kullanıcıya onay bildirimi sunar).
  - Pil döngü analizi yaparak tek bir pil setiyle kaç saat oynandığını ve pil başı ortalama verimliliği hesaplar.

- **📊 Kapsamlı Analizler & Canlı Grafikler:**
  - Bugün, bu hafta, bu ay ve bu yıla ait toplam oyun süreleri.
  - En uzun oturum rekoru, ortalama oturum süresi, en aktif gün ve en aktif saat dilimi analitiği.
  - Son 7 günlük oyun alışkanlığını gösteren dinamik vektör çizgi grafiği.

- **🏷️ Çoklu Kol Desteği & Özel Takma Adlar (Nicknames):**
  - Birden fazla oyun kolu kullanan oyuncular için her kola özel isim verme desteği (Örn: *"Anıl Xbox"*, *"Siyah Kol"*).
  - Sol kenar çubuğundaki **Global Kontrolcü Filtresi** sayesinde tek tıkla tüm ekranları seçilen kontrolcüye göre anında filtreleme.

- **🔔 Windows Bildirimleri & Sistem Tepsisi (System Tray):**
  - Kontrolcünüz bağlandığında veya bağlantısı kesildiğinde özel takma adıyla masaüstü bildirimi: *"🎮 Anıl Xbox bağlandı"* / *"Anıl Xbox bağlantısı kesildi"*.
  - Pencere kapatıldığında arka planda sistem tepsisinde (saatin yanında) sessizce ve minimum RAM tüketimiyle çalışmaya devam eder.
  - Windows açılışında otomatik başlatma seçeneği.

- **💾 CSV Dışa Aktarma & Oturum Yönetimi:**
  - Oturum geçmişini ve pil değişim kayıtlarını tek tıkla Excel/CSV formatında dışa aktarma.
  - Tek tek oturum silme veya geçmişi toplu temizleme (silinen kayıtlar istatistikleri canlı günceller).

- **🔒 %100 Yerel & Gizlilik Odaklı:**
  - İnternete veya harici bir sunucuya hiçbir telemetri veya veri gönderilmez.
  - Tüm kayıtlar kullanıcının kendi bilgisayarında `%LocalAppData%\ControllerHub\controllerhub.db` içinde SQLite WAL (Write-Ahead Logging) modunda yüksek performansla tutulur.

---

## 🏗️ Mimari ve Proje Yapısı

Proje, kurumsal standartlarda **Clean Architecture** ve **MVVM** tasarım kalıplarıyla geliştirilmiştir:

```text
controller-hub/
├── src/
│   ├── ControllerHub.Core/             # İş modelleri, domain mantığı, arayüzler (Interfaces)
│   │   ├── Interfaces/                 # ISessionRepository, IBatteryRepository, IDeviceWatcherService
│   │   ├── Models/                     # ControllerSession, BatteryReading, ControllerMetadata
│   │   └── Services/                   # BatteryCycleCalculator, BatteryChangeDetector
│   │
│   ├── ControllerHub.Infrastructure/   # Donanım API'leri, SQLite veritabanı, Windows entegrasyonu
│   │   ├── DeviceDetection/            # CompositeDeviceWatcher (PnP + WinRT BLE GATT + XInput)
│   │   ├── Export/                     # CsvExporter (RFC 4180 uyumlu dışa aktarım)
│   │   ├── Persistence/                # SqliteDatabaseContext, Repositories (WAL modu)
│   │   └── Services/                   # SessionTrackerService, WindowsStartupService
│   │
│   └── ControllerHub.App/              # WPF Kullanıcı Arayüzü (MVVM)
│       ├── Assets/                     # Uygulama ikonları, rozetler ve görseller
│       ├── Services/                   # Win32 SystemTrayManager (Shell_NotifyIconW)
│       ├── Themes/                     # Fluent Light tema fırçaları, buton & ComboBox stilleri
│       ├── ViewModels/                 # MainViewModel, DashboardVM, SessionsVM, BatteryVM...
│       └── Views/                      # DashboardView, SessionsView, BatteryView, SettingsView...
│
├── tests/
│   └── ControllerHub.Tests/            # xUnit & Moq birim testleri (60/60 test başarılı)
│       ├── BatteryCycleCalculatorTests.cs
│       ├── SqliteSessionRepositoryTests.cs
│       └── SessionTrackerServiceTests.cs
│
├── dist/                               # Taşınabilir tek-dosya (Single-File) derleme çıktıları
├── LICENSE                             # MIT Açık Kaynak Lisansı
└── README.md                           # Proje dokümantasyonu
```

---

## 🚀 Başlangıç ve Derleme

### Gereksinimler
- **İşletim Sistemi:** Windows 10 (Sürüm 19041+) veya Windows 11
- **Geliştirme Ortamı:** [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### 1. Depoyu Klonlayın
```bash
git clone https://github.com/kullanici-adiniz/controller-hub.git
cd controller-hub
```

### 2. Bağımlılıkları Geri Yükleyin ve Testleri Çalıştırın
```bash
dotnet restore
dotnet test
```
*(Tüm 60 birim testin yeşil yandığını teyit edin.)*

### 3. Geliştirme Modunda Çalıştırın
```bash
dotnet run --project src/ControllerHub.App/ControllerHub.App.csproj
```

### 4. Bağımsız Tek Dosya (Single-File) Release Sürümü Üretin
```bash
dotnet publish src/ControllerHub.App/ControllerHub.App.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist/GamepadCenter-v1.0.0-win-x64
```
Derleme tamamlandığında `dist/GamepadCenter-v1.0.0-win-x64/GamepadCenter.exe` dosyası doğrudan çift tıklanarak kuruluma gerek olmadan çalıştırılabilir.

---

## 🗄️ Veritabanı Yapılandırması (Zero-Config)

Kullanıcıların veya projeyi klonlayanların hiçbir veritabanı ayarı yapması gerekmez:
- Uygulama ilk kez açıldığında, o anki Windows kullanıcısının `%LocalAppData%\ControllerHub\` klasöründe otomatik olarak sıfırdan bir `controllerhub.db` dosyası oluşturur.
- Tablolar ve indeksler kod içerisindeki `CREATE TABLE IF NOT EXISTS` mekanizmasıyla milisaniyeler içinde hazır hale gelir.
- Kişisel oturumlar ve donanım kayıtları yalnızca kullanıcının kendi bilgisayarında saklanır; Git deposuna hiçbir zaman dahil edilmez.

---

## 🤝 Katkıda Bulunma

Katkılarınızı memnuniyetle kabul ediyoruz!
1. Bu depoyu çatallayın (Fork).
2. Yeni özelliğiniz için bir dal açın (`git checkout -b feature/harika-ozellik`).
3. Değişikliklerinizi commit edin (`git commit -m 'feat: harika özellik eklendi'`).
4. Dalınıza push yapın (`git push origin feature/harika-ozellik`).
5. Bir **Pull Request (PR)** oluşturun.

---

## 📄 Lisans

Bu proje [MIT Lisansı](LICENSE) kapsamında açık kaynak olarak lisanslanmıştır. İstediğiniz gibi kullanabilir, değiştirebilir ve geliştirebilirsiniz.
