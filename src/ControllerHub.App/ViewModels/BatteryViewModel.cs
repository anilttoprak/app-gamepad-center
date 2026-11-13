using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControllerHub.Core.Interfaces;
using ControllerHub.Core.Models;
using ControllerHub.Core.Services;
using ControllerHub.Infrastructure.Export;
using Microsoft.Win32;

namespace ControllerHub.App.ViewModels;

public record BatteryChangeDisplayItem(
    long Id,
    string Date,
    string PreviousLevelText,
    string NewLevelText,
    string Note,
    string Source);

public partial class BatteryViewModel : ViewModelBase
{
    private IBatteryRepository? _batteryRepository;
    private IDeviceWatcherService? _deviceWatcher;
    private int? _lastKnownPercentage;
    private readonly BatteryChangeDetector _changeDetector = new();
    private BatteryChangeProposal? _activeProposal;

    public override string Title => "Pil Yönetimi";

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
    private string _batteryNote = "Duracell Ultra AA (2'li set)";

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

    public void Initialize(IBatteryRepository batteryRepository, IDeviceWatcherService deviceWatcher)
    {
        _batteryRepository = batteryRepository;
        _deviceWatcher = deviceWatcher;
    }

    public async Task RefreshDataAsync()
    {
        if (_batteryRepository == null) return;

        // 1. Son bilinen pil okumasını getir
        var latestReading = await _batteryRepository.GetLatestReadingAsync();
        if (latestReading?.Percentage != null)
        {
            UpdateCurrentPercentage(
                latestReading.Percentage.Value,
                latestReading.RecordedAtUtc,
                latestReading.Source,
                latestReading.Confidence,
                _deviceWatcher?.CurrentDevice.IsConnected ?? false);
        }

        // 2. Pil değişim geçmişini veritabanından çek
        var changes = await _batteryRepository.GetBatteryChangesAsync(100);
        ChangeHistory.Clear();

        foreach (var c in changes)
        {
            var prevText = c.PreviousPercentage.HasValue ? $"%{c.PreviousPercentage.Value}" : "-";
            var newText = c.NewPercentage.HasValue ? $"%{c.NewPercentage.Value}" : "-";
            var dateText = c.ChangedAtUtc.ToLocalTime().ToString("dd MMM yyyy HH:mm");

            ChangeHistory.Add(new BatteryChangeDisplayItem(
                c.Id,
                dateText,
                prevText,
                newText,
                string.IsNullOrWhiteSpace(c.Note) ? "Not belirtilmedi" : c.Note,
                c.ChangeType));
        }

        HasChanges = ChangeHistory.Count > 0;
        IsEmptyChanges = !HasChanges;
    }

    public void UpdateCurrentPercentage(
        int percentage,
        DateTimeOffset timestamp,
        BatterySource source,
        BatteryConfidence confidence = BatteryConfidence.Exact,
        bool isConnected = true)
    {
        // 1. Otomatik pil değişimi tespit motorunu değerlendir (yalnızca donanımsal bağlı okumalarda)
        if (source == BatterySource.WindowsApiGatt && isConnected)
        {
            var deviceKey = _deviceWatcher?.CurrentDevice.DeviceKey ?? "XBOX_CONTROLLER_DYNAMIC";
            var proposal = _changeDetector.Evaluate(deviceKey, _lastKnownPercentage, percentage, isConnected, timestamp);
            if (proposal != null)
            {
                _activeProposal = proposal;
                PendingProposalText = proposal.ReasonMessage;
                ProposalDetails = $"Eski Seviye: %{proposal.PreviousPercentage} ➔ Yeni Seviye: %{proposal.NewPercentage}";
                HasPendingProposal = true;
            }
        }

        _lastKnownPercentage = percentage;
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

        var clamped = Math.Clamp(NewBatteryPercentage, 0, 100);
        var change = new BatteryChange
        {
            DeviceKey = _deviceWatcher?.CurrentDevice.DeviceKey ?? "XBOX_CONTROLLER_DYNAMIC",
            ChangedAtUtc = DateTimeOffset.UtcNow,
            PreviousPercentage = _lastKnownPercentage,
            NewPercentage = clamped,
            Note = string.IsNullOrWhiteSpace(BatteryNote) ? null : BatteryNote.Trim(),
            ChangeType = "Manuel"
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
            Note = $"Yeni pil seti: {change.Note}"
        });

        UpdateCurrentPercentage(clamped, change.ChangedAtUtc, BatterySource.Manual, BatteryConfidence.Exact, true);
        await RefreshDataAsync();

        ShowFeedback("✅ Yeni pil seti başarıyla kaydedildi ve geçmişe eklendi!");
    }

    [RelayCommand]
    private async Task SaveManualPercentageAsync()
    {
        if (_batteryRepository == null) return;

        var clamped = Math.Clamp(ManualPercentageInput, 0, 100);
        var now = DateTimeOffset.UtcNow;

        await _batteryRepository.RecordBatteryReadingAsync(new BatteryReading
        {
            DeviceKey = _deviceWatcher?.CurrentDevice.DeviceKey ?? "XBOX_CONTROLLER_DYNAMIC",
            RecordedAtUtc = now,
            Percentage = clamped,
            Source = BatterySource.Manual,
            Confidence = BatteryConfidence.Exact,
            Note = "Kullanıcı manuel seviye girdi"
        });

        UpdateCurrentPercentage(clamped, now, BatterySource.Manual, BatteryConfidence.Exact, true);
        ShowFeedback($"✅ Pil seviyesi manuel olarak %{clamped} olarak güncellendi.");
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
