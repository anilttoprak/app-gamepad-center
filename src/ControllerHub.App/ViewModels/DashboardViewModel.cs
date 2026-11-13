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

public partial class DashboardViewModel : ViewModelBase
{
    private ISessionRepository? _sessionRepository;
    private IBatteryRepository? _batteryRepository;
    private SessionTrackerService? _sessionTracker;

    private List<ControllerSession> _cachedSessions = new();

    public override string Title => "Genel Bakış";

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
        SessionTrackerService sessionTracker)
    {
        _sessionRepository = sessionRepository;
        _batteryRepository = batteryRepository;
        _sessionTracker = sessionTracker;

        _sessionTracker.ActiveSessionTicked += OnActiveSessionTicked;
        _sessionTracker.SessionStarted += async (_, _) => await RefreshDataAsync();
        _sessionTracker.SessionEnded += async (_, _) => await RefreshDataAsync();
    }

    public async Task RefreshDataAsync()
    {
        if (_sessionRepository == null) return;

        var nowUtc = DateTimeOffset.UtcNow;
        var activeSession = _sessionTracker?.ActiveSession;
        var activeSessions = _sessionTracker?.ActiveSessions ?? (activeSession != null && activeSession.IsActive ? new[] { activeSession } : (IReadOnlyList<ControllerSession>)Array.Empty<ControllerSession>());

        // 1. Veritabanından tüm oturumları getir (Analiz ve KPI'lar için)
        var sessions = await _sessionRepository.GetRecentSessionsAsync(1000);
        _cachedSessions = sessions.ToList();

        // 2. Toplam Süre & Bugünkü Süre (SessionTimeCalculator ile kesin matematiksel hesap)
        var totalDuration = SessionTimeCalculator.CalculateTotalDurationMulti(_cachedSessions, activeSessions, nowUtc);
        TotalConnectionTime = SessionTimeCalculator.FormatDurationText(totalDuration);

        var todayLocal = DateOnly.FromDateTime(DateTime.Now);
        var todayDuration = SessionTimeCalculator.CalculateDayDurationMulti(_cachedSessions, activeSessions, todayLocal, TimeZoneInfo.Local, nowUtc);
        TodayConnectionTime = SessionTimeCalculator.FormatDurationText(todayDuration);

        // 3. Toplam Oturum Sayısı (Yalnızca tamamlanmış oturumlar + aktif oturum sayısı)
        int completedCount = _cachedSessions.Count(s => s.EndedAtUtc.HasValue);
        int totalCount = completedCount + activeSessions.Count;
        TotalSessionsCount = totalCount.ToString();

        HasSessions = totalCount > 0;
        IsEmptyState = !HasSessions;

        // 4. Son 7 Günlük Kullanım Grafiğini Gerçek Veriden Üret
        GenerateWeeklyActivity(todayLocal, nowUtc, activeSessions);

        // 5. Son Oturumlar Tablosunu Gerçek Veriden Doldur
        PopulateRecentSessions(activeSessions);

        // 6. Son Bilinen Pil Seviyesini Getir
        if (_batteryRepository != null)
        {
            var latestBattery = await _batteryRepository.GetLatestReadingAsync();
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

            var changes = await _batteryRepository.GetBatteryChangesAsync(1);
            BatteryActionButtonText = changes.Count > 0 ? "🔋  Pil Değiştirdim" : "🔋  İlk Pil Setini Tanımla";
        }
    }

    private void GenerateWeeklyActivity(DateOnly todayLocal, DateTimeOffset nowUtc, IReadOnlyList<ControllerSession> activeSessions)
    {
        WeeklyActivity.Clear();

        var dailyValues = new List<(string DayName, double Hours)>();
        double maxDayHours = 1.0;

        // Son 7 gün (6 gün öncesinden bugüne)
        for (int i = 6; i >= 0; i--)
        {
            var date = todayLocal.AddDays(-i);
            var dayDuration = SessionTimeCalculator.CalculateDayDurationMulti(_cachedSessions, activeSessions, date, TimeZoneInfo.Local, nowUtc);
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

    private void PopulateRecentSessions(IReadOnlyList<ControllerSession> activeSessions)
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

        var completedSessions = _cachedSessions.Where(s => s.EndedAtUtc.HasValue).ToList();
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
            var nowUtc = DateTimeOffset.UtcNow;
            var activeSessions = _sessionTracker?.ActiveSessions;
            var activeSession = _sessionTracker?.ActiveSession;

            // Toplam ve bugünkü süreyi canlı sayaçla anlık ilerlet
            var total = SessionTimeCalculator.CalculateTotalDurationMulti(_cachedSessions, activeSessions, nowUtc);
            TotalConnectionTime = SessionTimeCalculator.FormatDurationText(total);

            var todayLocal = DateOnly.FromDateTime(DateTime.Now);
            var today = SessionTimeCalculator.CalculateDayDurationMulti(_cachedSessions, activeSessions, todayLocal, TimeZoneInfo.Local, nowUtc);
            TodayConnectionTime = SessionTimeCalculator.FormatDurationText(today);

            // Tablodaki aktif elemanların süresini güncelle
            if (activeSessions != null && activeSessions.Count > 0)
            {
                for (int i = 0; i < RecentSessions.Count; i++)
                {
                    if (!RecentSessions[i].IsActive) break;
                    if (i < activeSessions.Count)
                    {
                        var actDur = activeSessions[i].GetEffectiveDuration(nowUtc);
                        RecentSessions[i] = RecentSessions[i] with { Duration = SessionTimeCalculator.FormatDurationText(actDur) };
                    }
                }
            }
            else if (RecentSessions.Count > 0 && RecentSessions[0].IsActive)
            {
                RecentSessions[0] = RecentSessions[0] with { Duration = SessionTimeCalculator.FormatDurationText(activeDuration) };
            }
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
