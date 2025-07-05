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

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);
    }
}