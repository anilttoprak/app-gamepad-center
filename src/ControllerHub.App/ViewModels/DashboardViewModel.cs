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

public record DayActivityItem(string DayName, double Hours, double MaxHours)
{
    public double BarHeight => Math.Clamp((Hours / Math.Max(0.5, MaxHours)) * 110, 6, 110);
    public string DisplayText => Hours >= 1.0 ? $"{Hours:0.#} sa" : (Hours > 0 ? $"{(int)(Hours * 60)} dk" : "0");
}

public record RecentSessionItem(string Date, string TimeRange, string Duration, string Reason, bool IsActive);

public record ControllerFilterOption(string DeviceKey, string DisplayName);

public partial class DashboardViewModel : ViewModelBase
{
    private ISessionRepository? _sessionRepository;
    private IBatteryRepository? _batteryRepository;
    private IControllerMetadataRepository? _metadataRepository;
    private SessionTrackerService? _sessionTracker;

    private List<ControllerSession> _cachedSessions = new();

    public override string Title => "Genel Bakış";

    public ObservableCollection<ControllerFilterOption> ControllerFilterOptions { get; } = new();

    [ObservableProperty]
    private ControllerFilterOption? _selectedControllerFilter;

    partial void OnSelectedControllerFilterChanged(ControllerFilterOption? value)
    {
        RecalculateFilteredMetrics();
    }

    [ObservableProperty]
    private string _totalConnectionTime = "0 dk";

    [ObservableProperty]
    private string _todayConnectionTime = "0 dk";

    [ObservableProperty]
    private string _batteryLevelText = "Bilinmiyor";

    [ObservableProperty]
    private string _batterySourceText = "Bluetooth GATT (0x180F)";

    [ObservableProperty]
    private string _batteryConfidenceBadge = "Kademeli Donanım Ölçümü";

    [ObservableProperty]
    private bool _hasBatteryProposal = false;

    [ObservableProperty]
    private string _batteryProposalText = "";

    [ObservableProperty]
    private string _batteryActionButtonText = "🔋  İlk Pil Setini Tanımla";

    [ObservableProperty]
    private string _statusFeedback = "";

    [ObservableProperty]
    private bool _hasFeedback = false;

    [ObservableProperty]
    private string _totalSessionsCount = "0";

    [ObservableProperty]
    private string _activeControllerName = "Xbox Wireless Controller";

    [ObservableProperty]
    private string _connectionStateText = "⚪ Bağlı Değil";

    [ObservableProperty]
    private bool _isConnected = false;

    [ObservableProperty]
    private bool _hasSessions = false;

    [ObservableProperty]
    private bool _isEmptyState = true;

    public ObservableCollection<DayActivityItem> WeeklyActivity { get; } = new();
    public ObservableCollection<RecentSessionItem> RecentSessions { get; } = new();

    public event EventHandler<string>? NavigationRequested;

    [RelayCommand]
    private void ViewAllSessions()
    {
        NavigationRequested?.Invoke(this, "Sessions");
    }

