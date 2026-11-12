using CommunityToolkit.Mvvm.ComponentModel;

namespace ControllerHub.App.ViewModels;

public partial class AnalyticsViewModel : ViewModelBase
{
    public override string Title => "Analizler & İstatistikler";

    [ObservableProperty]
    private string _averageSessionDuration = "1 sa 32 dk";

    [ObservableProperty]
    private string _longestSession = "4 sa 15 dk (01 Kas)";

    [ObservableProperty]
    private string _mostActiveDay = "Cumartesi (Haftanın %28'i)";

    [ObservableProperty]
    private string _mostActiveHour = "20:00 - 23:00";

    [ObservableProperty]
    private string _thisWeekTotal = "14 sa 50 dk";

    [ObservableProperty]
    private string _thisMonthTotal = "42 sa 15 dk";

    [ObservableProperty]
    private string _batteryChangeCount = "2 kez pil değiştirildi";

    [ObservableProperty]
    private string _estimatedHoursPerBatterySet = "~25 saat / set";
}
