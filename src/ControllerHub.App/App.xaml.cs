using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using ControllerHub.App.ViewModels;

namespace ControllerHub.App;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private const int HWND_BROADCAST = 0xFFFF;

    public App()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. Çalışma dizinini uygulamanın bulunduğu dizine sabitle
        try
        {
            Environment.CurrentDirectory = AppDomain.CurrentDomain.BaseDirectory;
        }
        catch { }

        // 2. Tek Örnek (Single Instance) Kontrolü
        bool isOnlyInstance;
        try
        {
            _singleInstanceMutex = new Mutex(true, @"Local\GamepadCenter_SingleInstance_Mutex", out isOnlyInstance);
        }
        catch
        {
            isOnlyInstance = true;
        }

        if (!isOnlyInstance)
        {
            // Zaten açık olan örneğe pencereyi öne getirmesi için mesaj gönder
            try
            {
                var showMsg = RegisterWindowMessage("GamepadCenter_ShowMainWindow");
                if (showMsg != 0)
                {
                    PostMessage((IntPtr)HWND_BROADCAST, showMsg, IntPtr.Zero, IntPtr.Zero);
                }
            }
            catch { }

            Shutdown();
            return;
        }

        // 3. Başlat menüsü kısayolunu oluştur / güncelle
        try
        {
            EnsureStartMenuShortcut();
        }
        catch { }

        // 4. Windows Başlangıcı (--autostart) Kontrolü
        bool isAutostart = e.Args.Any(arg => arg.Equals("--autostart", StringComparison.OrdinalIgnoreCase))
                           || Environment.GetCommandLineArgs().Any(arg => arg.Equals("--autostart", StringComparison.OrdinalIgnoreCase));

        var mainWindow = new MainWindow(isAutostart);
        MainWindow = mainWindow;

        if (!isAutostart)
        {
            mainWindow.Show();
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        try
        {
            if (MainWindow?.DataContext is MainViewModel mainVm)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                mainVm.ShutdownGracefullyAsync(cts.Token).GetAwaiter().GetResult();
            }
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        catch { }

        if (MainWindow?.DataContext is MainViewModel mainVm)
        {
            mainVm.Dispose();
        }
        base.OnExit(e);
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
            shortcut.IconLocation = $"{exePath},0";
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
}
