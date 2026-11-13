using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControllerHub.Core.Interfaces;
using ControllerHub.Core.Models;
using ControllerHub.Core.Services;
using ControllerHub.Infrastructure.Export;
using ControllerHub.Infrastructure.Services;
using Microsoft.Win32;

namespace ControllerHub.App.ViewModels;

public record BatteryChangeDisplayItem(
    long Id,
    string Date,
    string PreviousLevelText,
    string NewLevelText,
    string Note,
    string Source,
    string PerformanceText,
    int SessionCount);

public partial class BatteryViewModel : ViewModelBase
{
    private IBatteryRepository? _batteryRepository;
    private IDeviceWatcherService? _deviceWatcher;
    private ISessionRepository? _sessionRepository;
    private SessionTrackerService? _sessionTracker;
    private readonly Dictionary<string, int> _lastKnownPercentageByDevice = new();
    private readonly BatteryChangeDetector _changeDetector = new();
    private BatteryChangeProposal? _activeProposal;

    public override string Title => "Pil Yönetimi";

    public ObservableCollection<ControllerFilterOption> ControllerFilterOptions { get; } = new();

    [ObservableProperty]
    private ControllerFilterOption? _selectedControllerFilter;

    public event EventHandler<ControllerFilterOption>? ControllerFilterChanged;
    private bool _isUpdatingFilter = false;

    partial void OnSelectedControllerFilterChanged(ControllerFilterOption? value)
    {
        if (_isUpdatingFilter) return;
        if (value != null)
        {
            ControllerFilterChanged?.Invoke(this, value);
        }
        _ = RefreshDataAsync();
    }

    public void UpdateFilterOptions(IEnumerable<ControllerFilterOption> options, string selectedKey)
    {
        _isUpdatingFilter = true;
        try
        {
            ControllerFilterOptions.Clear();
            foreach (var opt in options)
            {
                ControllerFilterOptions.Add(opt);
            }

            SelectedControllerFilter = ControllerFilterOptions.FirstOrDefault(o => o.DeviceKey == selectedKey)
                ?? ControllerFilterOptions.FirstOrDefault();
        }
        finally
        {
            _isUpdatingFilter = false;
        }

        _ = RefreshDataAsync();
    }

    [ObservableProperty]
    private string _currentPercentage = "Bilinmiyor";

    [ObservableProperty]
    private string _lastUpdatedText = "Pil okuması bekleniyor...";

    [ObservableProperty]
    private string _sourceDescription = "Bilinmiyor";

    [ObservableProperty]
    private string _confidenceDescription = "Bilinmiyor";

    [ObservableProperty]
    private string _confidenceBadge = "Belirsiz";

    [ObservableProperty]
    private string _activeBatteryUsageText = "0 dk (Henüz oturum yok)";

    [ObservableProperty]
    private string _averageBatteryDurationText = "Veri yetersiz (En az 1 değişim gerekli)";

    [ObservableProperty]
    private string _bestBatterySetText = "Henüz rekor bulunmuyor";

    [ObservableProperty]
    private bool _hasPendingProposal = false;

    [ObservableProperty]
    private string _pendingProposalText = "";

    [ObservableProperty]
    private string _proposalDetails = "";

    [ObservableProperty]
    private int _newBatteryPercentage = 100;

    [ObservableProperty]
    private int _manualPercentageInput = 100;

    [ObservableProperty]
    private string _batteryNote = "Kutu İçeriği Pil (Duracell OEM 2'li set)";

    [ObservableProperty]
    private bool _isFirstBatterySetup = true;

    [ObservableProperty]
    private string _batteryFormTitle = "İlk Pil Setini Tanımla (Kutu İçeriği)";

    [ObservableProperty]
    private string _batteryFormSubtitle = "Gamepad'i yeni aldığınızda kutudan çıkan ilk pili kaydederek kullanım döngüsünü başlatın.";

    [ObservableProperty]
    private string _batterySaveButtonText = "🔋  İlk Pil Setini Başlat";

    [ObservableProperty]
    private string _statusFeedback = "";

