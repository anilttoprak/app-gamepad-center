using System.IO;
using Microsoft.Win32;

namespace ControllerHub.Infrastructure.Services;

public class WindowsStartupService
{
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string AppName = "GamepadCenter";
    private const string LegacyAppName = "ControllerHub";
    private const string ShortcutFileName = "Gamepad Center.lnk";
    private const string LegacyShortcutFileName = "ControllerHub.lnk";

    public virtual bool IsStartupEnabled()
    {
        try
        {
            // 1. Windows shell:startup klasöründeki kısayolu kontrol et
            var startupDir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (File.Exists(Path.Combine(startupDir, ShortcutFileName)))
            {
                return true;
            }

            // 2. Kayıt defteri (Run) anahtarını kontrol et
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
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            var startupDir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            var shortcutPath = Path.Combine(startupDir, ShortcutFileName);
            var oldShortcutPath = Path.Combine(startupDir, LegacyShortcutFileName);

            if (enable)
            {
                // 1. HKCU Run Kaydı (Argümanla birlikte)
                try
                {
                    using var runKey = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
                    if (runKey != null)
                    {
                        try { runKey.DeleteValue(LegacyAppName, false); } catch { }
                        runKey.SetValue(AppName, $"\"{exePath}\" --autostart");
                    }
                }
                catch { }

                // 2. Windows 10/11 Görev Yöneticisi StartupApproved Kaydı (0x02 = Etkin)
                try
                {
                    using var approvedKey = Registry.CurrentUser.OpenSubKey(ApprovedRegistryKey, true);
                    if (approvedKey != null)
                    {
                        try { approvedKey.DeleteValue(LegacyAppName, false); } catch { }
                        var enabledBytes = new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                        approvedKey.SetValue(AppName, enabledBytes, RegistryValueKind.Binary);
                    }
                }
                catch { }

                // 3. shell:startup Klasörüne Kısayol Oluştur (Çift Katmanlı Güvence)
                try
                {
                    if (File.Exists(oldShortcutPath)) File.Delete(oldShortcutPath);

                    Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                    if (shellType != null)
                    {
                        dynamic? shell = Activator.CreateInstance(shellType);
                        if (shell != null)
                        {
                            dynamic shortcut = shell.CreateShortcut(shortcutPath);
                            shortcut.TargetPath = exePath;
                            shortcut.Arguments = "--autostart";
                            shortcut.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
                            shortcut.IconLocation = $"{exePath},0";
                            shortcut.Description = "Gamepad Center — Otomatik Başlatma";
                            shortcut.Save();
                        }
                    }
                }
                catch { }
            }
            else
            {
                // 1. Kayıt defterini temizle
                try
                {
                    using var runKey = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
                    if (runKey != null)
                    {
                        try { runKey.DeleteValue(AppName, false); } catch { }
                        try { runKey.DeleteValue(LegacyAppName, false); } catch { }
                    }
                }
                catch { }

                try
                {
                    using var approvedKey = Registry.CurrentUser.OpenSubKey(ApprovedRegistryKey, true);
                    if (approvedKey != null)
                    {
                        try { approvedKey.DeleteValue(AppName, false); } catch { }
                        try { approvedKey.DeleteValue(LegacyAppName, false); } catch { }
                    }
                }
                catch { }

                // 2. Startup klasöründeki kısayolu temizle
                try
                {
                    if (File.Exists(shortcutPath)) File.Delete(shortcutPath);
                    if (File.Exists(oldShortcutPath)) File.Delete(oldShortcutPath);
                }
                catch { }
            }
        }
        catch { }
    }
}
