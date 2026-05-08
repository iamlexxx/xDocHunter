using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using xDocHunter.Services;

namespace xDocHunter.Views;

public partial class PdfViewerWindow : Window
{
    // ── Win32 ────────────────────────────────────────────────────────────────

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] static extern bool   UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern uint   SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT pt);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr SetCursor(IntPtr hCursor);
    [DllImport("user32.dll")] static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

    private const int  WH_MOUSE_LL    = 14;
    private const int  WM_LBUTTONDOWN = 0x0201;
    private const int  WM_LBUTTONUP   = 0x0202;
    private const int  WM_RBUTTONDOWN = 0x0204;
    private const int  WM_RBUTTONUP   = 0x0205;
    private const int  WM_MBUTTONDOWN = 0x0207;
    private const int  WM_MBUTTONUP   = 0x0208;
    private const int  WM_MOUSEMOVE   = 0x0200;
    private const uint GA_ROOT        = 2;
    private const int  IDC_SIZEALL    = 32646;
    private const int  IDC_ARROW      = 32512;
    private const int  INPUT_MOUSE    = 0;
    private const uint MOUSEEVENTF_WHEEL  = 0x0800;
    private const uint MOUSEEVENTF_HWHEEL = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData, flags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    // 64-bit Windows: type(4) + padding(4) + union at offset 8 = 40 bytes total.
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT
    {
        [FieldOffset(0)] public int type;
        [FieldOffset(8)] public MOUSEINPUT mi;
    }

    // ── Fields ────────────────────────────────────────────────────────────────

    private static readonly string UserDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "xDocHunter", "WebView2");

    private readonly string _filePath;

    private IntPtr    _hookHandle = IntPtr.Zero;
    private HookProc? _hookProc;
    private IntPtr    _ourHwnd    = IntPtr.Zero;
    private IntPtr    _panCursor  = IntPtr.Zero;
    private bool      _panMode    = false;
    private POINT     _panOrigin;

    // Cached WebView2 screen bounds — updated by timer so PointToScreen
    // is never called from inside the hook callback.
    private int _wvLeft, _wvTop, _wvRight, _wvBottom;
    private DispatcherTimer? _boundsTimer;

    private static readonly int _inputSize = Marshal.SizeOf<INPUT>();

    // ── Construction ──────────────────────────────────────────────────────────

    public PdfViewerWindow(string filePath)
    {
        InitializeComponent();
        _filePath = filePath;

        var name = Path.GetFileName(filePath);
        Title = $"{name} — xDocHunter (read-only)";
        FileNameText.Text = name;
        FilePathText.Text = filePath;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(UserDataFolder);
            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: UserDataFolder);
            await Web.EnsureCoreWebView2Async(env);

            var settings = Web.CoreWebView2.Settings;
            settings.AreDefaultContextMenusEnabled = !ThemeManager.CustomPdfMouseControls;
            settings.AreDevToolsEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = true;

            Web.CoreWebView2.Navigate(new Uri(_filePath).AbsoluteUri);
            StatusOverlay.Visibility = Visibility.Collapsed;

            if (ThemeManager.CustomPdfMouseControls)
            {
                _ourHwnd   = new WindowInteropHelper(this).Handle;
                _panCursor = LoadCursor(IntPtr.Zero, IDC_SIZEALL);

                UpdateBoundsCache();
                _boundsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                _boundsTimer.Tick += (_, _) => UpdateBoundsCache();
                _boundsTimer.Start();

                InstallHook();
            }
        }
        catch (Exception ex)
        {
            StatusDetail.Text =
                "Could not start the built-in viewer.\n" +
                "Make sure the WebView2 Runtime is installed (it ships with Windows 10/11).\n\n" +
                ex.Message;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _boundsTimer?.Stop();
        RemoveHook();
    }

    // ── Bounds cache (UI thread only) ─────────────────────────────────────────

    private void UpdateBoundsCache()
    {
        try
        {
            var tl = Web.PointToScreen(new Point(0, 0));
            var br = Web.PointToScreen(new Point(Web.ActualWidth, Web.ActualHeight));
            _wvLeft   = (int)tl.X;
            _wvTop    = (int)tl.Y;
            _wvRight  = (int)br.X;
            _wvBottom = (int)br.Y;
        }
        catch { }
    }

    // ── Hook install / remove ─────────────────────────────────────────────────

    private void InstallHook()
    {
        if (_hookHandle != IntPtr.Zero) return;
        _hookProc   = LowLevelMouseHook;
        _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, IntPtr.Zero, 0);
    }

    private void RemoveHook()
    {
        if (_hookHandle == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
        _hookProc   = null;
        _panMode    = false;
    }

    // ── Hook callback ─────────────────────────────────────────────────────────

    private IntPtr LowLevelMouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || !IsVisible)
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        var fg = GetForegroundWindow();
        if (fg != _ourHwnd && GetAncestor(fg, GA_ROOT) != _ourHwnd)
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        var hs  = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
        int msg = (int)wParam;

        if (!IsOverWebView(hs.pt))
        {
            if (_panMode) ExitPanMode();
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        switch (msg)
        {
            case WM_LBUTTONDOWN:
                if (_panMode) { ExitPanMode(); return (IntPtr)1; }
                Web.ZoomFactor = Math.Min(Web.ZoomFactor * 1.15, 5.0);
                return (IntPtr)1;

            case WM_LBUTTONUP when !_panMode:
                return (IntPtr)1;

            case WM_RBUTTONDOWN:
                if (_panMode) { ExitPanMode(); return (IntPtr)1; }
                Web.ZoomFactor = Math.Max(Web.ZoomFactor / 1.15, 0.25);
                return (IntPtr)1;

            case WM_RBUTTONUP:
                return (IntPtr)1;

            case WM_MBUTTONDOWN:
                if (_panMode) ExitPanMode();
                else EnterPanMode(hs.pt);
                return (IntPtr)1;

            case WM_MBUTTONUP:
                return (IntPtr)1;

            case WM_MOUSEMOVE when _panMode:
                DoPan(hs.pt);
                SetCursor(_panCursor);
                return (IntPtr)1;
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    // ── Pan ───────────────────────────────────────────────────────────────────

    private void EnterPanMode(POINT origin)
    {
        _panMode   = true;
        _panOrigin = origin;
        SetCursor(_panCursor);
    }

    private void ExitPanMode()
    {
        _panMode = false;
        SetCursor(LoadCursor(IntPtr.Zero, IDC_ARROW));
    }

    private void DoPan(POINT current)
    {
        int dx = current.X - _panOrigin.X;
        int dy = current.Y - _panOrigin.Y;
        _panOrigin = current;

        if (dx == 0 && dy == 0) return;

        const int scale = 4;
        var inputs = new List<INPUT>(2);

        if (dy != 0)
            inputs.Add(new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_WHEEL,  mouseData = (uint)(-dy * scale) } });
        if (dx != 0)
            inputs.Add(new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_HWHEEL, mouseData = (uint)(-dx * scale) } });

        // IMPORTANT: SendInput cannot be called from inside WH_MOUSE_LL — it re-enters the hook
        // before the callback returns, causing Windows to deadlock (hook delivery is serialized).
        // Fire it on the thread pool so it runs after the current callback has returned.
        var arr = inputs.ToArray();
        _ = Task.Run(() => SendInput((uint)arr.Length, arr, _inputSize));
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private bool IsOverWebView(POINT pt)
    {
        try
        {
            var hwndUnder = WindowFromPoint(pt);
            if (hwndUnder == IntPtr.Zero) return false;
            if (GetAncestor(hwndUnder, GA_ROOT) != _ourHwnd) return false;
            return pt.X >= _wvLeft && pt.X <= _wvRight
                && pt.Y >= _wvTop  && pt.Y <= _wvBottom;
        }
        catch { return false; }
    }
}
