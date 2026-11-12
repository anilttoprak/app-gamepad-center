# Controller Hub --- Project Specification

**Sürüm:** 1.0\
**Hedef platform:** Windows 11\
**Hedef cihaz:** Microsoft Xbox Wireless Controller, 9. Nesil (Carbon
Black / Model 1914 ailesi)\
**Proje tipi:** Yerel Windows masaüstü uygulaması\
**Belge amacı:** Google Antigravity gibi bir AI coding agent'a proje
bağlamını vermek ve geliştirmeyi kontrollü aşamalara bölmek.

------------------------------------------------------------------------

## 0. Antigravity için çalışma talimatı --- önce bunu oku

Bu belge projenin ürün gereksinimlerini ve geliştirme planını tanımlar.
**Projeyi tek seferde tamamlamaya çalışma.** Kullanıcı aşamaları tek tek
onaylayacak.

### Zorunlu çalışma kuralları

1.  İlk yanıtında belgeyi özetle, önerdiğin teknik mimariyi açıkla ve
    yalnızca **Aşama 0: keşif ve proje iskeleti** için plan sun.
2.  Kullanıcı açıkça istemeden sonraki aşamaya geçme.
3.  Her aşamada:
    -   Önce yapılacak işleri ve değişecek dosyaları listele.
    -   Küçük, anlaşılır adımlarla uygula.
    -   Uygulamayı derle/çalıştır ve ilgili testleri yap.
    -   Sonuçları, test çıktısını ve bilinen sorunları raporla.
    -   Aşama sonunda kullanıcıdan onay iste.
4.  Çalışan özellikleri sebepsiz yere yeniden yazma. Mevcut kodu
    incelemeden dosyaları topluca değiştirme.
5.  Gerçek cihazda test edilmemiş Bluetooth veya pil özelliklerini
    çalışıyor gibi gösterme. Mock/demo verilerini her zaman açıkça
    etiketle.
6.  Bağlantı algılama başarısız olursa bunu gizleme. Windows cihaz
    listesinden alınan kanıtı, cihaz kimliğini ve algılama yöntemini
    raporla.
7.  Her aşamanın sonunda `README.md` ve bu belgedeki proje durumu
    bölümünü güncelle.
8.  Gereksiz bağımlılık ekleme. Her yeni NuGet paketi için neden
    gerektiğini açıkla.
9.  Veri kaybına yol açabilecek veritabanı değişikliklerinde migration
    kullan; mevcut kullanıcı verisini silme.
10. Uygulamanın arayüz dili Türkçe olsun. Kod, sınıf, değişken ve teknik
    dokümantasyon isimleri İngilizce olabilir.
11. İlk hedef çalışan ve güvenilir bağlantı süresi takibidir.
    Animasyonlar, aşırı efektler ve gereksiz görsel süsler öncelik
    değildir.
12. Kullanıcıdan bir bilgi gerekiyorsa tek seferde en önemli soruyu sor;
    varsayım yapıyorsan bunu açıkça belirt.

### Aşama geçiş kuralı

Bir aşamanın sonunda şu formatı kullan:

-   **Tamamlananlar**
-   **Değiştirilen dosyalar**
-   **Çalıştırılan testler ve sonuçları**
-   **Gerçek cihazda doğrulananlar**
-   **Bilinen sorunlar / riskler**
-   **Bir sonraki aşamada yapılacaklar**

Sonra kullanıcı onayı bekle. Kullanıcı onaylamadan sonraki aşamanın
kodunu yazmaya başlama.

------------------------------------------------------------------------

# 1. Ürün vizyonu

Controller Hub, Windows 11 bilgisayarında Xbox kablosuz kontrolcüsünün
Bluetooth bağlantı oturumlarını otomatik takip eden, kullanım
istatistiklerini gösteren ve pil kayıtlarını yöneten yerel bir masaüstü
uygulamasıdır.

Bu uygulama oyunların açık olup olmadığını takip etmeyi hedeflemez. Ana
ölçüm **kontrolcünün bilgisayara bağlı kaldığı süredir**.

Örnek: - Kontrolcü Bluetooth ile bağlanır: oturum başlar. - Kullanıcı
masaüstünde bekler veya oyun oynar: süre devam eder. - Kontrolcü
bağlantısı kesilir: oturum biter. - Bir sonraki bağlantıda yeni oturum
başlar. - Önceki oturumlar ve toplam süre korunur. - Bilgisayar yeniden
başlasa da veriler kaybolmaz.

> Ürün metinlerinde "oynadığın süre" ifadesi kullanılacaksa bunun gerçek
> oyun oynama süresi değil, kontrolcünün PC'ye bağlı kalma süresi olduğu
> açıkça belirtilmelidir.

## 1.1 Birincil hedefler

-   Xbox kontrolcüsünün Windows 11'e bağlantısını güvenilir biçimde
    algılamak.
-   Bağlantı başlangıç ve bitiş zamanlarını kaydetmek.
-   Oturum sürelerini ve toplam süreyi doğru hesaplamak.
-   Günlük, haftalık, aylık ve yıllık analizler sunmak.
-   Pil seviyesini ve manuel pil değişim kayıtlarını yönetmek.
-   Verileri yerel olarak, kalıcı ve güvenli biçimde saklamak.
-   Modern, sade, beyaz temalı bir dashboard sunmak.

