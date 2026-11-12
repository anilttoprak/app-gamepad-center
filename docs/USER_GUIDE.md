# Controller Hub — Kullanıcı El Kitabı ve Yayın Rehberi (v1.0.0)

Controller Hub, Windows 11 üzerinde Microsoft Xbox Wireless Controller (Model 1914) ve uyumlu Bluetooth denetleyicilerin bağlantı oturumlarını, kullanım sürelerini ve pil ömrünü takip eden minimalist, profesyonel bir masaüstü kontrol merkezidir.

---

## 1. Sistem Gereksinimleri

- **İşletim Sistemi:** Windows 10 (Derleme 19041 veya üstü) / Windows 11 (Tüm sürümler)
- **Mimari:** x64 (64-bit)
- **Bağlantı:** Dahili veya harici Bluetooth 4.0 / BLE adaptörü
- **Hedef Donanım:** Xbox Wireless Controller (9. Nesil / Model 1914) ve uyumlu Xbox Bluetooth kolları
- **Çalışma Bağımlılığı:** **YOK.** (.NET 8 çalışma zamanı tekil EXE içerisine gömülmüştür).

---

## 2. Kurulum ve Başlatma

Controller Hub taşınabilir (portable) olarak tasarlanmıştır:
1. `ControllerHub.exe` dosyasını bilgisayarınızda dilediğiniz bir klasöre (örn: `C:\Program Files\ControllerHub` veya `Masaüstü`) kopyalayın.
2. Dosyaya çift tıklayarak çalıştırın.
3. Yönetici (Admin) izni **gerektirmez**, doğrudan standart kullanıcı profiliyle çalışır.

---

## 3. Kontrolcüyü Eşleştirme ve Oturum Takibi

1. Kontrolcünüzü Windows Ayarları -> *Bluetooth ve Cihazlar* menüsünden bilgisayarınızla bir kez eşleştirin.
2. Kontrolcünün ortasındaki Xbox logosuna basarak açtığınızda:
   - Controller Hub bağlantıyı 4 saniye içinde doğrular.
   - Durum rozeti **🟢 Bağlı** olarak güncellenir.
   - Oturum otomatik başlar ve Genel Bakış ekranındaki canlı sayaç saniye saniye işler.
   - Görev çubuğunun sağ alt köşesinde *"🎮 Xbox Kontrolcüsü Bağlandı"* bildirimi gösterilir.
3. Kontrolcüyü kapattığınızda veya menzil dışına çıktığınızda:
   - 4 saniyelik sinyal onaylama (debounce) penceresinden sonra oturum otomatik kapatılır.
   - Süre SQLite veritabanına kalıcı olarak işlenir.
   - Bildirim alanında *"⚪ Kontrolcü Bağlantısı Kesildi — Süre: X dk"* özeti gösterilir.

---

## 4. Pil Yönetimi ve Telemetrisi

- **Gerçek Zamanlı Donanım Okuması:** Bluetooth LE GATT Pil Servisi (`0x180F`) üzerinden kontrolcünün pili %1 hassasiyetle okunur.
- **Şeffaf Kaynak Gösterimi:** Arayüzde okumanın donanımsal mı (`Bluetooth GATT`) yoksa kullanıcı girişi mi (`Manuel`) olduğu açıkça rozetle gösterilir.
- **Stale Cache Filtresi:** Kontrolcü kapandığında Windows önbelleğindeki eski değerler güvenli bir şekilde dondurulur, sahte okuma yapılmaz.

### Pil Seti Değiştirme ve Onay Mekanizması:
1. **Manuel Kayıt:** Yeni pil taktığınızda *Pil Yönetimi* sekmesindeki "Yeni Pil Seti Kaydet" formundan başlangıç yüzdesini ve pil tipini (örn: Eneloop Pro, Duracell Ultra) yazıp kaydedebilirsiniz.
2. **Otomatik Tahmin & Kullanıcı Onayı:** Kontrolcünün pili %15 seviyesindeyken yeni pil takıp açtığınızda pil %100'e fırlarsa, Controller Hub bunu algılar ve ekranda bir öneri bandı açar:
   - **[✓ Evet, Kaydet]:** Değişimi onaylar ve geçmişe `Otomatik (Onaylandı)` olarak kaydeder.
   - **[✕ Yoksay]:** Öneriyi reddeder, veritabanına gereksiz kayıt yazılmaz.

---

## 5. Analizler ve Raporlama

- **Dönem Filtreleri:** Analizler ekranından *Tümü*, *Bugün*, *Bu Hafta* veya *Bu Ay* seçenekleriyle kullanımınızı süzebilirsiniz.
- **Metrikler:** Ortalama oturum süresi, en uzun (rekor) oturum, en aktif gün/saat aralığı ve pil seti başına ortalama bağlantı süresi matematiksel olarak hesaplanır.
- **CSV Dışa Aktarma:** *Oturum Geçmişi* ve *Pil Yönetimi* ekranlarındaki "CSV Dışa Aktar" butonuyla tüm kayıtlarınızı Excel ve Numbers ile tam uyumlu (UTF-8 BOM) tablo olarak kaydedebilirsiniz.

---

## 6. Sistem Tepsisi ve Windows Başlangıcı

- **Tepsiye Küçültme:** Pencereyi `X` ile kapattığınızda uygulama kapanmaz; görev çubuğunda saatin yanındaki sistem tepsisinde arka planda çalışmaya devam eder.
- **Tepsi Menüsü:** Tepsideki Controller Hub simgesine sağ tıklayarak:
  - *Controller Hub'ı Göster:* Pencereyi açar.
  - *Durum:* Aktif bağlantıyı ve süreyi gösterir.
  - *Çıkış:* Uygulamayı ve oturumu güvenli şekilde sonlandırır.
- **Windows Açılışında Başlatma:** *Ayarlar* sekmesinden *"Windows açılışında otomatik başlat"* seçeneğini işaretlediğinizde, bilgisayar her açıldığında Controller Hub arka planda hazır bekler.

---

## 7. Gizlilik ve Veri Depolama

- Controller Hub **%100 yereldir (offline)**.
- İnternete hiçbir telemetri, kişisel veri veya kullanım kaydı gönderilmez.
- Tüm veriler yerel bilgisayarınızda şu konumda tutulur:
  `%LocalAppData%\ControllerHub\controllerhub.db`
- Veritabanı klasörünüze doğrudan Ayarlar ekranındaki *"Klasörü Aç"* butonuyla erişebilirsiniz.

---

## 8. Kaldırma / Verileri Temizleme

Uygulamayı bilgisayarınızdan tamamen kaldırmak isterseniz:
1. Sistem tepsisindeki Controller Hub simgesine sağ tıklayıp **Çıkış** deyin.
2. `ControllerHub.exe` dosyasını silin.
3. Oturum geçmişinizi de silmek isterseniz `%LocalAppData%\ControllerHub` klasörünü silmeniz yeterlidir.
