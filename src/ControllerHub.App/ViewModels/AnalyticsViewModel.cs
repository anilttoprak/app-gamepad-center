using CommunityToolkit.Mvvm.ComponentModel;
using ControllerHub.Core.Interfaces;
using ControllerHub.Core.Services;
using ControllerHub.Infrastructure.Services;

namespace ControllerHub.App.ViewModels;

public partial class AnalyticsViewModel : ViewModelBase
{
    private ISessionRepository? _sessionRepository;
    private IBatteryRepository? _batteryRepository;
    private SessionTrackerService? _sessionTracker;

    public override string Title => "Analizler & İstatistikler";

    [ObservableProperty]
    private string _averageSessionDuration = "0 dk";

    [ObservableProperty]
    private string _longestSession = "Henüz oturum yok";

    [ObservableProperty]
    private string _mostActiveDay = "Henüz veri yok";

    [ObservableProperty]
    private string _mostActiveHour = "Henüz veri yok";

    [ObservableProperty]
    private string _thisWeekTotal = "0 dk";

    [ObservableProperty]
    private string _thisMonthTotal = "0 dk";

    [ObservableProperty]
    private string _thisYearTotal = "0 dk";

    [ObservableProperty]
    private string _batteryChangeCount = "0 kez pil değiştirildi";

    [ObservableProperty]
    private string _estimatedHoursPerBatterySet = "Veri yetersiz (En az 1 değişim gerekli)";

    public void Initialize(
        ISessionRepository sessionRepository,
        IBatteryRepository batteryRepository,
        SessionTrackerService sessionTracker)
    {
        _sessionRepository = sessionRepository;
        _batteryRepository = batteryRepository;
        _sessionTracker = sessionTracker;
    }

    public async Task RefreshDataAsync()
    {
        if (_sessionRepository == null || _batteryRepository == null) return;

        var nowUtc = DateTimeOffset.UtcNow;
        var sessions = await _sessionRepository.GetRecentSessionsAsync(1000);
        var batteryChanges = await _batteryRepository.GetBatteryChangesAsync(100);
        var activeSession = _sessionTracker?.ActiveSession;

        var report = AnalyticsEngine.ComputeAnalytics(
            sessions,
            batteryChanges,
            activeSession,
            TimeZoneInfo.Local,
            nowUtc);

        AverageSessionDuration = SessionTimeCalculator.FormatDurationText(report.AverageSessionDuration);

        if (report.LongestSession != null)
        {
            var longestDur = TimeSpan.FromSeconds(report.LongestSession.DurationSeconds);
            var dateStr = report.LongestSession.StartedAtUtc.ToLocalTime().ToString("dd MMM yyyy");
            LongestSession = $"{SessionTimeCalculator.FormatDurationText(longestDur)} ({dateStr})";
        }
        else
        {
            LongestSession = "Henüz oturum yok";
        }

        if (report.MostActiveDay.HasValue)
        {
            var dayTr = GetTurkishDayName(report.MostActiveDay.Value);
            MostActiveDay = $"{dayTr} (%{report.MostActiveDayPercentage})";
        }
        else
        {
            MostActiveDay = "Henüz veri yok";
        }

        MostActiveHour = report.MostActiveHourRange;
        ThisWeekTotal = SessionTimeCalculator.FormatDurationText(report.ThisWeekTotal);
        ThisMonthTotal = SessionTimeCalculator.FormatDurationText(report.ThisMonthTotal);
        ThisYearTotal = SessionTimeCalculator.FormatDurationText(report.ThisYearTotal);

        BatteryChangeCount = $"{report.TotalBatteryChangesCount} kez";

        if (report.EstimatedHoursPerBatterySet.HasValue)
        {
            EstimatedHoursPerBatterySet = $"~{report.EstimatedHoursPerBatterySet.Value:0.#} sa / set";
        }
        else
        {
            EstimatedHoursPerBatterySet = "Değişim kaydı yok";
        }
    }

    private static string GetTurkishDayName(DayOfWeek dayOfWeek) => dayOfWeek switch
    {
        DayOfWeek.Monday => "Pazartesi",
        DayOfWeek.Tuesday => "Salı",
        DayOfWeek.Wednesday => "Çarşamba",
        DayOfWeek.Thursday => "Perşembe",
        DayOfWeek.Friday => "Cuma",
        DayOfWeek.Saturday => "Cumartesi",
        DayOfWeek.Sunday => "Pazar",
        _ => ""
    };
}
