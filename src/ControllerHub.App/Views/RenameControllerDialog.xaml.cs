using System.Windows;

namespace ControllerHub.App.Views;

public partial class RenameControllerDialog : Window
{
    public string CustomName { get; private set; } = string.Empty;

    public RenameControllerDialog(string hardwareName, string currentCustomName)
    {
        InitializeComponent();
        TxtHardwareInfo.Text = hardwareName;
        TxtCustomName.Text = currentCustomName;
        TxtCustomName.Focus();
        TxtCustomName.SelectAll();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var trimmed = TxtCustomName.Text.Trim();
        CustomName = trimmed.Length > 50 ? trimmed[..50] : trimmed;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