## 1.2 İlk sürümde hedeflenmeyenler

-   Hangi oyunun oynandığını otomatik tespit etmek.
-   Gerçek oyun içi süreyi ölçmek.
-   Bulut hesabı veya çevrimiçi senkronizasyon.
-   Mobil uygulama.
-   Çoklu kullanıcı / çoklu PC senkronizasyonu.
-   Otomatik pil değişimini kesin olarak tespit etmek.
-   İlk aşamada pil sağlığı veya kimyasal pil kapasitesi ölçümü.
-   Kontrolcü tuşlarına müdahale etmek, tuş eşlemek veya firmware
    güncellemek.

------------------------------------------------------------------------

# 2. Donanım ve gerçekçilik notları

## 2.1 Hedef kontrolcü

Kullanıcının cihazı, fotoğrafta görülen Microsoft Xbox Wireless
Controller 9. Nesil, Carbon Black modelidir. Bu nesil genel olarak iki
adet AA pil ile çalışır ve Bluetooth bağlantısını destekler.

Uygulama başlangıçta tek bir Xbox kontrolcüsünü hedeflese de mimari
ileride birden fazla kontrolcüye genişletilebilir olmalıdır.

## 2.2 Bağlantı algılama

Bağlantı takibi projenin en önemli teknik riskidir. Windows'ta bir
Bluetooth cihazının eşleştirilmiş olması, o anda bağlı olduğu anlamına
gelmez. Sadece Bluetooth eşleştirme listesini kontrol etmek yeterli
değildir.

Aşağıdaki yöntemler araştırılıp gerçek cihazda doğrulanmalıdır:

1.  Windows PnP / HID cihaz arayüzlerinin mevcut cihazları ve
    geliş-gidiş bildirimleri.
2.  Windows.Devices.Enumeration API'leri ve mümkünse DeviceWatcher.
3.  Xbox / XInput API'leri üzerinden kontrolcünün o anda bağlı olup
    olmadığı.
4.  Gerekirse Windows GameInput veya uygun Windows oyun denetleyicisi
    API'leri.
5.  Bluetooth bağlantı durumu ile oyun kontrolcüsü aygıt varlığını ayırt
    eden bir doğrulama katmanı.

**Önemli:** XInput, HID, PnP ve Bluetooth katmanları farklı
kavramlardır. Bir API'nin "cihaz bağlı" sonucunu vermesi her durumda
Bluetooth bağlantısının kesin kanıtı olmayabilir. Bu nedenle seçilen
yöntemin hangi cihaz durumunu ölçtüğü dokümante edilmelidir.

### Bağlantı algılama kabul kriterleri

-   Kontrolcü kapalıyken veya PC'den uzaktayken yanlışlıkla bağlı
    görünmemeli.
-   Kontrolcü bağlandığında makul sürede oturum başlamalı.
-   Kontrolcü kapatıldığında veya bağlantı koptuğunda oturum bitmeli.
-   Kısa süreli API hataları yüzünden oturumlar sürekli bölünmemeli.
-   Aynı bağlantı için iki ayrı oturum açılmamalı.
-   Uygulama yeniden başlatıldığında açık kalmış oturum güvenli şekilde
    kapatılmalı.
-   Bluetooth bağlantısı kopup geri geldiğinde iki ayrı oturum
    kaydedilmeli.
-   Uygulama kapalıyken gerçekleşen bağlantılar için geçmişe dönük süre
    uydurulmamalı.

### Önerilen bağlantı durum makinesi

-   `Disconnected`
-   `Connecting` (opsiyonel, yalnızca güvenilir sinyal varsa)
-   `Connected`
-   `Disconnecting` (opsiyonel)
-   `Unknown` (API hatası veya çelişkili sinyal)

`Unknown`, otomatik olarak `Disconnected` sayılmamalıdır. Kısa süreli
sorgu hatalarında bir debounce / grace period uygulanmalıdır. Örneğin
3--5 saniyelik kısa bir doğrulama penceresi düşünülebilir; gerçek cihaz
testleriyle ayarlanmalıdır.

## 2.3 Pil seviyesi

Windows'un kontrolcü ve sürücü kombinasyonuna göre sunduğu pil bilgisi
değişebilir. Bazı API'ler yalnızca kaba pil seviyeleri verebilir; bazı
bağlantı türlerinde pil verisi hiç gelmeyebilir.

Bu nedenle: - Pil verisi API'den alınabiliyorsa kaynağı ve hassasiyeti
göster. - Alınamıyorsa "Pil bilgisi alınamıyor" de. - Kullanıcı manuel
yüzde girebilsin. - Demo yüzdesini gerçek cihaz verisi gibi gösterme. -
Pil yüzdesinin güncellenme zamanını kaydet. - Son bilinen değer ile
canlı değer ayrıştırılsın.

## 2.4 Manuel pil değişimi

İlk sürümde pil değişimi kullanıcı tarafından kaydedilecek: - "Pil
değiştirdim" butonu. - Yeni takılan pillerin başlangıç yüzdesi
(varsayılan %100; kullanıcı değiştirebilir). - Değişim tarihi ve
saati. - İsteğe bağlı not. - Eski pilin son bilinen yüzdesi varsa bu
değer. - Her pil değişimi için geçmiş kaydı.

