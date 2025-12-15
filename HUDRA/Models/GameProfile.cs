using System.Text.Json.Serialization;

namespace HUDRA.Models
{
    /// <summary>
    /// Per-game profile settings that are automatically applied when a game launches.
    /// </summary>
    public class GameProfile
    {
        // Master toggle - if false, no profile settings are active
        public bool HasProfile { get; set; } = false;

        // Auto-revert to default profile when game closes
        public bool AutoRevertOnClose { get; set; } = false;

        // TDP Settings (0 = not set/use system default)
        public int TdpWatts { get; set; } = 0;

        // Resolution Settings (0x0 = not set/use system default)
        public int ResolutionWidth { get; set; } = 0;
        public int ResolutionHeight { get; set; } = 0;

        // Refresh Rate Settings (0 = not set/use system default)
        public int RefreshRateHz { get; set; } = 0;

        // FPS Limit Settings (RTSS) (-1 = default/don't change, 0 = unlimited, >0 = specific limit)
        public int FpsLimit { get; set; } = -1;

        // HDR Settings (null = don't change/default, true = on, false = off)
        public bool? HdrEnabled { get; set; } = null;

        // AMD RSR Settings (null = don't change/default, true = on, false = off)
        public bool? RsrEnabled { get; set; } = null;
        public int RsrSharpness { get; set; } = 80;

        // AMD AFMF Settings (null = don't change/default, true = on, false = off)
        public bool? AfmfEnabled { get; set; } = null;

        // AMD Anti-Lag Settings (null = don't change/default, true = on, false = off)
        public bool? AntiLagEnabled { get; set; } = null;

        // Fan Curve Settings ("Default" = don't change)
        public string FanCurvePreset { get; set; } = "Default";

        /// <summary>
        /// Returns true if any profile setting is actually configured
        /// </summary>
        [JsonIgnore]
        public bool HasAnySettingsConfigured =>
            TdpWatts > 0 ||
            (ResolutionWidth > 0 && ResolutionHeight > 0) ||
            RefreshRateHz > 0 ||
            FpsLimit >= 0 || // -1 = default (not configured), 0+ = configured
            HdrEnabled.HasValue ||
            RsrEnabled.HasValue ||
            AfmfEnabled.HasValue ||
            AntiLagEnabled.HasValue ||
            (FanCurvePreset != "Default" && !string.IsNullOrEmpty(FanCurvePreset));
    }
}
