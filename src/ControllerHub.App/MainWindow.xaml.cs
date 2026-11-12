using System.ComponentModel;
using System.Windows;
using ControllerHub.App.Services;
using ControllerHub.App.ViewModels;

namespace ControllerHub.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly SystemTrayManager _trayManager;
    private bool _isExplicitExit = false;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        _trayManager = new SystemTrayManager(this, RequestExit);

        Loaded += OnWindowLoaded;
        Closing += OnWindowClosing;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        _trayManager.Initialize();

        _viewModel.NotificationRequested += (_, notification) =>
        {
            _trayManager.ShowNotification(notification.Title, notification.Message);
        };

        _viewModel.TrayStatusUpdated += (_, status) =>
        {
            _trayManager.UpdateStatus(status);
        };
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (!_isExplicitExit && _viewModel.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            _trayManager.ShowNotification(
                "Controller Hub Arka Planda",
                "Uygulama sistem tepsisinde çalışmaya devam ediyor.");
        }
        else
        {
            PerformExit();
        }
    }

    public void RequestExit()
    {
        _isExplicitExit = true;
        Close();
    }

    private void PerformExit()
    {
        _trayManager.Dispose();
        _viewModel.Dispose();
        Application.Current.Shutdown();
    }
}