İki AA pil birlikte değiştirildiyse tek bir "pil seti değişimi" kaydı
oluştur. İlk sürümde pilleri tek tek A/B olarak izlemek zorunlu
değildir. İleride ayrı pil kimliği takibi eklenebilir.

Otomatik değişim tahmini daha sonra gelebilir; yüzde bir anda yükseldi
diye bunun kesin pil değişimi olduğu varsayılmamalıdır. Şarj, ölçüm
hatası veya bağlantı yeniden kurulması benzer sonuç üretebilir.

------------------------------------------------------------------------

# 3. Görsel tasarım sistemi

## 3.1 Tasarım yönü

**Beyaz tema, modern, premium, sade, profesyonel. Gaming/RGB/neon
tasarım kullanılmayacak.**

Referans hissi: - Modern Windows 11 uygulamaları - Fluent Design
yaklaşımı - Temiz SaaS analytics dashboard - Bol beyaz alan - Hafif
gölgeler ve ince sınırlar - Yuvarlatılmış ama aşırı olmayan kartlar - Az
ve tutarlı vurgu rengi

## 3.2 Renk paleti

Başlangıç için: - Ana arka plan: `#F7F8FA` - Kart: `#FFFFFF` - Ana
metin: `#172033` - İkincil metin: `#667085` - İnce sınır: `#E6EAF0` -
Ana vurgu: `#2563EB` - Başarılı / bağlı: `#16A36A` - Uyarı: `#D97706` -
Hata / bağlantı kesik: `#DC4C64` - Grafik yardımcı renkleri: mavi, mor,
turkuaz, amber tonları; ölçülü kullanılmalı.

Renkler merkezi theme dosyasında tanımlanmalı. Bileşenlere rastgele renk
yazılmamalı.

## 3.3 Tipografi ve yerleşim

-   Windows uyumlu Segoe UI / Segoe UI Variable.
-   Net başlık hiyerarşisi.
-   Kartlar aynı yükseklik ve hizalama sistemini izlemeli.
-   Masaüstü pencere boyutlandırıldığında responsive yerleşim.
-   Yatay/dikey taşma olmamalı.
-   Klavye ile gezinme ve ekran okuyucu etiketleri düşünülmeli.
-   Durum yalnızca renkle anlatılmamalı; metin ve ikon da kullanılmalı.

## 3.4 Ana dashboard düzeni

Üst alan: - Controller Hub logosu / adı. - Kontrolcü adı ve bağlantı
durumu. - Son algılama zamanı. - Ayarlar erişimi.

Ana metrik kartları: 1. Toplam bağlantı süresi. 2. Bugünkü bağlantı
süresi. 3. Mevcut pil seviyesi. 4. Toplam oturum sayısı.

Grafik alanı: - Son 7 gün kullanım grafiği. - Zaman filtresi: 7 gün / 30
gün / 12 ay / tüm zamanlar.

Hızlı işlemler: - Pil değiştirdim. - Pil seviyesini güncelle. - Oturum
geçmişini aç. - Rapor dışa aktar.

Alt alan: - Son oturumlar. - Son pil değişimleri. - En uzun oturum veya
kullanım rekoru.

Dashboard boş durumunda örnek rakamlar gerçek veri gibi gösterilmemeli.
İlk açılışta açıklayıcı empty state gösterilmeli.

## 3.5 Diğer ekranlar

### Genel Bakış

Ana dashboard ve temel metrikler.

### Oturum Geçmişi

-   Başlangıç zamanı.
-   Bitiş zamanı.
-   Süre.
-   Gün ve saat.
-   Oturumun otomatik mi yoksa kurtarma/manuel kapatma mı olduğu.
-   Tarihe göre filtreleme.
-   CSV dışa aktarma.

### Analizler

-   Günlük kullanım.
-   Haftalık kullanım.
-   Aylık kullanım.
-   Yıllık kullanım.
-   Ortalama oturum süresi.
-   En uzun oturum.
-   En aktif gün.
-   En aktif saat aralığı.
-   Toplam bağlantı sayısı.
-   Bağlantı kopma / yeniden bağlanma sıklığı (yalnızca güvenilir
    şekilde kaydedilebiliyorsa).

### Pil Yönetimi

-   Son bilinen pil seviyesi.
-   Pil seviyesi geçmişi.
-   "Pil değiştirdim" butonu.
-   Değişim tarihi ve saati.
-   Değişim anındaki eski ve yeni yüzde.
-   Pil başına tahmini kullanım (veri birikince).
-   Manuel not.

### Ayarlar

-   Windows ile başlat.
-   Uygulama açılışında dashboard / sistem tepsisi tercihi.
-   Bağlantı bildirimleri.
-   İzlenecek kontrolcü seçimi (ileride birden fazla cihaz varsa).
-   Veritabanı konumu ve yedekleme.
-   Hakkında / sürüm.

------------------------------------------------------------------------

# 4. Fonksiyonel gereksinimler

## FR-01 --- Kontrolcü tespiti

Uygulama hedef Xbox kontrolcüsünün gerçek cihaz durumunu tespit eder.
Eşleştirilmiş ama bağlı olmayan cihazı bağlı saymaz.

## FR-02 --- Oturum başlatma

