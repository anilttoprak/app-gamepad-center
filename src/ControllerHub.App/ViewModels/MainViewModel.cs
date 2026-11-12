using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ControllerHub.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public DashboardViewModel DashboardVM { get; } = new();
    public SessionsViewModel SessionsVM { get; } = new();
    public AnalyticsViewModel AnalyticsVM { get; } = new();
    public BatteryViewModel BatteryVM { get; } = new();
    public SettingsViewModel SettingsVM { get; } = new();

    [ObservableProperty]
    private ViewModelBase _currentView;

    [ObservableProperty]
    private string _activeControllerName = "Xbox Wireless Controller";

    [ObservableProperty]
    private string _connectionStatusBadge = "Bağlı Değil (Prototip)";

    [ObservableProperty]
    private bool _isDeviceConnected = false;

    public MainViewModel()
    {
        _currentView = DashboardVM;
    }

    [RelayCommand]
    private void Navigate(string viewName)
    {
        CurrentView = viewName switch
        {
            "Dashboard" => DashboardVM,
            "Sessions" => SessionsVM,
            "Analytics" => AnalyticsVM,
            "Battery" => BatteryVM,
            "Settings" => SettingsVM,
            _ => DashboardVM
        };
    }
}