    [ObservableProperty]
    private bool _hasFeedback = false;

    [ObservableProperty]
    private bool _hasChanges = false;

    [ObservableProperty]
    private bool _isEmptyChanges = true;

    [ObservableProperty]
    private double _batteryLevelBarWidth = 0;

    public ObservableCollection<BatteryChangeDisplayItem> ChangeHistory { get; } = new();

    public BatteryChangeProposal? ActiveProposal => _activeProposal;

    public void Initialize(
        IBatteryRepository batteryRepository,
        IDeviceWatcherService deviceWatcher,
        ISessionRepository? sessionRepository = null,
        SessionTrackerService? sessionTracker = null)
    {
        _batteryRepository = batteryRepository;
        _deviceWatcher = deviceWatcher;
        _sessionRepository = sessionRepository;
        _sessionTracker = sessionTracker;
    }

    public async Task RefreshDataAsync()
    {
        if (_batteryRepository == null) return;

        var selectedKey = SelectedControllerFilter?.DeviceKey ?? "ALL";
        var filterDeviceKey = selectedKey == "ALL" ? null : selectedKey;

        // 1. Son bilinen pil okumasını getir (seçili kontrolcüye özel veya genel)
        var latestReading = await _batteryRepository.GetLatestReadingAsync(filterDeviceKey);
        if (latestReading?.Percentage != null)
        {
            UpdateCurrentPercentage(
                latestReading.Percentage.Value,
                latestReading.RecordedAtUtc,
                latestReading.Source,
                latestReading.Confidence,
                _deviceWatcher?.CurrentDevice.IsConnected ?? false,
                latestReading.DeviceKey);
        }
        else
        {
            CurrentPercentage = "Bilinmiyor";
            LastUpdatedText = "Pil okuması bekleniyor...";
            SourceDescription = "Bilinmiyor";
            ConfidenceDescription = "Bilinmiyor";
            ConfidenceBadge = "Belirsiz";
            BatteryLevelBarWidth = 0;
        }

        // 2. Pil değişim geçmişini ve oturum verilerini çekerek pil performansı analizini hesapla
        var changes = await _batteryRepository.GetBatteryChangesAsync(100, filterDeviceKey);
        var sessions = _sessionRepository != null ? await _sessionRepository.GetRecentSessionsAsync(1000) : Array.Empty<ControllerSession>();
        if (selectedKey != "ALL")
        {
            sessions = sessions.Where(s => s.DeviceKey == selectedKey).ToList();
        }

        var activeSession = _sessionTracker?.ActiveSession;
        if (selectedKey != "ALL")
        {
            activeSession = _sessionTracker?.ActiveSessions.FirstOrDefault(a => a.DeviceKey == selectedKey);
        }

        // Eğer bekleyen öneri başka bir cihaz içinse ve şu an tek bir cihaz filtreleniyorsa öneriyi gizle
        if (HasPendingProposal && _activeProposal != null && selectedKey != "ALL" && _activeProposal.DeviceKey != selectedKey)
        {
            HasPendingProposal = false;
        }

        var performance = BatteryCycleCalculator.ComputePerformance(sessions, changes, activeSession, DateTimeOffset.UtcNow);
        var cycleMap = performance.Cycles.Where(c => c.ChangeId.HasValue).ToDictionary(c => c.ChangeId!.Value);

        ChangeHistory.Clear();

        foreach (var c in changes)
        {
            var prevText = c.PreviousPercentage.HasValue ? $"%{c.PreviousPercentage.Value}" : "-";
            var newText = c.NewPercentage.HasValue ? $"%{c.NewPercentage.Value}" : "-";
            var dateText = c.ChangedAtUtc.ToLocalTime().ToString("dd MMM yyyy HH:mm");

            string perfText;
            int sessCount = 0;
            if (cycleMap.TryGetValue(c.Id, out var cycleStats))
            {
                sessCount = cycleStats.SessionCount;
                perfText = cycleStats.IsActiveCycle
                    ? $"{cycleStats.DurationFormatted} (Aktif Pil - {sessCount} oturum)"
                    : $"{cycleStats.DurationFormatted} ({sessCount} oturum)";
            }
            else
            {
                perfText = "-";
            }

            ChangeHistory.Add(new BatteryChangeDisplayItem(
                c.Id,
                dateText,
                prevText,
                newText,
                string.IsNullOrWhiteSpace(c.Note) ? "Not belirtilmedi" : c.Note,
                c.ChangeType,
                perfText,
                sessCount));
        }

        HasChanges = ChangeHistory.Count > 0;
        IsEmptyChanges = !HasChanges;
        IsFirstBatterySetup = !HasChanges;

        if (IsFirstBatterySetup)
        {
            BatteryFormTitle = "İlk Pil Setini Tanımla (Kutu İçeriği)";
            BatteryFormSubtitle = "Gamepad'i yeni aldığınızda kutudan çıkan ilk pili kaydederek kullanım döngüsünü başlatın. (Geriye dönük tüm oturumlarınız bu pile sayılır)";
            BatterySaveButtonText = "🔋  İlk Pil Setini Başlat";
            if (string.IsNullOrWhiteSpace(BatteryNote) || BatteryNote.Contains("Duracell Ultra"))
            {
                BatteryNote = "Kutu İçeriği Pil (Duracell OEM 2'li set)";
            }
        }
        else
        {
            BatteryFormTitle = "Yeni Pil Seti Kaydet (Pil Değiştirildi)";
            BatteryFormSubtitle = "Kontrolcünün pillerini değiştirdiğinizde buradan yeni bir kullanım döngüsü başlatın.";
            BatterySaveButtonText = "💾  Pil Değişimini Kaydet";
            if (BatteryNote.Contains("Kutu İçeriği"))
            {
                BatteryNote = "Duracell Ultra AA (2'li set)";
            }
        }

        // 3. Pil Performans Özeti Metrikleri
        if (performance.ActiveCycle != null)
        {
            ActiveBatteryUsageText = $"{performance.ActiveCycle.DurationFormatted} ({performance.ActiveCycle.SessionCount} oturum)";
        }
        else
        {
            ActiveBatteryUsageText = "0 dk (Henüz oturum yok)";
        }

        if (performance.AverageDurationPerCycle > TimeSpan.Zero)
        {
            AverageBatteryDurationText = SessionTimeCalculator.FormatDurationText(performance.AverageDurationPerCycle);
        }
        else
        {
            AverageBatteryDurationText = "Veri yetersiz (En az 1 tamamlanmış döngü)";
        }

        if (performance.BestCycle != null && performance.BestCycle.TotalUsageDuration > TimeSpan.Zero)
        {
            BestBatterySetText = $"{performance.BestCycle.DurationFormatted} ({performance.BestCycle.BatteryName})";
        }
        else
        {
            BestBatterySetText = "Henüz rekor bulunmuyor";
        }
    }