Bağlantı doğrulandığında: - Tek bir aktif oturum oluştur. - Başlangıç
timestamp'ini kaydet. - Dashboard durumunu Connected yap. - İsteğe bağlı
Windows bildirimi göster.

## FR-03 --- Oturum bitirme

Bağlantı doğrulanmış şekilde koptuğunda: - Aktif oturumun bitiş
timestamp'ini yaz. - Süreyi başlangıç ve bitiş arasından hesapla. -
Oturumu kapat. - Dashboard durumunu Disconnected yap.

## FR-04 --- Süre hesaplama

-   Süreler `DateTimeOffset` / UTC timestamp ile saklanmalı.
-   Kullanıcıya yerel saat diliminde gösterilmeli.
-   Yaz saati / saat dilimi değişiklikleri süre hesaplamasını bozmamalı.
-   Aktif oturum süresi ekranda canlı güncellenmeli.
-   Toplam süre kapalı oturumların toplamı + varsa aktif oturumun geçen
    süresidir.
-   Aynı oturum iki kez toplama eklenmemeli.

## FR-05 --- Kalıcılık

-   Uygulama kapatılıp açıldığında geçmiş korunmalı.
-   Bilgisayar yeniden başladığında geçmiş korunmalı.
-   Ani kapanma durumunda açık oturum için güvenli kurtarma politikası
    olmalı.
-   Uygulama kapalıyken geçen süre oturuma eklenmemeli.
-   Veritabanı bozulması için yedekleme / hata mesajı olmalı.

## FR-06 --- Günlük ve dönemsel analiz

-   Bugün: yerel günün 00:00'ından şimdiye kadar.
-   Hafta: Pazartesi başlangıçlı yerel hafta.
-   Ay: takvim ayı.
-   Yıl: takvim yılı.
-   Oturum gece yarısını geçerse süre ilgili günlere doğru dağıtılmalı.
-   Grafik toplamları oturum listesi ve KPI'larla tutarlı olmalı.

## FR-07 --- Pil kaydı

-   Kullanıcı mevcut yüzdeyi girebilir.
-   Kullanıcı pil değişimi kaydı oluşturabilir.
-   Yeni pil setinin başlangıç yüzdesi girilebilir.
-   Kayıt tarih/saat ve not içerir.
-   Pil geçmişi listelenebilir ve dışa aktarılabilir.

## FR-08 --- Dışa aktarma

İlk sürümde CSV: - Oturumlar CSV. - Pil değişimleri CSV. - UTF-8 BOM
veya Excel'de Türkçe karakterleri düzgün açacak encoding. - Dosya
kaydetme iletişim kutusu.

## FR-09 --- Sistem tepsisi

İlk MVP'den sonra: - Kapatma yerine tepsiye küçültme. - Tepsi menüsü:
Dashboard'u aç, durumu göster, çıkış. - Gerçek Exit seçildiğinde aktif
oturum doğru kapatılır. - Windows ile başlat seçeneği kullanıcı izniyle
eklenir.

------------------------------------------------------------------------

# 5. Önerilen teknik mimari

## 5.1 Öncelikli teknoloji önerisi

Öncelik: **C# + .NET 8 veya güncel LTS .NET + WPF** (veya Windows App
SDK / WinUI 3, ancak proje başında birini seç ve gerekçesiz değiştirme).

Neden: - Windows 11 ile doğal entegrasyon. - PnP / Windows API'lerine
erişim. - Yerel masaüstü uygulaması ve sistem tepsisi için uygun
ekosistem. - SQLite ile yerel veri saklama. - Dağıtılabilir Windows
EXE/MSIX oluşturabilme.

WPF seçilirse modern beyaz tasarım için Fluent benzeri stil
oluşturulabilir. UI katmanını MVVM ile kur.

Alternatif teknoloji önereceksen: - Önce WPF/WinUI seçeneklerini
artı-eksi karşılaştır. - Bluetooth / PnP erişimini nasıl yapacağını
açıkla. - Kullanıcı onayı almadan teknoloji yığınını değiştirme.

## 5.2 Önerilen katmanlar

-   `ControllerHub.App` --- WPF UI, navigation, app lifecycle.
-   `ControllerHub.Core` --- domain modelleri, hesaplamalar, servis
    arayüzleri.
-   `ControllerHub.Infrastructure` --- Windows cihaz API'leri, SQLite,
    logging, export.
-   `ControllerHub.Tests` --- unit ve integration testleri.

Başlangıçta tek proje ile prototip yapılabilir ama iş mantığı UI'dan
ayrılmalıdır. Proje gereksiz yere karmaşık mikroservis mimarisine
dönüştürülmemelidir.

## 5.3 Önerilen paketler

Yalnızca ihtiyaç doğrulanırsa ekle: - SQLite için
`Microsoft.Data.Sqlite` veya EF Core SQLite. - MVVM için
`CommunityToolkit.Mvvm`. - Grafik için sürdürülebilir bir WPF grafik
kütüphanesi; ör. LiveCharts2. Paket seçimini proje aşamasında
gerekçelendir. - Logging için `Microsoft.Extensions.Logging` veya
Serilog. - CSV için ilk aşamada .NET standart kütüphaneleri yeterli
olabilir.

