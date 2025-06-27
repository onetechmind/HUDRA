using Microsoft.UI.Xaml;
using System;
using WinRT.Interop;
using System.Runtime.InteropServices;

namespace HUDRA.Helpers
{
    public static class DpiHelper
    {
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public static double GetScaleFactorForWindow(Window window)
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            var dpi = GetDpiForWindow(hwnd);
            return dpi / 96.0; // 96 DPI is the baseline (100% scale)
        }

        public static double ScaleValue(double baseValue, double scaleFactor)
        {
            return baseValue * scaleFactor;
        }

        public static int ScaleValueToInt(double baseValue, double scaleFactor)
        {
            return (int)Math.Round(baseValue * scaleFactor);
        }

        // Convert logical pixels to physical pixels
        public static Windows.Graphics.SizeInt32 LogicalToPhysicalPixels(
            double logicalWidth, double logicalHeight, double scaleFactor)
        {
            return new Windows.Graphics.SizeInt32(
                ScaleValueToInt(logicalWidth, scaleFactor),
                ScaleValueToInt(logicalHeight, scaleFactor)
            );
        }

        // Get effective pixels per view pixel
        public static double GetRasterizationScale(XamlRoot xamlRoot)
        {
            return xamlRoot?.RasterizationScale ?? 1.0;
        }
    }
}