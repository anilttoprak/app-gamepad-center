using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using ControllerHub.App.ViewModels;

namespace ControllerHub.App;

public partial class App : Application
{
    private const string AppId = "GamepadCenter.App";

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

    public App()
    {
        try
        {
            SetCurrentProcessExplicitAppUserModelID(AppId);
            EnsureStartMenuShortcut();
        }
        catch { }

        DispatcherUnhandledException += (s, e) =>
        {
            LogCrash("DispatcherUnhandledException", e.Exception);
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogCrash("AppDomainUnhandledException", ex);
            }
        };
    }

    private static void EnsureStartMenuShortcut()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            var programsDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            if (!Directory.Exists(programsDir)) return;

            // Eski ControllerHub kısayolunu temizle
            try
            {
                var oldLnk = Path.Combine(programsDir, "ControllerHub.lnk");
                if (File.Exists(oldLnk)) File.Delete(oldLnk);
            }
            catch { }

            var shortcutPath = Path.Combine(programsDir, "Gamepad Center.lnk");

            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;

            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell == null) return;

            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = exePath;
            shortcut.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
            shortcut.Description = "Gamepad Center — Evrensel Kontrol Merkezi";
            shortcut.Save();
        }
        catch { }
    }

    private static void LogCrash(string source, Exception ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControllerHub");
            Directory.CreateDirectory(dir);
            var logFile = Path.Combine(dir, "crash.log");
            File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex}\n\n");
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (MainWindow?.DataContext is MainViewModel mainVm)
        {
            mainVm.Dispose();
        }
        base.OnExit(e);
    }
}
