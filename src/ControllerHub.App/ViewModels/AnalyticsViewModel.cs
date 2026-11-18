using System.Collections.ObjectModel;
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

    public ObservableCollection<ControllerFilterOption> ControllerFilterOptions { get; } = new();

    private readonly object _collectionsLock = new();

    public AnalyticsViewModel()
    {
        try
        {
            System.Windows.Data.BindingOperations.EnableCollectionSynchronization(ControllerFilterOptions, _collectionsLock);
        }
        catch { }
    }

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
        void Update()
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

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(Update);
        }
        else
        {
            Update();
        }
    }

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

        var selectedKey = SelectedControllerFilter?.DeviceKey ?? "ALL";
        var filterDeviceKey = selectedKey == "ALL" ? null : selectedKey;

        var nowUtc = DateTimeOffset.UtcNow;
        var sessions = await _sessionRepository.GetRecentSessionsAsync(1000);
        var batteryChanges = await _batteryRepository.GetBatteryChangesAsync(100, filterDeviceKey);
        var activeSession = _sessionTracker?.ActiveSession;

        if (selectedKey != "ALL")
        {
            sessions = sessions.Where(s => s.DeviceKey == selectedKey).ToList();
            batteryChanges = batteryChanges.Where(c => c.DeviceKey == selectedKey).ToList();
            var allActive = _sessionTracker?.ActiveSessions;
            activeSession = allActive?.FirstOrDefault(a => a.DeviceKey == selectedKey);
        }

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

        if (report.TotalBatteryChangesCount > 0)
        {
            BatteryChangeCount = $"{report.TotalBatteryChangesCount} kez";
        }
        else if (batteryChanges.Any(c => c.ChangeType == "İlk Kurulum (Fabrika Çıkışı)"))
        {
            BatteryChangeCount = "0 kez (İlk pil kullanımda)";
        }
        else
        {
            BatteryChangeCount = "0 kez";
        }

        if (report.EstimatedHoursPerBatterySet.HasValue)
        {
            EstimatedHoursPerBatterySet = $"~{report.EstimatedHoursPerBatterySet.Value:0.#} sa / set";
        }
        else
        {
            EstimatedHoursPerBatterySet = "Veri yetersiz (En az 1 değişim gerekli)";
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