Her paketin sürümünü ve lisansını kontrol et. Gereksiz UI
framework'lerini üst üste ekleme.

## 5.4 Önerilen klasör yapısı

``` text
ControllerHub/
  ControllerHub.sln
  src/
    ControllerHub.App/
      App.xaml
      MainWindow.xaml
      Views/
      ViewModels/
      Resources/
      Themes/
      Assets/
    ControllerHub.Core/
      Models/
      Interfaces/
      Services/
    ControllerHub.Infrastructure/
      DeviceDetection/
      Persistence/
      Export/
      Logging/
  tests/
    ControllerHub.Tests/
  docs/
    PROJECT_SPEC.md
    DEVICE_TEST_PLAN.md
  README.md
```

------------------------------------------------------------------------

# 6. Veri modeli

İlk sürüm için SQLite önerilir.

## 6.1 Sessions

  Alan              Tip                        Açıklama
  ----------------- -------------------------- ------------------------------------------
  Id                INTEGER / GUID             Oturum kimliği
  DeviceKey         TEXT                       Cihazı tanımlayan kararlı anahtar
  StartedAtUtc      TEXT / INTEGER             Başlangıç zamanı
  EndedAtUtc        TEXT / INTEGER, nullable   Bitiş zamanı
  DurationSeconds   INTEGER                    Hesaplanan süre
  EndReason         TEXT                       Disconnected, AppExit, Recovery, Unknown
  CreatedAtUtc      TEXT / INTEGER             Kayıt oluşturulma zamanı

Kural: Aynı `DeviceKey` için aynı anda birden fazla açık oturum
olmamalı.

## 6.2 BatteryReadings

  Alan            Tip                 Açıklama
  --------------- ------------------- --------------------------
  Id              INTEGER / GUID      Kayıt kimliği
  DeviceKey       TEXT                Kontrolcü
  RecordedAtUtc   TEXT / INTEGER      Ölçüm zamanı
  Percentage      INTEGER, nullable   0--100
  Source          TEXT                Manual / WindowsApi
  Confidence      TEXT                Exact / Coarse / Unknown
  Note            TEXT                Opsiyonel not

## 6.3 BatteryChanges

  -----------------------------------------------------------------------
  Alan                    Tip                     Açıklama
  ----------------------- ----------------------- -----------------------
  Id                      INTEGER / GUID          Değişim kimliği

  DeviceKey               TEXT                    Kontrolcü

  ChangedAtUtc            TEXT / INTEGER          Değişim zamanı

  PreviousPercentage      INTEGER, nullable       Eski pilin son bilinen
                                                  seviyesi

  NewPercentage           INTEGER, nullable       Yeni pil setinin
                                                  başlangıç seviyesi

  Note                    TEXT                    Kullanıcı notu

  ChangeType              TEXT                    Manual / Estimated
                                                  (ileride)
  -----------------------------------------------------------------------

## 6.4 Settings

Kullanıcı tercihleri: - Theme (ilk sürümde Light; ileride opsiyonel
Dark). - StartWithWindows. - MinimizeToTray. - NotificationsEnabled. -
SelectedDeviceKey. - DatabaseSchemaVersion.

## 6.5 Önemli hesaplama ilkeleri

-   Oturum süresi `EndedAtUtc - StartedAtUtc` üzerinden hesaplanır.
-   Süreyi her saniye veritabanına yazmak gerekmez. Aktif oturum
    başlangıcı saklanır ve UI canlı hesaplar.
-   Kapanışta oturum kapatılır.
-   Ani çökmede açık oturum bir sonraki başlangıçta `Recovery` ile
    kapatılır; son kapanış anı kesin bilinmiyorsa tahmin yapıldığı
    açıkça belirtilir. Daha güvenli seçenek, uygulamanın düzenli
    checkpoint zamanı tutmasıdır.
-   Günlük raporlar gece yarısını aşan oturumları günlere bölmelidir.
-   UTC sakla, yerel saat göster.

------------------------------------------------------------------------

# 7. Gizlilik, güvenlik ve kalite

-   Hiçbir veri varsayılan olarak internete gönderilmez.
-   Hesap açma veya bulut üyeliği zorunlu değildir.
-   Veritabanı kullanıcının yerel AppData klasöründe tutulabilir.
-   Loglarda gereksiz kişisel veri veya tam Bluetooth MAC adresi
    gösterme.
-   Veritabanı yedekleme / geri yükleme düşünülmeli.
-   Uygulama gereksiz yönetici izni istememeli.
-   Windows Defender tarafından şüpheli davranış oluşturacak bilinmeyen
    script veya gizli servis kullanılmamalı.
-   Hatalar kullanıcıya anlaşılır Türkçe ile gösterilmeli.
-   Uygulama başlatılırken cihaz sorgusu UI thread'i kilitlememeli.
-   Cihaz algılama arka plan servisi ve iptal edilebilir olmalı.
-   Log dosyaları boyut sınırına sahip olmalı.

------------------------------------------------------------------------

# 8. Aşamalı geliştirme planı

**Her aşama ayrı ayrı yapılacak ve kullanıcı onayı beklenecek.**

## Aşama 0 --- Keşif ve teknik doğrulama

Amaç: Kod yazmaya başlamadan Windows 11 ve hedef kontrolcünün nasıl
algılanabileceğini anlamak.

