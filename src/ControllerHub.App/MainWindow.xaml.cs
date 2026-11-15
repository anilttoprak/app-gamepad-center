using System.ComponentModel;
using System.Windows;
using ControllerHub.App.Services;
using ControllerHub.App.ViewModels;

namespace ControllerHub.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly SystemTrayManager _trayManager;
    private readonly bool _isAutostart;
    private bool _isExplicitExit = false;

    public MainWindow() : this(false)
    {
    }

    public MainWindow(bool isAutostart)
    {
        InitializeComponent();
        _isAutostart = isAutostart;
        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        _trayManager = new SystemTrayManager(this, RequestExit);
        _trayManager.Initialize();

        _viewModel.NotificationRequested += (_, notification) =>
        {
            _trayManager.ShowNotification(notification.Title, notification.Message);
        };

        _viewModel.TrayStatusUpdated += (_, status) =>
        {
            _trayManager.UpdateStatus(status);
        };

        Loaded += OnWindowLoaded;
        Closing += OnWindowClosing;

        if (_isAutostart)
        {
            _trayManager.ShowNotification(
                "Gamepad Center Arka Planda Hazır",
                "Kontrolcünüz bağlandığında oturum ve pil durumu otomatik takip edilecektir.");
        }
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (!_isAutostart)
        {
            // Uygulama elle açıldığında pencereyi en öne getir ve odakla
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (!_isExplicitExit && _viewModel.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            _trayManager.ShowNotification(
                "Arka Planda Çalışıyor",
                "Gamepad Center sistem tepsisinde açık kalmaya devam ediyor.");
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
        // 1. Arayüzü ve sistem tepsisi menüsünü ANINDA kaldır
        Hide();
        _trayManager.Dispose();

        // 2. Açık oturumları arka planda kaydedip 500ms içinde süreci tamamen sonlandır
        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                await _viewModel.ShutdownGracefullyAsync(cts.Token);
            }
            catch { }
            finally
            {
                Environment.Exit(0);
            }
        });
    }
}