    public void UpdateCurrentPercentage(
        int percentage,
        DateTimeOffset timestamp,
        BatterySource source,
        BatteryConfidence confidence = BatteryConfidence.Exact,
        bool isConnected = true,
        string? deviceKey = null)
    {
        var targetDeviceKey = !string.IsNullOrEmpty(deviceKey)
            ? deviceKey
            : (_deviceWatcher?.CurrentDevice.DeviceKey ?? "XBOX_CONTROLLER_DYNAMIC");

        // 1. Otomatik pil değişimi tespit motorunu değerlendir (yalnızca AYNI cihazın donanımsal bağlı okumalarında)
        if (source == BatterySource.WindowsApiGatt && isConnected)
        {
            if (_lastKnownPercentageByDevice.TryGetValue(targetDeviceKey, out var prevPct))
            {
                var proposal = _changeDetector.Evaluate(targetDeviceKey, prevPct, percentage, isConnected, timestamp);
                if (proposal != null)
                {
                    _activeProposal = proposal;
                    PendingProposalText = proposal.ReasonMessage;
                    ProposalDetails = $"Eski Seviye: %{proposal.PreviousPercentage} ➔ Yeni Seviye: %{proposal.NewPercentage}";
                    HasPendingProposal = true;
                }
            }
        }

        _lastKnownPercentageByDevice[targetDeviceKey] = percentage;

        // Yalnızca şu an bu cihaz görüntüleniyorsa veya genel özet ("ALL") seçiliyse ekranı güncelle
        var currentFilterKey = SelectedControllerFilter?.DeviceKey ?? "ALL";
        if (currentFilterKey == "ALL" || currentFilterKey == targetDeviceKey)
        {
            CurrentPercentage = $"%{percentage}";

            SourceDescription = source switch
            {
                BatterySource.WindowsApiGatt => "Bluetooth GATT (0x180F)",
                BatterySource.Manual => "Manuel Kullanıcı Girişi",
                BatterySource.XInput => "XInput Telemetrisi",
                _ => "Bilinmiyor"
            };

            ConfidenceDescription = confidence switch
            {
                BatteryConfidence.Exact => "Kademeli Donanım Ölçümü",
                BatteryConfidence.Coarse => "Kaba Seviye",
                _ => "Belirsiz"
            };

            ConfidenceBadge = source == BatterySource.WindowsApiGatt ? "Donanımsal Doğrulanmış" : "Manuel Giriş";

            LastUpdatedText = $"{timestamp.ToLocalTime():dd MMM yyyy HH:mm:ss} ({SourceDescription})";
            BatteryLevelBarWidth = Math.Clamp((percentage / 100.0) * 240, 4, 240);
        }
    }