Görevler: 1. Windows sürümünü ve .NET SDK durumunu kontrol et. 2. Hedef
Xbox kontrolcüsü bağlandığında Device Manager / PnP listesinde hangi
cihaz adları ve Instance ID'ler görünüyor araştır. 3. Bağlantı yokken ve
bağlıyken cihaz listelerini karşılaştır. 4. XInput / Windows API
seçeneklerini değerlendir. 5. Pil bilgisinin hangi API'den
alınabileceğini araştır. 6. Bağlantı algılama için en güvenilir
yaklaşımı öner. 7. Teknik riskleri ve test planını yaz.

Çıktılar: - `docs/DEVICE_TEST_PLAN.md` - Teknik mimari kararı (ADR). -
Seçilen bağlantı algılama stratejisi.

**Kabul kriteri:** Kullanıcıya gerçek cihazda hangi sinyali okuyacağımız
açıkça anlatılmış olmalı. Henüz tam uygulama yazılmamalı.

## Aşama 1 --- Proje iskeleti ve tasarım prototipi

Görevler: 1. Solution ve projeleri oluştur. 2. MVVM temelini kur. 3.
Beyaz tema renklerini ve tipografiyi tanımla. 4. Sol navigasyon veya üst
navigasyon yaklaşımından birini seç. 5. Dashboard'u gerçek cihaz verisi
olmadan, açıkça mock data olarak tasarla. 6. Pencere boyutlandırma ve
boş durumları hazırla.

Ekranlar: - Genel Bakış - Oturum Geçmişi - Analizler - Pil Yönetimi -
Ayarlar

Çıktı: - Çalışan UI prototipi. - `README.md` güncellemesi.

**Kabul kriteri:** Uygulama açılır, beyaz tema tutarlı görünür, ekranlar
arasında gezinilir. Henüz sahte metrikler gerçek veri gibi sunulmaz.

## Aşama 2 --- Gerçek Xbox bağlantı algılama prototipi

Görevler: 1. Bağlantı algılama servisini UI'dan bağımsız kur. 2.
Connected / Disconnected / Unknown durumlarını üret. 3. PnP veya seçilen
Windows API'sini kullan. 4. 3--5 saniye polling veya event tabanlı
yöntemi test et; mümkünse event tabanlı tercih et. 5. Debounce / grace
period ekle. 6. Gerçek kontrolcü ile bağlanma, kapatma, Bluetooth'u
kapatma, yeniden bağlanma senaryolarını test et. 7. Bağlantı durumunu
küçük debug panelinde göster.

**Kabul kriteri:** En az 10 bağlanma/kopma döngüsünde yanlış oturum açma
veya çift oturum olmadan doğru algılama. Sonuçlar gerçek test olarak
raporlanmalı.

## Aşama 3 --- Oturum kaydı ve zaman hesaplama

Görevler: 1. SQLite repository ve migration sistemini ekle. 2. Oturum
başlatma/bitirme akışını yaz. 3. Aktif oturumu canlı göster. 4. Toplam,
bugün ve oturum sayısı hesaplarını yap. 5. Gece yarısını aşan oturumları
doğru günlere böl. 6. Uygulama kapanışı, çökme ve yeniden başlatma
senaryolarını ele al. 7. Zaman hesaplamaları için unit test yaz.

**Kabul kriteri:** Bağlantı döngüleri veritabanında doğru kaydolur,
yeniden başlatma sonrası geçmiş korunur ve hesaplar testlerden geçer.

## Aşama 4 --- Dashboard'u gerçek veriye bağlama

Görevler: 1. Mock metrikleri kaldır. 2. KPI kartlarını gerçek repository
sorgularına bağla. 3. Haftalık kullanım grafiğini gerçek veriden üret.
4. Son oturumlar tablosunu ekle. 5. Empty state ve hata durumlarını
tasarla. 6. Grafik ve metriklerin aynı hesaplama mantığını kullandığını
doğrula.

**Kabul kriteri:** Dashboard'daki toplamlar oturum geçmişiyle birebir
tutarlı.

## Aşama 5 --- Pil yönetimi (manuel ilk sürüm)

Görevler: 1. Pil seviyesi girme formu. 2. "Pil değiştirdim" işlemi. 3.
Değişim anındaki eski seviye ve yeni başlangıç seviyesi. 4. Tarih/saat
ve opsiyonel not. 5. Pil değişim geçmişi. 6. Pil yüzdesi geçmişi
grafiği. 7. Pil kayıtlarını CSV dışa aktarma.

**Kabul kriteri:** Kullanıcı pil değiştirdiğinde kayıt kalıcı saklanır
ve geçmişte görüntülenir.

## Aşama 6 --- Analizler ve raporlama

Görevler: 1. Günlük / haftalık / aylık / yıllık filtre. 2. Ortalama
oturum süresi. 3. En uzun oturum. 4. En aktif gün ve saat. 5. Toplam
oturum sayısı. 6. Pil değişim sayısı. 7. Pil seti başına tahmini
bağlantı süresi (veri yeterli olduğunda). 8. CSV dışa aktarma.

**Kabul kriteri:** Filtreler doğru çalışır, tüm grafikler ve sayılar
test verileriyle doğrulanır.

