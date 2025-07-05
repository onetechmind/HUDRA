using System;

namespace HUDRA.Configuration
{
    public static class HudraSettings
    {
        // TDP Configuration
        public const int MIN_TDP = 5;
        public const int MAX_TDP = 30;
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
        public static readonly TimeSpan GAMEPAD_POLL_INTERVAL = TimeSpan.FromMilliseconds(100);
        public static readonly TimeSpan TOPMOST_CHECK_INTERVAL = TimeSpan.FromMilliseconds(200);
        public static readonly TimeSpan SNAP_ANIMATION_DELAY = TimeSpan.FromMilliseconds(150);

        // Gamepad Configuration
        public const int TOTAL_CONTROLS = 7;
        public const double VOLUME_STEP = 2.0;
        public const double BRIGHTNESS_STEP = 2.0;

        // Window Positioning
        public const int WINDOW_PADDING = 20;

        // Control Indices for Gamepad Navigation
        public enum ControlIndex
        {
            TdpSelector = 0,
            ResolutionSelector = 1,
            RefreshRateSelector = 2,
            MuteButton = 3,
            VolumeSlider = 4,
            BrightnessSlider = 5,
            CloseButton = 6
        }
    }
}