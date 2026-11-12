using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControllerHub.Core.Interfaces;
using ControllerHub.Core.Models;
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

    public override string Title => "Pil Yönetimi";

    [ObservableProperty]
    private string _currentPercentage = "Bilinmiyor";

    [ObservableProperty]
    private string _lastUpdatedText = "Pil okuması bekleniyor...";

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

    public void Initialize(IBatteryRepository batteryRepository, IDeviceWatcherService deviceWatcher)
    {
        _batteryRepository = batteryRepository;
        _deviceWatcher = deviceWatcher;
    }

    public async Task RefreshDataAsync()
    {
        if (_batteryRepository == null) return;

        // 1. Son bilinen pil okumasını getir
        var latestReading = await _batteryRepository.GetLatestReadingAsync("XBOX_CONTROLLER_DYNAMIC");
        if (latestReading?.Percentage != null)
        {
            UpdateCurrentPercentage(latestReading.Percentage.Value, latestReading.RecordedAtUtc, latestReading.Source);
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

    public void UpdateCurrentPercentage(int percentage, DateTimeOffset timestamp, BatterySource source)
    {
        _lastKnownPercentage = percentage;
        CurrentPercentage = $"%{percentage}";

        var sourceText = source == BatterySource.WindowsApiGatt ? "Bluetooth GATT (0x180F)" :
                         source == BatterySource.Manual ? "Manuel Giriş" : "XInput";

        LastUpdatedText = $"{timestamp.ToLocalTime():dd MMM yyyy HH:mm:ss} ({sourceText})";
        BatteryLevelBarWidth = Math.Clamp((percentage / 100.0) * 240, 4, 240);
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

        UpdateCurrentPercentage(clamped, change.ChangedAtUtc, BatterySource.Manual);
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

        UpdateCurrentPercentage(clamped, now, BatterySource.Manual);
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