## Aşama 7 --- Pil telemetrisi araştırması ve opsiyonel otomasyon

Bu aşama ilk MVP'nin dışında tutulabilir.

Görevler: 1. Windows API'leriyle pil bilgisinin gerçek kontrolcüden
okunup okunamadığını araştır. 2. Gerçek pil seviyesi ile manuel değeri
ayırt et. 3. Ölçüm hassasiyetini kullanıcıya göster. 4. Ani yüzde
artışından otomatik pil değişimi tahmini üretilecekse false positive
riskini yönet. 5. Otomatik tahmini kesin kayıt olarak değil, kullanıcı
onayı bekleyen öneri olarak sun.

**Kabul kriteri:** Gerçek cihazda doğrulanmayan pil yüzdesi veya değişim
olayı otomatik gerçekmiş gibi kaydedilmez.

## Aşama 8 --- Sistem tepsisi, Windows başlangıcı ve bildirimler

Görevler: 1. Sistem tepsisi ikonu. 2. Pencereyi kapatınca tepsiye
küçültme seçeneği. 3. Tepsi menüsü. 4. Windows açılışında otomatik
başlatma (kullanıcı tercihi). 5. Bağlandı / bağlantı kesildi
bildirimleri. 6. Gerçek Exit işleminde oturumu doğru kapatma.

**Kabul kriteri:** Uygulama arka planda güvenilir şekilde çalışır ve
kullanıcı istediğinde tamamen kapanır.

## Aşama 9 --- Paketleme ve yayın hazırlığı

Görevler: 1. Release build. 2. Self-contained EXE veya MSIX
seçeneklerini değerlendir. 3. Uygulama ikonu ve sürüm bilgisi. 4.
Kurulum / kaldırma testi. 5. Windows başlangıcı ve veritabanı konumunu
kontrol et. 6. Temiz Windows kullanıcı profilinde test. 7. Kullanıcı
dokümantasyonu.

**Kabul kriteri:** Kullanıcı geliştirme ortamına ihtiyaç duymadan
uygulamayı kurup çalıştırabilir.

------------------------------------------------------------------------

# 9. Test planı

Her aşamada uygun testler eklenmeli.

## Bağlantı testleri

-   Kontrolcü kapalı.
-   Kontrolcü açık ama eşleştirilmemiş.
-   Eşleştirilmiş ama bağlı değil.
-   Bluetooth ile bağlı.
-   Kontrolcü manuel kapatıldı.
-   Bluetooth PC'de kapatıldı.
-   PC uykuya girdi ve geri döndü.
-   Kontrolcü yeniden bağlandı.
-   Uygulama bağlıyken kapatıldı ve yeniden açıldı.
-   Uygulama bağlantı yokken açıldı, sonra kontrolcü bağlandı.

## Zaman testleri

-   30 saniyelik kısa oturum.
-   Birkaç saatlik oturum.
-   Gece yarısını aşan oturum.
-   Saat dilimi değişikliği.
-   Windows yeniden başlatma.
-   Uygulamanın beklenmedik kapanması.
-   Aynı anda iki event gelmesi.

## Pil testleri

-   Manuel yüzde girme.
-   Pil değişimi kaydetme.
-   Eski yüzde bilinmiyorken değişim kaydı.
-   Pil değişim notu.
-   Veritabanı yeniden açıldığında pil geçmişi.
-   CSV Türkçe karakter testi.

## UI testleri

-   1280x720 ve 1920x1080.
-   Pencere küçültme / büyütme.
-   Uzun tarih ve süre metinleri.
-   Boş veri durumu.
-   Hata durumu.
-   Klavye erişilebilirliği.

------------------------------------------------------------------------

# 10. Analiz tanımları

Terimler tutarlı kullanılmalı:

-   **Bağlantı süresi:** Kontrolcünün PC'ye bağlı olduğu doğrulanmış
    süre.
-   **Oturum:** Bir bağlantı başlangıcı ile bağlantı bitişi arasındaki
    dönem.
-   **Toplam bağlantı süresi:** Tüm oturum sürelerinin toplamı.
-   **Bugünkü kullanım:** Yerel takvim gününde kalan bağlantı
    sürelerinin toplamı.
-   **Ortalama oturum:** Tamamlanmış oturumların ortalama süresi.
-   **En uzun oturum:** Tek bir oturumdaki en yüksek süre.
-   **Pil değişim sayısı:** Kullanıcının manuel kaydettiği pil seti
    değişimlerinin sayısı.
-   **Pil seti kullanım süresi:** Bir pil değişim kaydı ile bir sonraki
    pil değişim kaydı arasındaki doğrulanmış kontrolcü bağlantı
    sürelerinin toplamı. İlk/son aralıklar eksik olabilir; tahmin olduğu
    belirtilmeli.

"Bağlantı süresi" hiçbir yerde otomatik olarak "oyun süresi" diye
sunulmamalı.

------------------------------------------------------------------------

# 11. İlk sürüm için öncelik sırası

P0 --- Olmazsa olmaz: - Gerçek bağlantı algılama. - Oturum başlatma /
bitirme. - Kalıcı veritabanı. - Toplam ve günlük süre. - Beyaz
dashboard.

P1 --- İlk MVP'nin devamı: - Oturum geçmişi. - Haftalık grafik. - Manuel
pil seviyesi. - Manuel pil değişimi. - CSV dışa aktarma.

