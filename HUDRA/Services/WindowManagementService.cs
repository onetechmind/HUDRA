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

        public void ToggleVisibility()
        {
            try
            {
                var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
                var appWindow = AppWindow.GetFromWindowId(windowId);

                if (_isWindowVisible)
                {
                    // Hide window
                    appWindow.Hide();
                    _isWindowVisible = false;
                }
                else
                {
                    // Show window
                    appWindow.Show();
                    _isWindowVisible = true;

                    // CRITICAL: Activate the window to bring it to foreground
                    _window.Activate();

                    // Ensure proper positioning and topmost behavior
                    PositionWindow();

                    if (_forceTopmost)
                    {
                        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    }

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
                // Enable vertical-only resizing for testing scroll behavior
                presenter.IsResizable = true;
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
    }
}