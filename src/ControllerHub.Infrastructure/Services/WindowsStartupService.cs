using Microsoft.Win32;

namespace ControllerHub.Infrastructure.Services;

public class WindowsStartupService
{
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "GamepadCenter";
    private const string LegacyAppName = "ControllerHub";

    public virtual bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
            var value = (key?.GetValue(AppName) as string) ?? (key?.GetValue(LegacyAppName) as string);
            return !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    public virtual void SetStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
            if (key == null) return;

            // Eski kaydı temizle
            try { key.DeleteValue(LegacyAppName, false); } catch { }

            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(AppName, $"\"{exePath}\"");
                }
            }
            else
            {
                try { key.DeleteValue(AppName, false); } catch { }
            }
        }
        catch { }
    }
}
