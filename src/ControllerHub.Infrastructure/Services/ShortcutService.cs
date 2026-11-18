using System;
using System.IO;

namespace ControllerHub.Infrastructure.Services;

public static class ShortcutService
{
    private const string ShortcutFileName = "Gamepad Center.lnk";
    private const string LegacyShortcutFileName = "ControllerHub.lnk";

    public static bool DesktopShortcutExists()
    {
        try
        {
            var desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            return File.Exists(Path.Combine(desktopDir, ShortcutFileName));
        }
        catch
        {
            return false;
        }
    }

    public static bool CreateDesktopShortcut()
    {
        try
        {
            var desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!Directory.Exists(desktopDir))
            {
                Directory.CreateDirectory(desktopDir);
            }

            // Eski ControllerHub kısayolunu temizle
            try
            {
                var oldLnk = Path.Combine(desktopDir, LegacyShortcutFileName);
                if (File.Exists(oldLnk)) File.Delete(oldLnk);
            }
            catch { }

            var shortcutPath = Path.Combine(desktopDir, ShortcutFileName);
            return CreateShortcutInternal(shortcutPath);
        }
        catch
        {
            return false;
        }
    }

    public static bool EnsureStartMenuShortcut()
    {
        try
        {
            var programsDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            if (!Directory.Exists(programsDir)) return false;

            // Eski ControllerHub kısayolunu temizle
            try
            {
                var oldLnk = Path.Combine(programsDir, LegacyShortcutFileName);
                if (File.Exists(oldLnk)) File.Delete(oldLnk);
            }
            catch { }

            var shortcutPath = Path.Combine(programsDir, ShortcutFileName);
            return CreateShortcutInternal(shortcutPath);
        }
        catch
        {
            return false;
        }
    }

    private static bool CreateShortcutInternal(string shortcutPath)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return false;

        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null) return false;

        dynamic? shell = Activator.CreateInstance(shellType);
        if (shell == null) return false;

        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = exePath;
        shortcut.IconLocation = $"{exePath},0";
        shortcut.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
        shortcut.Description = "Gamepad Center — Evrensel Kontrol Merkezi";
        shortcut.Save();
        return true;
    }
}
