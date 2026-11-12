using CommunityToolkit.Mvvm.ComponentModel;

namespace ControllerHub.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    public override string Title => "Ayarlar";

    [ObservableProperty]
    private bool _startWithWindows = true;

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private bool _notificationsEnabled = true;

    [ObservableProperty]
    private string _databasePath = @"%LocalAppData%\ControllerHub\controllerhub.db";

    [ObservableProperty]
    private string _appVersion = "1.0.0 (Aşama 1 UI Prototipi)";

    [ObservableProperty]
    private string _trackedControllerName = "Microsoft Xbox Wireless Controller (Model 1914)";
}
