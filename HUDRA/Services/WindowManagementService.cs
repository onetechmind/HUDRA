// HUDRA/Services/WindowManagementService.cs
using HUDRA.Configuration;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinRT.Interop;

namespace HUDRA.Services
{
    public class WindowManagementService : IDisposable
    {
        private readonly Window _window;
        private readonly IntPtr _hwnd;
        private readonly DpiScalingService _dpiService;
        private DispatcherTimer? _topmostTimer;
        private bool _forceTopmost = true;
        private bool _isWindowVisible = true;

        public bool IsVisible => _isWindowVisible;

        /// <summary>True if this window is the OS foreground window right now.</summary>
        public bool IsForeground => GetForegroundWindow() == _hwnd;

        /// <summary>
        /// Force this window to the foreground if it is visible but not
        /// foreground. Needed because gamepad readings are foreground-gated by
        /// Windows: a topmost overlay receives clicks WITHOUT becoming the
        /// foreground window, so after another app steals foreground (e.g. a web
        /// link opening the browser) the user taps HUDRA, sees it respond to the
        /// tap, and the controller stays dead - the readings are still routed to
        /// the other app. Every tap therefore re-asserts foreground.
        /// </summary>
        public void EnsureForeground()
        {
            if (!_isWindowVisible || IsForeground) return;

            DebugLogger.Log("Window tapped while not foreground - forcing foreground (gamepad readings are foreground-gated)", "GPAD");
            ForceForegroundWindow(_hwnd);
        }

        /// <summary>
        /// Fired when the window is shown (unhidden) via ToggleVisibility.
        /// Use this to force input focus to the app when it becomes visible.
        /// </summary>
        public event EventHandler? WindowShown;

        /// <summary>
        /// Raised after the window has been hidden. Exists so state teardown happens
        /// once, here, instead of being duplicated at each hide call site - the
        /// hotkey and tray paths used to skip it, leaving gamepad focus state behind.
        /// </summary>
        public event EventHandler? WindowHidden;

        public WindowManagementService(Window window, DpiScalingService dpiService)
        {
            _window = window;
            _dpiService = dpiService;
            _hwnd = WindowNative.GetWindowHandle(window);
        }

        public void Initialize()
        {
            SetInitialSize();
            MakeBorderlessWithRoundedCorners();
            ApplyRoundedCorners();
            PositionWindow();
            SetWindowIcon();
            StartTopmostBehavior();
        }

        // Guards against multi-fire from any source (held hotkey, tray double-click,
        // rapid button presses): repeated toggles produce show/hide storms which
        // re-fire WindowShown and steal focus.
        private static readonly TimeSpan ToggleDebounce = TimeSpan.FromMilliseconds(250);
        private DateTime _lastToggle = DateTime.MinValue;

        public void ToggleVisibility()
        {
            var now = DateTime.UtcNow;
            if (now - _lastToggle < ToggleDebounce)
            {
                System.Diagnostics.Debug.WriteLine("Ignoring window toggle within debounce window");
                return;
            }
            _lastToggle = now;

            try
            {
                var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
                var appWindow = AppWindow.GetFromWindowId(windowId);

                if (_isWindowVisible)
                {
                    // Hide window
                    appWindow.Hide();
                    _isWindowVisible = false;

                    WindowHidden?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    // Show window
                    appWindow.Show();
                    _isWindowVisible = true;

                    // Position BEFORE activation so we activate on the final placement.
                    PositionWindow();

                    // WinUI activation (sets window active-state intent).
                    _window.Activate();

                    // CRITICAL: force OS foreground from a (likely) background process so
                    // Windows.Gaming.Input routes gamepad readings to HUDRA immediately,
                    // without the user having to click the window. A plain Activate/
                    // SetForegroundWindow is blocked by the Win32 foreground lock here.
                    ForceForegroundWindow(_hwnd);

                    // Re-assert topmost z-order AFTER foreground. SWP_NOACTIVATE only fixes
                    // z-order and does not deactivate, so it won't undo the activation above.
                    if (_forceTopmost)
                    {
                        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    }

                    // Notify subscribers that window is now visible
                    // This allows MainWindow to force input focus to the app
                    WindowShown?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to toggle window visibility: {ex.Message}");
            }
        }

        public void SetInitialVisibilityState(bool isVisible)
        {
            _isWindowVisible = isVisible;
        }

        private void SetInitialSize()
        {
            var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            var fullHeightSize = _dpiService.FullHeightWindowSize;
            appWindow.Resize(fullHeightSize);
        }

        private void MakeBorderlessWithRoundedCorners()
        {
            var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.SetBorderAndTitleBar(false, false);
            }

            appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        }

        private void ApplyRoundedCorners()
        {
            try
            {
                var preference = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(_hwnd, 33, ref preference, sizeof(int));
            }
            catch
            {
                // Fallback for older Windows versions
            }
        }