P2 --- İyileştirme: - Analizler ekranı. - Sistem tepsisi. - Windows
başlangıcında çalışma. - Bildirimler. - Pil telemetrisi araştırması.

P3 --- Gelecek: - Birden fazla kontrolcü. - Otomatik pil değişimi
tahmini. - Pil seti A/B kimliği. - Gelişmiş raporlar. - Yedekleme / içe
aktarma.

------------------------------------------------------------------------

# 12. Proje durumu --- Antigravity bu bölümü güncel tutsun

  Aşama                                Durum        Not
  ------------------------------------ ------------ ----------------------------------------------------
  0\. Keşif ve teknik doğrulama        Tamamlandı   .NET 8 SDK kuruldu, BLE GATT pil (%100) ve PnP teyit edildi.
  1\. Proje iskeleti ve UI prototipi   Tamamlandı   Solution, 4 katmanlı mimari, MVVM, beyaz tema ve 5 ekran prototipi tamamlandı.
  2\. Gerçek bağlantı algılama         Tamamlandı   Windows PnP ve WinRT BLE watcher kuruldu, 4s debounce ve 10 döngü doğrulaması test edildi.
  3\. Oturum ve zaman hesaplama        Tamamlandı   SQLite (WAL), oturum kaydı/kurtarma ve gece yarısı süre hesaplama motoru tamamlandı.
  4\. Gerçek verili dashboard          Tamamlandı   Dashboard gerçek SQLite verisine bağlandı, mock banner kaldırıldı, empty state ve canlı sayaç entegre edildi.
  5\. Manuel pil yönetimi              Tamamlandı   Manuel pil seviyesi, yeni pil seti kaydı, SQLite geçmişi ve Excel uyumlu CSV dışa aktarımı tamamlandı.
  6\. Analizler ve raporlama           Tamamlandı   Filtreli oturum geçmişi, AnalyticsEngine, dönem toplamları ve oturum CSV dışa aktarımı tamamlandı.
  7\. Pil telemetrisi                  Tamamlandı   BLE GATT (0x180F) telemetrisi, doğruluk/kaynak rozetleri, BatteryChangeDetector ve kullanıcı onaylı öneri motoru tamamlandı.
  8\. Sistem tepsisi ve başlangıç      Tamamlandı   Shell_NotifyIconW, Win32 tray menüsü, pencereyi tepsiye küçültme, Windows başlangıç servisi ve toast bildirimleri tamamlandı.
  9\. Paketleme ve yayın               Tamamlandı   Release konfigürasyonunda win-x64 Self-Contained taşınabilir tekil EXE üretildi, uygulama ikonu ve meta veriler gömüldü, kullanıcı el kitabı tamamlandı.

------------------------------------------------------------------------

# 13. Antigravity'ye verilecek ilk mesaj

Aşağıdaki metni bu dosyayla birlikte Antigravity'ye gönder:

> Bu projede `PROJECT_SPEC.md` ana gereksinim belgesidir. Önce tamamını
> oku ve anladığını kısa şekilde özetle. Projeyi tek seferde geliştirme.
> Belgedeki aşama geçiş kurallarına kesinlikle uy.
>
> Şu an yalnızca **Aşama 0 --- Keşif ve teknik doğrulama** ile başla.
> Windows 11 ortamını incele, .NET SDK ve mevcut araçları kontrol et,
> Xbox Wireless Controller 9. Nesil Bluetooth bağlantısını güvenilir
> algılamak için Windows API seçeneklerini araştır. Eşleştirilmiş cihaz
> ile gerçekten bağlı cihazı ayır. Pil bilgisinin erişilebilirliğini
> ayrıca değerlendir.
>
> Henüz dashboard'un tamamını veya tüm uygulamayı yazma. Önce cihaz
> algılama yaklaşımını gerçekçi biçimde belirle,
> `docs/DEVICE_TEST_PLAN.md` ve teknik karar belgesini hazırla.
> Sonuçları ve riskleri raporla, sonra benden onay bekle. Onayım olmadan
> Aşama 1'e geçme.
>
> Arayüz tasarımının gaming/RGB değil, beyaz, modern, premium ve
> minimalist olması zorunludur. Uygulama Türkçe olacaktır. Demo
> verilerini gerçek veri gibi sunma.

------------------------------------------------------------------------

## Son ürünün başarı tanımı

Controller Hub başarılı sayılabilmesi için: 1. Kullanıcı Xbox
kontrolcüsünü Bluetooth ile bağladığında oturum otomatik başlar. 2.
Bağlantı kesilince oturum otomatik biter. 3. Toplam süre ve geçmiş doğru
kalır. 4. Bilgisayar yeniden başlatıldığında geçmiş kaybolmaz. 5.
Dashboard gerçek veriyi gösterir. 6. Pil değişimi kullanıcı tarafından
kolayca kaydedilir. 7. Günlük, haftalık ve aylık analizler doğru
hesaplanır. 8. Arayüz beyaz, sade, hızlı ve profesyoneldir. 9. Uygulama
kapalıyken gerçekleşmeyen ölçümleri varmış gibi göstermez. 10. Kullanıcı
tüm verilerinin kendi bilgisayarında kaldığını bilir.