    [RelayCommand]
    public async Task AcceptProposalAsync()
    {
        if (_activeProposal == null || _batteryRepository == null) return;

        var change = new BatteryChange
        {
            DeviceKey = _activeProposal.DeviceKey,
            ChangedAtUtc = _activeProposal.DetectedAtUtc,
            PreviousPercentage = _activeProposal.PreviousPercentage,
            NewPercentage = _activeProposal.NewPercentage,
            Note = "Otomatik algılandı ve kullanıcı tarafından onaylandı",
            ChangeType = "Otomatik (Onaylandı)"
        };

        await _batteryRepository.RecordBatteryChangeAsync(change);
        _activeProposal = null;
        HasPendingProposal = false;

        await RefreshDataAsync();
        ShowFeedback("✅ Pil değişimi önerisi onaylandı ve geçmişe kaydedildi.");
    }

    [RelayCommand]
    public void DismissProposal()
    {
        _activeProposal = null;
        HasPendingProposal = false;
        ShowFeedback("ℹ️ Pil değişimi önerisi reddedildi. Geçmişe herhangi bir kayıt yazılmadı.");
    }

    [RelayCommand]
    private async Task SaveBatteryChangeAsync()
    {
        if (_batteryRepository == null) return;

        var isFirst = ChangeHistory.Count == 0;
        DateTimeOffset startTimestamp = DateTimeOffset.UtcNow;

        // Eğer bu ilk pil seti ise, geriye dönük ilk oturumun başlangıç zamanını al
        // Böylece gamepad ilk alındığından beri oynanmış tüm oturumlar bu ilk pile sayılır!
        if (isFirst && _sessionRepository != null)
        {
            var pastSessions = await _sessionRepository.GetRecentSessionsAsync(1000);
            var completed = pastSessions.Where(s => s.EndedAtUtc.HasValue).ToList();
            if (completed.Count > 0)
            {
                startTimestamp = completed.Min(s => s.StartedAtUtc);
            }
        }

        var clamped = Math.Clamp(NewBatteryPercentage, 0, 100);
        var targetDeviceKey = (SelectedControllerFilter != null && SelectedControllerFilter.DeviceKey != "ALL")
            ? SelectedControllerFilter.DeviceKey
            : (_deviceWatcher?.CurrentDevice.DeviceKey ?? "XBOX_CONTROLLER_DYNAMIC");

        var prevPct = _lastKnownPercentageByDevice.TryGetValue(targetDeviceKey, out var p) ? p : (int?)null;

        var change = new BatteryChange
        {
            DeviceKey = targetDeviceKey,
            ChangedAtUtc = startTimestamp,
            PreviousPercentage = isFirst ? null : prevPct,
            NewPercentage = clamped,
            Note = string.IsNullOrWhiteSpace(BatteryNote) ? (isFirst ? "Kutu İçeriği Pil" : null) : BatteryNote.Trim(),
            ChangeType = isFirst ? "İlk Kurulum (Fabrika Çıkışı)" : "Manuel"
        };

        await _batteryRepository.RecordBatteryChangeAsync(change);

        // Yeni pil başlangıç seviyesini anlık okuma olarak da kaydet
        await _batteryRepository.RecordBatteryReadingAsync(new BatteryReading
        {
            DeviceKey = change.DeviceKey,
            RecordedAtUtc = change.ChangedAtUtc,
            Percentage = clamped,
            Source = BatterySource.Manual,
            Confidence = BatteryConfidence.Exact,
            Note = isFirst ? $"İlk pil seti: {change.Note}" : $"Yeni pil seti: {change.Note}"
        });

        UpdateCurrentPercentage(clamped, change.ChangedAtUtc, BatterySource.Manual, BatteryConfidence.Exact, true, change.DeviceKey);
        await RefreshDataAsync();

        ShowFeedback(isFirst
            ? "✅ İlk pil seti başarıyla tanımlandı! Oynadığınız tüm geçmiş oturumlar bu pile aktarıldı."
            : "✅ Yeni pil seti başarıyla kaydedildi ve geçmişe eklendi!");
    }

