using System.Windows;
using ControllerHub.App.ViewModels;

namespace ControllerHub.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
