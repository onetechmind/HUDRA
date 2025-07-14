using System;

namespace HUDRA.Configuration
{
    public static class HudraSettings
    {
        // TDP Configuration
        public const int MIN_TDP = 5;
        public const int MAX_TDP = 30;
        public const int DEFAULT_STARTUP_TDP = 10;
        public static int TotalTdpCount => MAX_TDP - MIN_TDP + 1;

        // UI Dimensions (base logical pixels)
        public const double BASE_WINDOW_WIDTH = 320.0;
        public const double BASE_WINDOW_HEIGHT = 450.0;
        public const double BASE_NUMBER_WIDTH = 35.0;
        public const double BASE_SPACING = 0.0;
        public const double BASE_BORDER_PADDING = 5.0;
        public const double BASE_SCROLL_PADDING = 10.0;

        // Timer Intervals
        public static readonly TimeSpan TDP_AUTO_SET_DELAY = TimeSpan.FromMilliseconds(1000);
        public static readonly TimeSpan RESOLUTION_AUTO_SET_DELAY = TimeSpan.FromMilliseconds(300);
        public static readonly TimeSpan REFRESH_RATE_AUTO_SET_DELAY = TimeSpan.FromMilliseconds(300);
        public static readonly TimeSpan TOPMOST_CHECK_INTERVAL = TimeSpan.FromMilliseconds(200);
        public static readonly TimeSpan SNAP_ANIMATION_DELAY = TimeSpan.FromMilliseconds(150);


        // Window Positioning
        public const int WINDOW_PADDING = 20;

    }
}