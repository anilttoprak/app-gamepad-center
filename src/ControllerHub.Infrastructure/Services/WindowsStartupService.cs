using System.IO;
using Microsoft.Win32;

namespace ControllerHub.Infrastructure.Services;

public class WindowsStartupService
{
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string AppName = "Gamepad Center";
    private static readonly string[] LegacyAppNames = new[] { "GamepadCenter", "ControllerHub" };
    private const string ShortcutFileName = "Gamepad Center.lnk";
    private const string LegacyShortcutFileName = "ControllerHub.lnk";

    public virtual bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
            if (key == null) return false;

            if (key.GetValue(AppName) is string val && !string.IsNullOrWhiteSpace(val))
            {
                return true;
            }

            foreach (var legacy in LegacyAppNames)
            {
                if (key.GetValue(legacy) is string legVal && !string.IsNullOrWhiteSpace(legVal))
                {
                    return true;
                }
            }

            return false;
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

            // Başlangıç klasöründeki (.lnk) kısayolları her halükarda temizle 
            // (Görev Yöneticisi'nde çift kopya oluşmaması için tek kaynak olarak Kayıt Defteri kullanılır)
            try
            {
                if (File.Exists(shortcutPath)) File.Delete(shortcutPath);
                if (File.Exists(oldShortcutPath)) File.Delete(oldShortcutPath);
            }
            catch { }

            if (enable)
            {
                // 1. HKCU Run Kaydı (Argümanla birlikte)
                try
                {
                    using var runKey = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
                    if (runKey != null)
                    {
                        foreach (var legacy in LegacyAppNames)
                        {
                            try { runKey.DeleteValue(legacy, false); } catch { }
                        }
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
                        foreach (var legacy in LegacyAppNames)
                        {
                            try { approvedKey.DeleteValue(legacy, false); } catch { }
                        }
                        var enabledBytes = new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                        approvedKey.SetValue(AppName, enabledBytes, RegistryValueKind.Binary);
                    }
                }
                catch { }
            }
            else
            {
                // Kayıt defterini temizle
                try
                {
                    using var runKey = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
                    if (runKey != null)
                    {
                        try { runKey.DeleteValue(AppName, false); } catch { }
                        foreach (var legacy in LegacyAppNames)
                        {
                            try { runKey.DeleteValue(legacy, false); } catch { }
                        }
                    }
                }
                catch { }

                try
                {
                    using var approvedKey = Registry.CurrentUser.OpenSubKey(ApprovedRegistryKey, true);
                    if (approvedKey != null)
                    {
                        try { approvedKey.DeleteValue(AppName, false); } catch { }
                        foreach (var legacy in LegacyAppNames)
                        {
                            try { approvedKey.DeleteValue(legacy, false); } catch { }
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
    }
}
