using System;

namespace HUDRA.Models
{
    /// <summary>
    /// Stores system state snapshot before a game profile is applied,
    /// allowing reversion to the original settings when the game exits.
    /// </summary>
    public class SystemDefaults
    {
        public int TdpWatts { get; set; }
        public bool StickyTdpEnabled { get; set; }
        public int ResolutionWidth { get; set; }
        public int ResolutionHeight { get; set; }
        public int RefreshRateHz { get; set; }
        public int FpsLimit { get; set; }
        public bool RsrEnabled { get; set; }
        public int RsrSharpness { get; set; }
        public bool AfmfEnabled { get; set; }
        public bool AntiLagEnabled { get; set; }
        public bool HdrEnabled { get; set; }
        public string FanCurvePreset { get; set; } = "Cruise";
        public bool FanCurveEnabled { get; set; }

        public DateTime CapturedAt { get; set; } = DateTime.Now;
    }
}
