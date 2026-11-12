using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ControllerHub.App.ViewModels;

public record MockDayUsage(string DayName, double Hours, double MaxHours = 4.0)
{
    public double BarHeight => Math.Clamp((Hours / MaxHours) * 110, 8, 110);
    public string DisplayText => $"{Hours:0.#} sa";
}

public record MockRecentSession(string Date, string TimeRange, string Duration, string Reason, bool IsActive);

public partial class DashboardViewModel : ViewModelBase
{
    public override string Title => "Genel Bakış";

    [ObservableProperty]
    private string _totalConnectionTime = "42 sa 15 dk";

    [ObservableProperty]
    private string _todayConnectionTime = "1 sa 45 dk";

    [ObservableProperty]
    private string _batteryLevelText = "%85";

    [ObservableProperty]
    private string _totalSessionsCount = "28";

    [ObservableProperty]
    private string _activeControllerName = "Xbox Wireless Controller (9. Nesil)";

    [ObservableProperty]
    private string _connectionStateText = "Bağlı Değil";

    [ObservableProperty]
    private bool _isConnected = false;

    public ObservableCollection<MockDayUsage> WeeklyActivity { get; } = new()
    {
        new("Pzt", 1.2),
        new("Sal", 2.0),
        new("Çar", 0.5),
        new("Per", 1.8),
        new("Cum", 3.2),
        new("Cmt", 3.8),
        new("Paz", 2.4)
    };

    public ObservableCollection<MockRecentSession> RecentSessions { get; } = new()
    {
        new("Bugün", "18:20 - 20:05", "1 sa 45 dk", "Kullanıcı Kapattı", false),
        new("Dün", "21:10 - 23:40", "2 sa 30 dk", "Kullanıcı Kapattı", false),
        new("10 Kas", "19:00 - 19:45", "45 dk", "Bağlantı Kesildi", false),
        new("09 Kas", "15:30 - 18:00", "2 sa 30 dk", "Kullanıcı Kapattı", false)
    };
}
