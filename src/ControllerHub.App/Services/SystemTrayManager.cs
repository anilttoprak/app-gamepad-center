using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace ControllerHub.App.Services;

public class SystemTrayManager : IDisposable
{
    private const int WM_USER = 0x0400;
    private const int WM_TRAYCALLBACK = WM_USER + 1024;
    private const int WM_CONTEXTMENU = 0x007B;
    private const int NIN_SELECT = WM_USER + 0;
    private const int NIN_KEYSELECT = WM_USER + 1;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;
    private const int NIM_SETVERSION = 0x00000004;

    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;
    private const int NIF_INFO = 0x00000010;

    private const int NIIF_NONE = 0x00000000;
    private const int NIIF_INFO = 0x00000001;
    private const int NIIF_WARNING = 0x00000002;
    private const int NIIF_ERROR = 0x00000003;
    private const int NIIF_USER = 0x00000004;
    private const int NIIF_LARGE_ICON = 0x00000020;
    private const int NOTIFYICON_VERSION_4 = 4;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpdata);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    private readonly Window _window;
    private readonly Action _onExitRequested;
    private NOTIFYICONDATA _notifyData;
    private IntPtr _hIcon = IntPtr.Zero;
    private IntPtr _hIconLarge = IntPtr.Zero;
    private bool _isCreated = false;
    private bool _disposed = false;
    private string _currentStatusText = "⚪ Bağlı Değil";

    public SystemTrayManager(Window window, Action onExitRequested)
    {
        _window = window;
        _onExitRequested = onExitRequested;
    }

    public void Initialize()
    {
        var helper = new WindowInteropHelper(_window);
        var hWnd = helper.EnsureHandle();

        var source = HwndSource.FromHwnd(hWnd);
        source?.AddHook(HwndHook);

        // İkonu executable'dan veya standart sistemden yükle
        try
        {
            var exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                ExtractIconEx(exePath, 0, out _hIconLarge, out _hIcon, 1);
            }
        }
        catch { }

        if (_hIcon == IntPtr.Zero && _hIconLarge != IntPtr.Zero)
        {
            _hIcon = _hIconLarge;
        }
        else if (_hIcon == IntPtr.Zero)
        {
            _hIcon = LoadIcon(IntPtr.Zero, (IntPtr)32512); // IDI_APPLICATION
        }

        _notifyData = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = hWnd,
            uID = 1001,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYCALLBACK,
            hIcon = _hIcon,
            szTip = "Gamepad Center — Evrensel Kontrol Merkezi"
        };

        _isCreated = Shell_NotifyIcon(NIM_ADD, ref _notifyData);
    }

    public void UpdateStatus(string statusText)
    {
        _currentStatusText = statusText;
        if (!_isCreated || _disposed) return;

        var tip = $"Gamepad Center\n{statusText}";
        if (tip.Length > 120) tip = tip.Substring(0, 120);

        _notifyData.szTip = tip;
        _notifyData.uFlags = NIF_TIP;
        Shell_NotifyIcon(NIM_MODIFY, ref _notifyData);
    }

    public void ShowNotification(string title, string message, bool isError = false)
    {
        if (!_isCreated || _disposed) return;

        _notifyData.uFlags = NIF_INFO;
        _notifyData.szInfoTitle = title.Length > 63 ? title.Substring(0, 63) : title;
        _notifyData.szInfo = message.Length > 255 ? message.Substring(0, 255) : message;

        if (isError)
        {
            _notifyData.dwInfoFlags = NIIF_ERROR;
            _notifyData.hBalloonIcon = IntPtr.Zero;
        }
        else
        {
            var customIcon = _hIconLarge != IntPtr.Zero ? _hIconLarge : _hIcon;
            if (customIcon != IntPtr.Zero)
            {
                _notifyData.dwInfoFlags = NIIF_USER | NIIF_LARGE_ICON;
                _notifyData.hBalloonIcon = customIcon;
            }
            else
            {
                _notifyData.dwInfoFlags = NIIF_INFO;
                _notifyData.hBalloonIcon = IntPtr.Zero;
            }
        }

        Shell_NotifyIcon(NIM_MODIFY, ref _notifyData);
    }

    public void RestoreWindow()
    {
        if (!_window.Dispatcher.CheckAccess())
        {
            _window.Dispatcher.Invoke(RestoreWindow);
            return;
        }

        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Visibility = Visibility.Visible;

        var helper = new WindowInteropHelper(_window);
        if (helper.Handle != IntPtr.Zero)
        {
            ShowWindow(helper.Handle, SW_RESTORE);
            SetForegroundWindow(helper.Handle);
        }

        _window.Activate();
        _window.Topmost = true;
        _window.Topmost = false;
        _window.Focus();
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAYCALLBACK)
        {
            int eventId = (int)((long)lParam & 0xFFFF);
            if (eventId == WM_LBUTTONUP || eventId == WM_LBUTTONDBLCLK || eventId == NIN_SELECT || eventId == NIN_KEYSELECT)
            {
                RestoreWindow();
                handled = true;
            }
            else if (eventId == WM_RBUTTONUP || eventId == WM_CONTEXTMENU)
            {
                ShowContextMenu();
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    private void ShowContextMenu()
    {
        if (!_window.Dispatcher.CheckAccess())
        {
            _window.Dispatcher.Invoke(ShowContextMenu);
            return;
        }

        var helper = new WindowInteropHelper(_window);
        if (helper.Handle != IntPtr.Zero)
        {
            SetForegroundWindow(helper.Handle);
        }

        var menu = new ContextMenu
        {
            Placement = PlacementMode.MousePoint
        };

        // 1. Gamepad Center'ı Aç
        var showItem = new MenuItem
        {
            Header = "Gamepad Center'ı Göster",
            FontWeight = FontWeights.SemiBold
        };
        showItem.Click += (_, _) =>
        {
            menu.IsOpen = false;
            RestoreWindow();
        };
        menu.Items.Add(showItem);

        // 2. Durum Rozeti (Tıklanamaz bilgi maddesi)
        var statusItem = new MenuItem
        {
            Header = $"Durum: {_currentStatusText}",
            IsEnabled = false
        };
        menu.Items.Add(statusItem);

        menu.Items.Add(new Separator());

        // 3. Tamamen Kapat / Çıkış
        var exitItem = new MenuItem
        {
            Header = "Çıkış"
        };
        exitItem.Click += (_, _) =>
        {
            menu.IsOpen = false;
            _onExitRequested();
        };
        menu.Items.Add(exitItem);

        menu.IsOpen = true;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            if (_isCreated)
            {
                Shell_NotifyIcon(NIM_DELETE, ref _notifyData);
                _isCreated = false;
            }

            if (_hIcon != IntPtr.Zero && _hIcon != _hIconLarge)
            {
                DestroyIcon(_hIcon);
            }
            _hIcon = IntPtr.Zero;

            if (_hIconLarge != IntPtr.Zero)
            {
                DestroyIcon(_hIconLarge);
                _hIconLarge = IntPtr.Zero;
            }
        }
    }
}