    [RelayCommand]
    private void NavigateBattery()
    {
        NavigationRequested?.Invoke(this, "Battery");
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        var completedSessions = _cachedSessions.Where(s => s.EndedAtUtc.HasValue).ToList();
        if (completedSessions.Count == 0)
        {
            ShowFeedback("⚠️ Dışa aktarılacak kayıtlı oturum bulunamadı.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Oturum Geçmişini Kaydet",
            Filter = "CSV Dosyası (*.csv)|*.csv",
            FileName = $"Oturum_Gecmisi_{DateTime.Now:yyyyMMdd_HHmm}.csv"
        };

        if (dialog.ShowDialog() == true)
        {
            var csv = CsvExportService.GenerateSessionsCsv(completedSessions);
            await CsvExportService.SaveCsvFileAsync(dialog.FileName, csv);
            ShowFeedback($"✅ Oturum geçmişi '{Path.GetFileName(dialog.FileName)}' olarak kaydedildi!");
        }
    }

    private void ShowFeedback(string message)
    {
        StatusFeedback = message;
        HasFeedback = true;
    }

    public void Initialize(
        ISessionRepository sessionRepository,
        IBatteryRepository batteryRepository,
        SessionTrackerService sessionTracker,
        IControllerMetadataRepository? metadataRepository = null)
    {
        _sessionRepository = sessionRepository;
        _batteryRepository = batteryRepository;
        _sessionTracker = sessionTracker;
        _metadataRepository = metadataRepository;

        _sessionTracker.ActiveSessionTicked += OnActiveSessionTicked;
        _sessionTracker.SessionStarted += async (_, _) => await RefreshDataAsync();
        _sessionTracker.SessionEnded += async (_, _) => await RefreshDataAsync();
    }

    public async Task RefreshDataAsync()
    {
        if (_sessionRepository == null) return;

        // 1. Veritabanından tüm oturumları getir (Analiz ve KPI'lar için)
        var sessions = await _sessionRepository.GetRecentSessionsAsync(1000);
        _cachedSessions = sessions.ToList();

        // 2. Kontrolcü filtreleme seçeneklerini güncelle
        await UpdateFilterOptionsAsync();

        // 3. Seçili filtreye göre metrikleri hesapla
        RecalculateFilteredMetrics();
    }

    private async Task UpdateFilterOptionsAsync()
    {
        var customNames = _metadataRepository != null
            ? await _metadataRepository.GetAllCustomNamesAsync()
            : new Dictionary<string, string>();

        var currentKey = SelectedControllerFilter?.DeviceKey ?? "ALL";

        var deviceKeys = _cachedSessions.Select(s => s.DeviceKey)
            .Concat(_sessionTracker?.ActiveSessions.Select(a => a.DeviceKey) ?? Enumerable.Empty<string>())
            .Where(k => !string.IsNullOrEmpty(k))
            .Distinct()
            .ToList();

        ControllerFilterOptions.Clear();
        ControllerFilterOptions.Add(new ControllerFilterOption("ALL", "🔘 Tüm Kontrolcüler (Genel Özet)"));

        foreach (var key in deviceKeys)
        {
            string name;
            if (customNames.TryGetValue(key, out var alias) && !string.IsNullOrWhiteSpace(alias))
            {
                name = alias;
            }
            else if (key.StartsWith("XINPUT"))
            {
                name = "USB Kablolu Oyun Kolu";
            }
            else
            {
                name = "Xbox Wireless Controller";
            }

            ControllerFilterOptions.Add(new ControllerFilterOption(key, $"🎮 {name}"));
        }

        SelectedControllerFilter = ControllerFilterOptions.FirstOrDefault(o => o.DeviceKey == currentKey)
            ?? ControllerFilterOptions.First();
    }

    private void RecalculateFilteredMetrics()
    {
        if (_sessionRepository == null) return;

        var nowUtc = DateTimeOffset.UtcNow;
        var activeSession = _sessionTracker?.ActiveSession;
        var allActive = _sessionTracker?.ActiveSessions ?? (activeSession != null && activeSession.IsActive ? new[] { activeSession } : (IReadOnlyList<ControllerSession>)Array.Empty<ControllerSession>());

        var filterKey = SelectedControllerFilter?.DeviceKey ?? "ALL";

        var filteredSessions = filterKey == "ALL"
            ? _cachedSessions
            : _cachedSessions.Where(s => s.DeviceKey == filterKey).ToList();

        var filteredActive = filterKey == "ALL"
            ? allActive
            : allActive.Where(s => s.DeviceKey == filterKey).ToList();

        // 1. Toplam Süre & Bugünkü Süre
        var totalDuration = SessionTimeCalculator.CalculateTotalDurationMulti(filteredSessions, filteredActive, nowUtc);
        TotalConnectionTime = SessionTimeCalculator.FormatDurationText(totalDuration);

        var todayLocal = DateOnly.FromDateTime(DateTime.Now);
        var todayDuration = SessionTimeCalculator.CalculateDayDurationMulti(filteredSessions, filteredActive, todayLocal, TimeZoneInfo.Local, nowUtc);
        TodayConnectionTime = SessionTimeCalculator.FormatDurationText(todayDuration);

        // 2. Toplam Oturum Sayısı
        int completedCount = filteredSessions.Count(s => s.EndedAtUtc.HasValue);
        int totalCount = completedCount + filteredActive.Count;
        TotalSessionsCount = totalCount.ToString();

        HasSessions = totalCount > 0;
        IsEmptyState = !HasSessions;

        // 3. Son 7 Günlük Kullanım Grafiği
        GenerateWeeklyActivity(todayLocal, nowUtc, filteredActive, filteredSessions);

        // 4. Son Oturumlar Tablosu
        PopulateRecentSessions(filteredActive, filteredSessions);

        // 5. Pil Bilgisi
        _ = UpdateBatteryInfoAsync(filterKey);
    }

    private async Task UpdateBatteryInfoAsync(string filterKey)
    {
        if (_batteryRepository == null) return;

        var latestBattery = filterKey == "ALL"
            ? await _batteryRepository.GetLatestReadingAsync()
            : await _batteryRepository.GetLatestReadingAsync(filterKey);

        if (latestBattery?.Percentage != null)
        {
            BatteryLevelText = $"%{latestBattery.Percentage.Value}";
            BatterySourceText = latestBattery.Source == BatterySource.WindowsApiGatt
                ? "Bluetooth GATT (0x180F)"
                : (latestBattery.Source == BatterySource.Manual ? "Manuel Giriş" : "XInput");
            BatteryConfidenceBadge = latestBattery.Confidence == BatteryConfidence.Exact
                ? "Kademeli Donanım Ölçümü"
                : "Kaba Hassasiyet";
        }
        else if (filterKey.StartsWith("XINPUT"))
        {
            BatteryLevelText = "🔌";
            BatterySourceText = "USB Sürekli Güç (Kablolu)";
            BatteryConfidenceBadge = "Kablolu Bağlantı";
        }
        else
        {
            BatteryLevelText = "Bilinmiyor";
            BatterySourceText = "Ölçüm Bekleniyor";
            BatteryConfidenceBadge = "-";
        }

        var changes = await _batteryRepository.GetBatteryChangesAsync(1);
        BatteryActionButtonText = changes.Count > 0 ? "🔋  Pil Değiştirdim" : "🔋  İlk Pil Setini Tanımla";
    }

    private void GenerateWeeklyActivity(
        DateOnly todayLocal,
        DateTimeOffset nowUtc,
        IReadOnlyList<ControllerSession> activeSessions,
        IReadOnlyList<ControllerSession> sessionsToUse)
    {
        WeeklyActivity.Clear();

        var dailyValues = new List<(string DayName, double Hours)>();
        double maxDayHours = 1.0;

        // Son 7 gün (6 gün öncesinden bugüne)
        for (int i = 6; i >= 0; i--)
        {
            var date = todayLocal.AddDays(-i);
            var dayDuration = SessionTimeCalculator.CalculateDayDurationMulti(sessionsToUse, activeSessions, date, TimeZoneInfo.Local, nowUtc);
            var hours = dayDuration.TotalHours;

            if (hours > maxDayHours)
            {
                maxDayHours = hours;
            }

            var dayName = i == 0 ? "Bugün" : GetTurkishDayShortName(date.DayOfWeek);
            dailyValues.Add((dayName, hours));
        }

        foreach (var (dayName, hours) in dailyValues)
        {
            WeeklyActivity.Add(new DayActivityItem(dayName, hours, maxDayHours));
        }
    }

    private void PopulateRecentSessions(
        IReadOnlyList<ControllerSession> activeSessions,
        IReadOnlyList<ControllerSession> sessionsToUse)
    {
        RecentSessions.Clear();

        foreach (var act in activeSessions)
        {
            if (act != null && act.IsActive)
            {
                var activeDuration = act.GetEffectiveDuration(DateTimeOffset.UtcNow);
                var deviceLabel = act.DeviceKey.Contains("XINPUT")
                    ? "Aktif Kol (Kablolu/USB)"
                    : (activeSessions.Count > 1 ? $"Aktif Kol ({act.DeviceName})" : "Aktif Oturum");

                RecentSessions.Add(new RecentSessionItem(
                    Date: "Bugün",
                    TimeRange: $"{act.StartedAtUtc.ToLocalTime():HH:mm} - Devam Ediyor",
                    Duration: SessionTimeCalculator.FormatDurationText(activeDuration),
                    Reason: deviceLabel,
                    IsActive: true));
            }
        }

        var completedSessions = sessionsToUse.Where(s => s.EndedAtUtc.HasValue).ToList();
        int remainingSlots = Math.Max(1, 5 - RecentSessions.Count);
        foreach (var s in completedSessions.Take(remainingSlots))
        {
            var startLocal = s.StartedAtUtc.ToLocalTime();
            var endLocal = s.EndedAtUtc!.Value.ToLocalTime();

            var dateStr = startLocal.Date == DateTime.Today ? "Bugün" :
                          startLocal.Date == DateTime.Today.AddDays(-1) ? "Dün" :
                          startLocal.ToString("dd MMM yyyy");

            var timeRangeStr = $"{startLocal:HH:mm} - {endLocal:HH:mm}";

            var reasonStr = s.EndReason switch
            {
                SessionEndReason.Disconnected => "Kullanıcı Kapattı / Koptu",
                SessionEndReason.AppExit => "Uygulama Kapanışı",
                SessionEndReason.Recovery => "Çökme / Kurtarma",
                _ => "Bilinmiyor"
            };

            RecentSessions.Add(new RecentSessionItem(
                Date: dateStr,
                TimeRange: timeRangeStr,
                Duration: SessionTimeCalculator.FormatDurationText(TimeSpan.FromSeconds(s.DurationSeconds)),
                Reason: reasonStr,
                IsActive: false));
        }
    }

    private void OnActiveSessionTicked(object? sender, TimeSpan activeDuration)
    {
        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
        {
            RecalculateFilteredMetrics();
        });
    }

    private static string GetTurkishDayShortName(DayOfWeek dayOfWeek) => dayOfWeek switch
    {
        DayOfWeek.Monday => "Pzt",
        DayOfWeek.Tuesday => "Sal",
        DayOfWeek.Wednesday => "Çar",
        DayOfWeek.Thursday => "Per",
        DayOfWeek.Friday => "Cum",
        DayOfWeek.Saturday => "Cmt",
        DayOfWeek.Sunday => "Paz",
        _ => ""
    };
}