        public void PositionWindow()
        {
            try
            {
                var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
                var appWindow = AppWindow.GetFromWindowId(windowId);

                // Get screen dimensions for full-height positioning
                var screenArea = new RECT();
                GetWindowRect(GetDesktopWindow(), ref screenArea);

                var windowSize = appWindow.Size;

                // Position flush with right edge (no padding) and at top of screen
                var x = screenArea.Right - windowSize.Width;
                var y = 0;

                appWindow.Move(new PointInt32(x, y));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to position window: {ex.Message}");
            }
        }

        /// <summary>
        /// Forces the given window to become the OS foreground/active window, even when
        /// the calling process is in the background (e.g. shown via global hotkey while a
        /// game is foreground). A plain SetForegroundWindow call is silently blocked by the
        /// Win32 foreground lock in that case, leaving the window shown-but-not-activated so
        /// Windows.Gaming.Input never routes gamepad readings to it until the user clicks.
        /// This bypasses the lock via AttachThreadInput, hardened by temporarily zeroing the
        /// foreground lock timeout.
        /// </summary>
        private void ForceForegroundWindow(IntPtr hwnd)
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == hwnd)
                return; // already foreground; nothing to do

            uint currentThreadId = GetCurrentThreadId();
            uint foregroundThreadId = (foreground == IntPtr.Zero)
                ? 0u
                : GetWindowThreadProcessId(foreground, out _);

            // Both of the following mutate state that outlives this method - an
            // attached input queue is shared with another process's UI thread, and the
            // foreground lock timeout is machine-wide. They MUST be undone even if
            // anything in between throws, hence the try/finally: previously an
            // exception left HUDRA's input queue attached to a game's thread and the
            // system's foreground lock disabled until reboot.
            bool attached = false;
            bool timeoutSaved = false;
            uint oldTimeout = 0;

            try
            {
                timeoutSaved = SystemParametersInfo(SPI_GETFOREGROUNDLOCKTIMEOUT, 0, ref oldTimeout, 0);
                if (timeoutSaved)
                {
                    uint zero = 0;
                    SystemParametersInfo(SPI_SETFOREGROUNDLOCKTIMEOUT, 0, ref zero, SPIF_SENDCHANGE);
                }

                // Attaching our input queue to the foreground thread lets SetForegroundWindow succeed.
                if (foregroundThreadId != 0 && foregroundThreadId != currentThreadId)
                    attached = AttachThreadInput(currentThreadId, foregroundThreadId, true);

                ShowWindow(hwnd, SW_SHOW);
                BringWindowToTop(hwnd);
                SetForegroundWindow(hwnd);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ForceForegroundWindow failed: {ex.Message}");
            }
            finally
            {
                if (attached)
                {
                    try { AttachThreadInput(currentThreadId, foregroundThreadId, false); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed to detach thread input: {ex.Message}"); }
                }

                if (timeoutSaved)
                {
                    try
                    {
                        uint restore = oldTimeout;
                        SystemParametersInfo(SPI_SETFOREGROUNDLOCKTIMEOUT, 0, ref restore, SPIF_SENDCHANGE);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to restore foreground lock timeout: {ex.Message}");
                    }
                }
            }
        }

        private void SetWindowIcon()
        {
            try
            {
                string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "HUDRA_Logo_64x64.ico");

                if (File.Exists(iconPath))
                {
                    var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
                    var appWindow = AppWindow.GetFromWindowId(windowId);
                    appWindow.SetIcon(iconPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to set window icon: {ex.Message}");
            }
        }

        private void StartTopmostBehavior()
        {
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

            _topmostTimer = new DispatcherTimer { Interval = HudraSettings.TOPMOST_CHECK_INTERVAL };
            _topmostTimer.Tick += (s, e) => {
                if (_forceTopmost && _isWindowVisible)
                {
                    SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
            };
            _topmostTimer.Start();
        }

        public void Dispose()
        {
            _topmostTimer?.Stop();
            _topmostTimer = null;
        }

        // P/Invoke declarations
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll")]
        private static extern bool SystemParametersInfo(int uAction, int uParam, ref RECT lpvParam, int fuWinIni);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, ref RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        // Overload taking a uint by-ref; the existing SystemParametersInfo above takes a RECT.
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uAction, uint uParam, ref uint pvParam, uint fWinIni);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const int SPI_GETWORKAREA = 48;
        private const int SW_SHOW = 5;
        private const uint SPI_GETFOREGROUNDLOCKTIMEOUT = 0x2000;
        private const uint SPI_SETFOREGROUNDLOCKTIMEOUT = 0x2001;
        private const uint SPIF_SENDCHANGE = 0x0002;
    }
}