    [RelayCommand]
    private async Task SaveManualPercentageAsync()
    {
        if (_batteryRepository == null) return;

        var clamped = Math.Clamp(ManualPercentageInput, 0, 100);
        var now = DateTimeOffset.UtcNow;
        var targetDeviceKey = (SelectedControllerFilter != null && SelectedControllerFilter.DeviceKey != "ALL")
            ? SelectedControllerFilter.DeviceKey
            : (_deviceWatcher?.CurrentDevice.DeviceKey ?? "XBOX_CONTROLLER_DYNAMIC");

        await _batteryRepository.RecordBatteryReadingAsync(new BatteryReading
        {
            DeviceKey = targetDeviceKey,
            RecordedAtUtc = now,
            Percentage = clamped,
            Source = BatterySource.Manual,
            Confidence = BatteryConfidence.Exact,
            Note = "Kullanıcı manuel seviye girdi"
        });

        UpdateCurrentPercentage(clamped, now, BatterySource.Manual, BatteryConfidence.Exact, true, targetDeviceKey);
        ShowFeedback($"✅ Pil seviyesi manuel olarak %{clamped} olarak güncellendi.");
    }

    [RelayCommand]
    public async Task DeleteBatteryChangeAsync(long id)
    {
        if (_batteryRepository == null) return;

        var success = await _batteryRepository.DeleteBatteryChangeAsync(id);
        if (success)
        {
            await RefreshDataAsync();
            ShowFeedback("🗑️ Pil değişim kaydı başarıyla silindi.");
        }
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        if (_batteryRepository == null) return;

        var changes = await _batteryRepository.GetBatteryChangesAsync(1000);
        if (changes.Count == 0)
        {
            ShowFeedback("⚠️ Dışa aktarılacak pil değişim kaydı bulunamadı.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Pil Değişim Geçmişini Kaydet",
            Filter = "CSV Dosyası (*.csv)|*.csv",
            FileName = $"Pil_Degisimleri_{DateTime.Now:yyyyMMdd_HHmm}.csv"
        };

        if (dialog.ShowDialog() == true)
        {
            var csv = CsvExportService.GenerateBatteryChangesCsv(changes);
            await CsvExportService.SaveCsvFileAsync(dialog.FileName, csv);
            ShowFeedback($"✅ Pil değişim geçmişi '{Path.GetFileName(dialog.FileName)}' olarak kaydedildi!");
        }
    }

    private void ShowFeedback(string message)
    {
        StatusFeedback = message;
        HasFeedback = true;
    }
}
