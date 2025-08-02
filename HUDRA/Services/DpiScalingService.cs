// HUDRA/Services/DpiScalingService.cs
using Microsoft.UI.Xaml;
using System;
using System.Runtime.InteropServices;
using WinRT.Interop;
using HUDRA.Configuration;

namespace HUDRA.Services
{
    public class DpiScalingService
    {
        private double _currentScaleFactor = 1.0;
        private readonly Window _window;

        public DpiScalingService(Window window)
        {
            _window = window;
            UpdateScaleFactor();
        }

        public double ScaleFactor => _currentScaleFactor;

        // Scaled dimension properties
        public double NumberWidth => HudraSettings.BASE_NUMBER_WIDTH * _currentScaleFactor;
        public double SpacingWidth => HudraSettings.BASE_SPACING * _currentScaleFactor;
        public double ItemWidth => NumberWidth + SpacingWidth;
        public double BorderPadding => HudraSettings.BASE_BORDER_PADDING * _currentScaleFactor;
        public double ScrollPadding => HudraSettings.BASE_SCROLL_PADDING * _currentScaleFactor;

        public Windows.Graphics.SizeInt32 ScaledWindowSize => new(
            (int)Math.Round(HudraSettings.BASE_WINDOW_WIDTH * _currentScaleFactor),
            (int)Math.Round(HudraSettings.BASE_WINDOW_HEIGHT * _currentScaleFactor)
        );

        public Windows.Graphics.SizeInt32 FullHeightWindowSize => new(
            (int)Math.Round(HudraSettings.BASE_WINDOW_WIDTH * _currentScaleFactor),
            GetScreenHeight()
        );

        public int ScaledWindowPadding => (int)(HudraSettings.WINDOW_PADDING * _currentScaleFactor);

        public void UpdateScaleFactor()
        {
            try
            {
                var hwnd = WindowNative.GetWindowHandle(_window);
                var dpi = GetDpiForWindow(hwnd);
                _currentScaleFactor = dpi / 96.0; // 96 DPI = 100% scale
            }
            catch
            {
                _currentScaleFactor = 1.0; // Fallback to 100% scale
            }
        }

        public bool HasScaleChanged(double previousScale)
        {
            return Math.Abs(_currentScaleFactor - previousScale) > 0.01;
        }

        private int GetScreenHeight()
        {
            try
            {
                var hwnd = WindowNative.GetWindowHandle(_window);
                var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTOPRIMARY);
                
                var monitorInfo = new MONITORINFO();
                monitorInfo.cbSize = Marshal.SizeOf(monitorInfo);
                
                if (GetMonitorInfo(monitor, ref monitorInfo))
                {
                    return monitorInfo.rcMonitor.Bottom - monitorInfo.rcMonitor.Top;
                }
            }
            catch
            {
                // Fallback to system metrics
            }

            return GetSystemMetrics(SM_CYSCREEN);
        }

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        private const uint MONITOR_DEFAULTTOPRIMARY = 1;
        private const int SM_CYSCREEN = 1;
    }
}