using System.Windows;
using ControllerHub.App.ViewModels;

namespace ControllerHub.App;

public partial class App : Application
{
    protected override void OnExit(ExitEventArgs e)
    {
        if (MainWindow?.DataContext is MainViewModel mainVm)
        {
            mainVm.Dispose();
        }
        base.OnExit(e);
    }
}
