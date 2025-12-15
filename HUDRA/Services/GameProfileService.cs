using HUDRA.Models;
using HUDRA.Services.FanControl;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace HUDRA.Services
{
    /// <summary>
    /// Service for managing per-game profiles that automatically apply hardware settings
    /// when a game is detected as running.
    /// </summary>
    public class GameProfileService : IDisposable
    {
        private readonly ResolutionService _resolutionService;
        private readonly RtssFpsLimiterService _fpsLimiterService;
        private readonly AmdAdlxService _amdService;
        private readonly FanControlService? _fanControlService;
        private readonly HdrService _hdrService;
        private readonly EnhancedGameDatabase _gameDatabase;

        private SystemDefaults? _systemDefaults;
        private bool _isProfileActive = false;
        private string? _activeProfileProcessName;
        private bool _disposed = false;

        public bool IsProfileActive => _isProfileActive;
        public string? ActiveProfileProcessName => _activeProfileProcessName;

        /// <summary>
        /// Gets the stored system defaults that will be used for reverting.
        /// Returns null if no profile is active or defaults weren't captured.
        /// </summary>
        public SystemDefaults? StoredSystemDefaults => _systemDefaults;

        public event EventHandler<ProfileApplicationResult>? ProfileApplied;
        public event EventHandler? ProfileReverted;

        public GameProfileService(
            ResolutionService resolutionService,
            RtssFpsLimiterService fpsLimiterService,
            AmdAdlxService amdService,
            FanControlService? fanControlService,
            HdrService hdrService,
            EnhancedGameDatabase gameDatabase)
        {
            _resolutionService = resolutionService;
            _fpsLimiterService = fpsLimiterService;
            _amdService = amdService;
            _fanControlService = fanControlService;
            _hdrService = hdrService;
            _gameDatabase = gameDatabase;
        }

        /// <summary>
        /// Captures current system state before applying a profile.
        /// </summary>
        public async Task<SystemDefaults> CaptureCurrentSettingsAsync()
        {
            var defaults = new SystemDefaults();

            try
            {
                // Capture TDP
                using var tdpService = new TDPService();
                var tdpResult = tdpService.GetCurrentTdp();
                if (tdpResult.Success)
                {
                    defaults.TdpWatts = tdpResult.TdpWatts;
                }

                // Capture Sticky TDP setting
                defaults.StickyTdpEnabled = SettingsService.GetTdpCorrectionEnabled();
                System.Diagnostics.Debug.WriteLine($"  Captured Sticky TDP: Enabled={defaults.StickyTdpEnabled}");

                // Capture Resolution
                var resResult = _resolutionService.GetCurrentResolution();
                if (resResult.Success)
                {
                    defaults.ResolutionWidth = resResult.CurrentResolution.Width;
                    defaults.ResolutionHeight = resResult.CurrentResolution.Height;
                }

                // Capture Refresh Rate
                var refreshResult = _resolutionService.GetCurrentRefreshRate();
                if (refreshResult.Success)
                {
                    defaults.RefreshRateHz = refreshResult.RefreshRate;
                }

                // Capture FPS Limit
                defaults.FpsLimit = _fpsLimiterService.GetCurrentFpsLimit();

                // Capture AMD Features
                System.Diagnostics.Debug.WriteLine($"Capturing AMD features - GPU available: {_amdService.IsAmdGpuAvailable()}");
                if (_amdService.IsAmdGpuAvailable())
                {
                    var rsrState = await _amdService.GetRsrStateAsync();
                    if (rsrState.success)
                    {
                        defaults.RsrEnabled = rsrState.enabled;
                        defaults.RsrSharpness = rsrState.sharpness;
                        System.Diagnostics.Debug.WriteLine($"  Captured RSR: Enabled={rsrState.enabled}, Sharpness={rsrState.sharpness}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("  Failed to capture RSR state");
                    }

                    var afmfState = await _amdService.GetAfmfStateAsync();
                    if (afmfState.success)
                    {
                        defaults.AfmfEnabled = afmfState.enabled;
                        System.Diagnostics.Debug.WriteLine($"  Captured AFMF: Enabled={afmfState.enabled}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("  Failed to capture AFMF state");
                    }

                    var antiLagState = await _amdService.GetAntiLagStateAsync();
                    if (antiLagState.success)
                    {
                        defaults.AntiLagEnabled = antiLagState.enabled;
                        System.Diagnostics.Debug.WriteLine($"  Captured Anti-Lag: Enabled={antiLagState.enabled}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("  Failed to capture Anti-Lag state");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("  AMD GPU not available - skipping AMD feature capture");
                }

                // Capture HDR state
                try
                {
                    defaults.HdrEnabled = _hdrService.IsHdrEnabled();
                    System.Diagnostics.Debug.WriteLine($"  Captured HDR: Enabled={defaults.HdrEnabled}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"  Failed to capture HDR state: {ex.Message}");
                }

                // Capture Fan Curve
                var fanCurve = SettingsService.GetFanCurve();
                defaults.FanCurveEnabled = fanCurve.IsEnabled;
                defaults.FanCurvePreset = fanCurve.ActivePreset ?? "Cruise";

                defaults.CapturedAt = DateTime.Now;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error capturing system defaults: {ex.Message}");
            }

            return defaults;
        }

        /// <summary>
        /// Applies a game profile, capturing current settings first for later reversion.
        /// </summary>
        public async Task<ProfileApplicationResult> ApplyProfileAsync(string processName, GameProfile profile)
        {
            var result = new ProfileApplicationResult { ActiveProfileProcessName = processName };

            if (!profile.HasProfile || !profile.HasAnySettingsConfigured)
            {
                result.OverallSuccess = false;
                result.Errors.Add("No profile settings configured");
                return result;
            }

            // Don't apply a new profile if one is already active
            if (_isProfileActive)
            {
                System.Diagnostics.Debug.WriteLine($"Profile already active for {_activeProfileProcessName}, skipping {processName}");
                result.OverallSuccess = false;
                result.Errors.Add($"Profile already active for {_activeProfileProcessName}");
                return result;
            }

            try
            {
                // Capture current settings before applying profile
                _systemDefaults = await CaptureCurrentSettingsAsync();

                System.Diagnostics.Debug.WriteLine($"Applying profile for {processName}");

                // Apply TDP (if > 0, meaning it's set)
                if (profile.TdpWatts > 0)
                {
                    try
                    {
                        using var tdpService = new TDPService();
                        var tdpResult = tdpService.SetTdp(profile.TdpWatts * 1000); // Convert to milliwatts
                        result.AddResult("TDP", tdpResult.Success, tdpResult.Message);
                        System.Diagnostics.Debug.WriteLine($"  TDP: {profile.TdpWatts}W - {(tdpResult.Success ? "OK" : "FAILED")}");
                    }
                    catch (Exception ex)
                    {
                        result.AddResult("TDP", false, ex.Message);
                    }
                }

                // Apply Resolution (if width/height > 0, meaning it's set)
                if (profile.ResolutionWidth > 0 && profile.ResolutionHeight > 0)
                {
                    try
                    {
                        var targetRes = new ResolutionService.Resolution
                        {
                            Width = profile.ResolutionWidth,
                            Height = profile.ResolutionHeight,
                            RefreshRate = profile.RefreshRateHz > 0 ? profile.RefreshRateHz : _systemDefaults.RefreshRateHz
                        };
                        var resResult = _resolutionService.SetRefreshRate(targetRes, targetRes.RefreshRate);
                        result.AddResult("Resolution", resResult.Success, resResult.Message);
                        System.Diagnostics.Debug.WriteLine($"  Resolution: {profile.ResolutionWidth}x{profile.ResolutionHeight} - {(resResult.Success ? "OK" : "FAILED")}");
                    }
                    catch (Exception ex)
                    {
                        result.AddResult("Resolution", false, ex.Message);
                    }
                }
                // Apply Refresh Rate only (if set but resolution not changed)
                else if (profile.RefreshRateHz > 0)
                {
                    try
                    {
                        var currentRes = _resolutionService.GetCurrentResolution();
                        if (currentRes.Success)
                        {
                            var resResult = _resolutionService.SetRefreshRate(currentRes.CurrentResolution, profile.RefreshRateHz);
                            result.AddResult("RefreshRate", resResult.Success, resResult.Message);
                            System.Diagnostics.Debug.WriteLine($"  Refresh Rate: {profile.RefreshRateHz}Hz - {(resResult.Success ? "OK" : "FAILED")}");
                        }
                    }
                    catch (Exception ex)
                    {
                        result.AddResult("RefreshRate", false, ex.Message);
                    }
                }

                // Apply FPS Limit (-1 = default/don't change, 0 = unlimited, >0 = specific limit)
                if (profile.FpsLimit >= 0)
                {
                    try
                    {
                        bool fpsSuccess;
                        if (profile.FpsLimit == 0)
                        {
                            // 0 = Unlimited (disable FPS limit)
                            fpsSuccess = await _fpsLimiterService.DisableGlobalFpsLimitAsync();
                            System.Diagnostics.Debug.WriteLine($"  FPS Limit: Unlimited - {(fpsSuccess ? "OK" : "FAILED")}");
                        }
                        else
                        {
                            // >0 = Specific FPS limit
                            fpsSuccess = await _fpsLimiterService.SetGlobalFpsLimitAsync(profile.FpsLimit);
                            System.Diagnostics.Debug.WriteLine($"  FPS Limit: {profile.FpsLimit} FPS - {(fpsSuccess ? "OK" : "FAILED")}");
                        }
                        result.AddResult("FPSLimit", fpsSuccess);
                    }
                    catch (Exception ex)
                    {
                        result.AddResult("FPSLimit", false, ex.Message);
                    }
                }

                // Apply AMD RSR (only if explicitly set, not "Default")
                if (_amdService.IsAmdGpuAvailable() && profile.RsrEnabled.HasValue)
                {
                    try
                    {
                        var rsrSuccess = await _amdService.SetRsrEnabledAsync(profile.RsrEnabled.Value, profile.RsrSharpness);
                        result.AddResult("RSR", rsrSuccess);
                        System.Diagnostics.Debug.WriteLine($"  RSR: {(profile.RsrEnabled.Value ? "Enabled" : "Disabled")} (Sharpness: {profile.RsrSharpness}) - {(rsrSuccess ? "OK" : "FAILED")}");
                    }
                    catch (Exception ex)
                    {
                        result.AddResult("RSR", false, ex.Message);
                    }
                }

                // Apply AMD AFMF (only if explicitly set, not "Default")
                if (_amdService.IsAmdGpuAvailable() && profile.AfmfEnabled.HasValue)
                {
                    try
                    {
                        var afmfSuccess = await _amdService.SetAfmfEnabledAsync(profile.AfmfEnabled.Value);
                        result.AddResult("AFMF", afmfSuccess);
                        System.Diagnostics.Debug.WriteLine($"  AFMF: {(profile.AfmfEnabled.Value ? "Enabled" : "Disabled")} - {(afmfSuccess ? "OK" : "FAILED")}");
                    }
                    catch (Exception ex)
                    {
                        result.AddResult("AFMF", false, ex.Message);
                    }
                }

                // Apply AMD Anti-Lag (only if explicitly set, not "Default")
                if (_amdService.IsAmdGpuAvailable() && profile.AntiLagEnabled.HasValue)
                {
                    try
                    {
                        var antiLagSuccess = await _amdService.SetAntiLagEnabledAsync(profile.AntiLagEnabled.Value);
                        result.AddResult("AntiLag", antiLagSuccess);
                        System.Diagnostics.Debug.WriteLine($"  Anti-Lag: {(profile.AntiLagEnabled.Value ? "Enabled" : "Disabled")} - {(antiLagSuccess ? "OK" : "FAILED")}");
                    }
                    catch (Exception ex)
                    {
                        result.AddResult("AntiLag", false, ex.Message);
                    }
                }

                // Apply Fan Curve Preset (if not "Default")
                if (profile.FanCurvePreset != "Default" && !string.IsNullOrEmpty(profile.FanCurvePreset) && _fanControlService != null)
                {
                    try
                    {
                        var presetCurve = GetFanCurvePreset(profile.FanCurvePreset);
                        if (presetCurve != null)
                        {
                            SettingsService.SetFanCurve(presetCurve);
                            SettingsService.SetFanCurveEnabled(true);
                            result.AddResult("FanCurve", true);
                            System.Diagnostics.Debug.WriteLine($"  Fan Curve: {profile.FanCurvePreset} - OK");
                        }
                        else
                        {
                            result.AddResult("FanCurve", false, "Invalid preset name");
                        }
                    }
                    catch (Exception ex)
                    {
                        result.AddResult("FanCurve", false, ex.Message);
                    }
                }

                _isProfileActive = true;
                _activeProfileProcessName = processName;

                ProfileApplied?.Invoke(this, result);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error applying profile: {ex.Message}");
                result.OverallSuccess = false;
                result.Errors.Add($"Unexpected error: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Reverts all settings to the saved Default Profile, or falls back to captured state if no defaults saved.
        /// </summary>
        public async Task<bool> RevertToDefaultsAsync()
        {
            // Try to use saved Default Profile first, fall back to captured state
            var revertTarget = SettingsService.GetDefaultProfile();
            string revertSource = "saved Default Profile";

            if (revertTarget == null)
            {
                // Fall back to captured state
                revertTarget = _systemDefaults;
                revertSource = "captured state (no Default Profile saved)";
            }

            if (revertTarget == null)
            {
                System.Diagnostics.Debug.WriteLine("No defaults available - nothing to revert");
                return false;
            }

            System.Diagnostics.Debug.WriteLine($"Reverting to {revertSource}...");

            try
            {
                // Revert TDP
                try
                {
                    using var tdpService = new TDPService();
                    tdpService.SetTdp(revertTarget.TdpWatts * 1000);
                    System.Diagnostics.Debug.WriteLine($"  TDP: {revertTarget.TdpWatts}W - OK");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"  TDP revert failed: {ex.Message}");
                }

                // Revert Sticky TDP setting
                try
                {
                    SettingsService.SetTdpCorrectionEnabled(revertTarget.StickyTdpEnabled);
                    System.Diagnostics.Debug.WriteLine($"  Sticky TDP: {(revertTarget.StickyTdpEnabled ? "Enabled" : "Disabled")} - OK");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"  Sticky TDP revert failed: {ex.Message}");
                }

                // Revert Resolution and Refresh Rate
                try
                {
                    var targetRes = new ResolutionService.Resolution
                    {
                        Width = revertTarget.ResolutionWidth,
                        Height = revertTarget.ResolutionHeight,
                        RefreshRate = revertTarget.RefreshRateHz
                    };
                    _resolutionService.SetRefreshRate(targetRes, revertTarget.RefreshRateHz);
                    System.Diagnostics.Debug.WriteLine($"  Resolution: {revertTarget.ResolutionWidth}x{revertTarget.ResolutionHeight}@{revertTarget.RefreshRateHz}Hz - OK");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"  Resolution revert failed: {ex.Message}");
                }

                // Revert FPS Limit
                try
                {
                    if (revertTarget.FpsLimit > 0)
                    {
                        await _fpsLimiterService.SetGlobalFpsLimitAsync(revertTarget.FpsLimit);
                    }
                    else
                    {
                        await _fpsLimiterService.DisableGlobalFpsLimitAsync();
                    }
                    System.Diagnostics.Debug.WriteLine($"  FPS Limit: {(revertTarget.FpsLimit > 0 ? revertTarget.FpsLimit + " FPS" : "Unlimited")} - OK");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"  FPS Limit revert failed: {ex.Message}");
                }

                // Revert AMD RSR and AFMF
                System.Diagnostics.Debug.WriteLine($"  AMD GPU available: {_amdService.IsAmdGpuAvailable()}");
                if (_amdService.IsAmdGpuAvailable())
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"  Reverting RSR to: Enabled={revertTarget.RsrEnabled}, Sharpness={revertTarget.RsrSharpness}");
                        var rsrSuccess = await _amdService.SetRsrEnabledAsync(revertTarget.RsrEnabled, revertTarget.RsrSharpness);
                        System.Diagnostics.Debug.WriteLine($"  RSR: {(revertTarget.RsrEnabled ? "Enabled" : "Disabled")} - {(rsrSuccess ? "OK" : "FAILED")}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"  RSR revert failed: {ex.Message}");
                    }

                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"  Reverting AFMF to: Enabled={revertTarget.AfmfEnabled}");
                        var afmfSuccess = await _amdService.SetAfmfEnabledAsync(revertTarget.AfmfEnabled);
                        System.Diagnostics.Debug.WriteLine($"  AFMF: {(revertTarget.AfmfEnabled ? "Enabled" : "Disabled")} - {(afmfSuccess ? "OK" : "FAILED")}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"  AFMF revert failed: {ex.Message}");
                    }

                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"  Reverting Anti-Lag to: Enabled={revertTarget.AntiLagEnabled}");
                        var antiLagSuccess = await _amdService.SetAntiLagEnabledAsync(revertTarget.AntiLagEnabled);
                        System.Diagnostics.Debug.WriteLine($"  Anti-Lag: {(revertTarget.AntiLagEnabled ? "Enabled" : "Disabled")} - {(antiLagSuccess ? "OK" : "FAILED")}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"  Anti-Lag revert failed: {ex.Message}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("  Skipping AMD features revert - AMD GPU not available");
                }

                // Revert HDR
                try
                {
                    _hdrService.SetHdrEnabled(revertTarget.HdrEnabled);
                    System.Diagnostics.Debug.WriteLine($"  HDR: {(revertTarget.HdrEnabled ? "Enabled" : "Disabled")} - OK");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"  HDR revert failed: {ex.Message}");
                }

                // Revert Fan Curve
                if (_fanControlService != null)
                {
                    try
                    {
                        var presetCurve = GetFanCurvePreset(revertTarget.FanCurvePreset);
                        if (presetCurve != null)
                        {
                            SettingsService.SetFanCurve(presetCurve);
                        }
                        SettingsService.SetFanCurveEnabled(revertTarget.FanCurveEnabled);
                        System.Diagnostics.Debug.WriteLine($"  Fan Curve: {revertTarget.FanCurvePreset} ({(revertTarget.FanCurveEnabled ? "Enabled" : "Disabled")}) - OK");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"  Fan Curve revert failed: {ex.Message}");
                    }
                }

                _isProfileActive = false;
                _activeProfileProcessName = null;
                _systemDefaults = null;

                ProfileReverted?.Invoke(this, EventArgs.Empty);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reverting to defaults: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Clears the profile active state without reverting settings.
        /// Call this when the user chooses to keep current settings after a game exits.
        /// </summary>
        public void ClearProfileState()
        {
            System.Diagnostics.Debug.WriteLine("Clearing profile state without reverting");
            _isProfileActive = false;
            _activeProfileProcessName = null;
            _systemDefaults = null;
        }

        /// <summary>
        /// Retrieves a game profile from the database.
        /// </summary>
        public GameProfile? GetProfileForGame(string processName)
        {
            try
            {
                var game = _gameDatabase.GetGame(processName);
                if (game == null || string.IsNullOrEmpty(game.ProfileJson))
                {
                    return null;
                }

                return JsonSerializer.Deserialize<GameProfile>(game.ProfileJson);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading profile for {processName}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Saves a game profile to the database.
        /// </summary>
        public bool SaveProfileForGame(string processName, GameProfile profile)
        {
            try
            {
                var game = _gameDatabase.GetGame(processName);
                if (game == null)
                {
                    System.Diagnostics.Debug.WriteLine($"Game not found in database: {processName}");
                    return false;
                }

                game.ProfileJson = JsonSerializer.Serialize(profile);
                _gameDatabase.SaveGame(game);

                System.Diagnostics.Debug.WriteLine($"Saved profile for {processName}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving profile for {processName}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets a fan curve preset by name.
        /// </summary>
        private FanCurve? GetFanCurvePreset(string presetName)
        {
            if (presetName == "Custom")
            {
                // Load custom curve from settings
                var customPoints = SettingsService.GetCustomFanCurve();
                return new FanCurve
                {
                    IsEnabled = true,
                    ActivePreset = "Custom",
                    Points = customPoints
                };
            }

            // Find the preset
            var preset = FanCurvePreset.AllPresets.FirstOrDefault(p =>
                string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase));

            if (preset != null)
            {
                return new FanCurve
                {
                    IsEnabled = true,
                    ActivePreset = preset.Name,
                    Points = preset.Points
                };
            }

            return null;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }
}
