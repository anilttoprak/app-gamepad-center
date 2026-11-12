namespace ControllerHub.Core.Models;

public class AppSettings
{
    public bool StartWithWindows { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool NotificationsEnabled { get; set; } = true;
    public string SelectedDeviceKey { get; set; } = string.Empty;
    public string Theme { get; set; } = "Light";
}
