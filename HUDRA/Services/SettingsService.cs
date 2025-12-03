using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using HUDRA.Configuration;
using HUDRA.Controls; // For FanCurve and FanCurvePoint classes
using HUDRA.Services.FanControl;
using HUDRA.Models;
using Microsoft.Win32;

namespace HUDRA.Services
{
    public static class SettingsService
    {
        // Existing keys
        private const string TdpCorrectionKey = "TdpCorrectionEnabled";
        private const string StartupTdpKey = "StartupTdp";
        private const string UseStartupTdpKey = "UseStartupTdp";
        private const string LastUsedTdpKey = "LastUsedTdp";

        // New fan curve keys
        private const string FanCurveEnabledKey = "FanCurveEnabled";
        private const string FanCurvePointsKey = "FanCurvePoints";
        private const string FanCurveActivePresetKey = "FanCurveActivePreset";
        private const string CustomFanCurvePointsKey = "CustomFanCurvePoints";

        // Power profile keys
        private const string PreferredPowerProfileKey = "PreferredPowerProfile";
        private const string RestorePowerProfileOnStartupKey = "RestorePowerProfileOnStartup";
        private const string CpuBoostEnabledKey = "CpuBoostEnabled";
        private const string RestoreCpuBoostOnStartupKey = "RestoreCpuBoostOnStartup";
        
        // New intelligent power switching keys
        private const string DefaultPowerProfileKey = "DefaultPowerProfile";
        private const string GamingPowerProfileKey = "GamingPowerProfile";
        private const string IntelligentPowerSwitchingKey = "IntelligentPowerSwitchingEnabled";

        // FPS Limiter keys
        private const string SelectedFpsLimitKey = "SelectedFpsLimit";
        private const string StartRtssWithHudraKey = "StartRtssWithHudra";

        //Startup
        private const string STARTUP_ENABLED_KEY = "StartupEnabled";
        private const string MINIMIZE_TO_TRAY_ON_STARTUP_KEY = "MinimizeToTrayOnStartup";
        
        // Lossless Scaling settings keys
        private const string LS_UPSCALING_ENABLED_KEY = "LSUpscalingEnabled";
        private const string LS_FRAME_GEN_MULTIPLIER_KEY = "LSFrameGenMultiplier";
        private const string LS_FLOW_SCALE_KEY = "LSFlowScale";

        // Hotkey settings keys
        private const string HIDE_SHOW_HOTKEY_KEY = "HideShowHotkeyKey";
        private const string HIDE_SHOW_HOTKEY_MODIFIERS = "HideShowHotkeyModifiers";

        // Enhanced game detection keys
        private const string ENHANCED_LIBRARY_SCANNING_KEY = "EnhancedLibraryScanningEnabled";
        private const string GAME_DATABASE_REFRESH_INTERVAL_KEY = "GameDatabaseRefreshInterval";

        // First-run experience keys
        private const string HAS_COMPLETED_FIRST_RUN_KEY = "HasCompletedFirstRun";
        private const string ENHANCED_GAME_DETECTION_ENABLED_KEY = "EnhancedGameDetectionEnabled";

        // Registry path for installer settings
        private const string REGISTRY_PATH = @"SOFTWARE\HUDRA";

        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HUDRA",
            "settings.json");

        private static readonly object _lock = new object();
        private static Dictionary<string, object>? _settings;

        static SettingsService()
        {
            LoadSettings();
        }

        // Existing TDP methods...
        public static bool GetTdpCorrectionEnabled()
        {
            lock (_lock)
            {
                if (_settings != null && _settings.TryGetValue(TdpCorrectionKey, out var value))
                {
                    if (value is JsonElement jsonElement)
                    {
                        if (jsonElement.ValueKind == JsonValueKind.True ||
                            jsonElement.ValueKind == JsonValueKind.False)
                        {
                            return jsonElement.GetBoolean();
                        }
                    }
                    else if (value is bool boolValue)
                    {
                        return boolValue;
                    }
                }
                return true; // Default to enabled
            }
        }

        public static void SetTdpCorrectionEnabled(bool enabled)
        {
            lock (_lock)
            {
                if (_settings == null)
                    _settings = new Dictionary<string, object>();

                _settings[TdpCorrectionKey] = enabled;
                SaveSettings();
            }
        }

        public static int GetStartupTdp()
        {
            lock (_lock)
            {
                if (_settings != null && _settings.TryGetValue(StartupTdpKey, out var value))
                {
                    if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Number && jsonElement.TryGetInt32(out int result))
                    {
                        return result;
                    }
                    else if (value is int intValue)
                    {
                        return intValue;
                    }
                }
                return HudraSettings.DEFAULT_STARTUP_TDP;
            }
        }

        public static bool GetUseStartupTdp()
        {
            lock (_lock)
            {
                if (_settings != null && _settings.TryGetValue(UseStartupTdpKey, out var value))
                {
                    if (value is JsonElement jsonElement)
                    {
                        if (jsonElement.ValueKind == JsonValueKind.True || jsonElement.ValueKind == JsonValueKind.False)
                        {
                            return jsonElement.GetBoolean();
                        }
                    }
                    else if (value is bool boolValue)
                    {
                        return boolValue;
                    }
                }
                return true;
            }
        }

        public static void SetUseStartupTdp(bool enabled)
        {
            lock (_lock)
            {
                if (_settings == null)
                    _settings = new Dictionary<string, object>();

                _settings[UseStartupTdpKey] = enabled;
                SaveSettings();
            }
        }

        public static void SetStartupTdp(int tdp)
        {
            lock (_lock)
            {
                if (_settings == null)
                    _settings = new Dictionary<string, object>();

                _settings[StartupTdpKey] = tdp;
                SaveSettings();
            }
        }

        public static int GetLastUsedTdp()
        {
            lock (_lock)
            {
                if (_settings != null && _settings.TryGetValue(LastUsedTdpKey, out var value))
                {
                    if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Number && jsonElement.TryGetInt32(out int result))
                    {
                        return result;
                    }
                    else if (value is int intValue)
                    {
                        return intValue;
                    }
                }
                return HudraSettings.DEFAULT_STARTUP_TDP;
            }
        }

        public static void SetLastUsedTdp(int tdp)
        {
            lock (_lock)
            {
                if (_settings == null)
                    _settings = new Dictionary<string, object>();

                _settings[LastUsedTdpKey] = tdp;
                SaveSettings();
            }
        }

        // NEW: Fan Curve Settings
        public static bool GetFanCurveEnabled()
        {
            lock (_lock)
            {
                if (_settings != null && _settings.TryGetValue(FanCurveEnabledKey, out var value))
                {
                    if (value is JsonElement jsonElement)
                    {
                        if (jsonElement.ValueKind == JsonValueKind.True || jsonElement.ValueKind == JsonValueKind.False)
                        {
                            return jsonElement.GetBoolean();
                        }
                    }
                    else if (value is bool boolValue)
                    {
                        return boolValue;
                    }
                }
                return false; // Default to disabled
            }
        }

        public static void SetFanCurveEnabled(bool enabled)
        {
            lock (_lock)
            {
                if (_settings == null)
                    _settings = new Dictionary<string, object>();

                _settings[FanCurveEnabledKey] = enabled;
                SaveSettings();
            }
        }

        public static FanCurve GetFanCurve()
        {
            lock (_lock)
            {
                try
                {
                    var isEnabled = GetBooleanSetting(FanCurveEnabledKey, false);
                    var pointsJson = GetStringSetting(FanCurvePointsKey, "");
                    var activePreset = GetStringSetting(FanCurveActivePresetKey, "");

                    FanCurvePoint[] points;

                    if (!string.IsNullOrEmpty(pointsJson))
                    {
                        points = JsonSerializer.Deserialize<FanCurvePoint[]>(pointsJson) ?? GetDefaultFanCurvePoints();
                    }
                    else
                    {
                        points = GetDefaultFanCurvePoints();
                    }

                    return new FanCurve
                    {
                        IsEnabled = isEnabled,
                        Points = points,
                        ActivePreset = activePreset
                    };
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading fan curve: {ex.Message}");
                    return new FanCurve
                    {
                        IsEnabled = false,
                        Points = GetDefaultFanCurvePoints(),
                        ActivePreset = ""
                    };
                }
            }
        }

        public static void SetFanCurve(FanCurve fanCurve)
        {
            lock (_lock)
            {
                try
                {
                    SetBooleanSetting(FanCurveEnabledKey, fanCurve.IsEnabled);
                    SetStringSetting(FanCurveActivePresetKey, fanCurve.ActivePreset ?? "");

                    var pointsJson = JsonSerializer.Serialize(fanCurve.Points);
                    SetStringSetting(FanCurvePointsKey, pointsJson);

                    System.Diagnostics.Debug.WriteLine($"Fan curve saved: Enabled={fanCurve.IsEnabled}, Points={fanCurve.Points.Length}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error saving fan curve: {ex.Message}");
                }
            }
        }

        private static FanCurvePoint[] GetDefaultFanCurvePoints()
        {
            return new FanCurvePoint[]
            {
            new() { Temperature = 30, FanSpeed = 20 },
            new() { Temperature = 40, FanSpeed = 30 },
            new() { Temperature = 55, FanSpeed = 50 },
            new() { Temperature = 70, FanSpeed = 75 },
            new() { Temperature = 85, FanSpeed = 100 }
            };
        }

        public static FanCurvePoint[] GetCustomFanCurve()
        {
            lock (_lock)
            {
                try
                {
                    var pointsJson = GetStringSetting(CustomFanCurvePointsKey, "");

                    if (!string.IsNullOrEmpty(pointsJson))
                    {
                        return JsonSerializer.Deserialize<FanCurvePoint[]>(pointsJson) ?? GetDefaultFanCurvePoints();
                    }
                    else
                    {
                        return GetDefaultFanCurvePoints();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading custom fan curve: {ex.Message}");
                    return GetDefaultFanCurvePoints();
                }
            }
        }

        public static void SetCustomFanCurve(FanCurvePoint[] points)
        {
            lock (_lock)
            {
                try
                {
                    var pointsJson = JsonSerializer.Serialize(points);
                    SetStringSetting(CustomFanCurvePointsKey, pointsJson);
                    System.Diagnostics.Debug.WriteLine($"Custom fan curve saved: {points.Length} points");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error saving custom fan curve: {ex.Message}");
                }
            }
        }
        private static bool GetBooleanSetting(string key, bool defaultValue)
        {
            if (_settings != null && _settings.TryGetValue(key, out var value))
            {
                if (value is JsonElement jsonElement &&
                    (jsonElement.ValueKind == JsonValueKind.True || jsonElement.ValueKind == JsonValueKind.False))
                {
                    return jsonElement.GetBoolean();
                }
                else if (value is bool boolValue)
                {
                    return boolValue;
                }
            }
            return defaultValue;
        }

        private static void SetBooleanSetting(string key, bool value)
        {
            if (_settings == null)
                _settings = new Dictionary<string, object>();

            _settings[key] = value;
            SaveSettings();
        }

        private static string GetStringSetting(string key, string defaultValue)
        {
            if (_settings != null && _settings.TryGetValue(key, out var value))
            {
                if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.String)
                {
                    return jsonElement.GetString() ?? defaultValue;
                }
                else if (value is string stringValue)
                {
                    return stringValue;
                }
            }
            return defaultValue;
        }

        private static void SetStringSetting(string key, string value)
        {
            if (_settings == null)
                _settings = new Dictionary<string, object>();

            _settings[key] = value;
            SaveSettings();
        }

        private static int GetIntegerSetting(string key, int defaultValue)
        {
            if (_settings != null && _settings.TryGetValue(key, out var value))
            {
                if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Number)
                {
                    return jsonElement.GetInt32();
                }
                else if (value is int intValue)
                {
                    return intValue;
                }
            }
            return defaultValue;
        }

        private static void SetIntegerSetting(string key, int value)
        {
            if (_settings == null)
                _settings = new Dictionary<string, object>();

            _settings[key] = value;
            SaveSettings();
        }

        private static FanCurve GetDefaultFanCurve()
        {
            // Return sensible default curve if no saved curve exists
            return new FanCurve
            {
                IsEnabled = false,
                Points = new FanCurvePoint[]
                {
                    new FanCurvePoint { Temperature = 30, FanSpeed = 20 },  // Low temp, quiet
                    new FanCurvePoint { Temperature = 40, FanSpeed = 30 },  // Gentle ramp
                    new FanCurvePoint { Temperature = 55, FanSpeed = 50 },  // Mid point
                    new FanCurvePoint { Temperature = 70, FanSpeed = 75 },  // Higher temps
                    new FanCurvePoint { Temperature = 85, FanSpeed = 100 }  // Max protection
                }
            };
        }

        // Existing private methods...
        private static void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    _settings = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                }
                else
                {
                    _settings = new Dictionary<string, object>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
                _settings = new Dictionary<string, object>();
            }
        }
        private static void SaveSettings()
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                var json = JsonSerializer.Serialize(_settings, options);
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }

        // <summary>
        /// Gets whether HUDRA should start with Windows
        /// </summary>
        public static bool GetStartupEnabled()
        {
            return GetBooleanSetting(STARTUP_ENABLED_KEY, false);
        }

        /// <summary>
        /// Sets whether HUDRA should start with Windows using Task Scheduler
        /// </summary>
        public static bool SetStartupEnabled(bool enabled)
        {
            try
            {
                // Apply to system using Task Scheduler first
                var success = StartupService.SetStartupEnabled(enabled);

                if (success)
                {
                    // Only save the setting if system operation succeeded
                    SetBooleanSetting(STARTUP_ENABLED_KEY, enabled);
                    System.Diagnostics.Debug.WriteLine($"🚀 Startup {(enabled ? "enabled" : "disabled")} via Task Scheduler");
                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Failed to {(enabled ? "enable" : "disable")} startup");
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Exception setting startup: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets whether HUDRA should minimize to tray when starting at boot
        /// </summary>
        public static bool GetMinimizeToTrayOnStartup()
        {
            return GetBooleanSetting(MINIMIZE_TO_TRAY_ON_STARTUP_KEY, false); // Default to maximized for startup
        }

        /// <summary>
        /// Sets whether HUDRA should minimize to tray when starting at boot
        /// </summary>
        public static void SetMinimizeToTrayOnStartup(bool minimize)
        {
            SetBooleanSetting(MINIMIZE_TO_TRAY_ON_STARTUP_KEY, minimize);
        }

        /// <summary>
        /// Synchronizes saved settings with actual system state
        /// Called on app startup to ensure consistency
        /// </summary>
        public static void SyncStartupState()
        {
            try
            {
                var settingEnabled = GetStartupEnabled();
                var systemEnabled = StartupService.IsStartupEnabled();

                if (settingEnabled != systemEnabled)
                {
                    System.Diagnostics.Debug.WriteLine($"🔄 Syncing startup state: Setting={settingEnabled}, System={systemEnabled}");

                    // Update system to match saved setting
                    StartupService.SetStartupEnabled(settingEnabled);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Error syncing startup state: {ex.Message}");
            }
        }

        // Power Profile Settings
        public static Guid? GetPreferredPowerProfile()
        {
            lock (_lock)
            {
                var guidString = GetStringSetting(PreferredPowerProfileKey, "");
                if (!string.IsNullOrEmpty(guidString) && Guid.TryParse(guidString, out var guid))
                {
                    return guid;
                }
                return null;
            }
        }

        public static void SetPreferredPowerProfile(Guid profileId)
        {
            lock (_lock)
            {
                SetStringSetting(PreferredPowerProfileKey, profileId.ToString("D"));
            }
        }

        public static bool GetRestorePowerProfileOnStartup()
        {
            return GetBooleanSetting(RestorePowerProfileOnStartupKey, false);
        }

        public static void SetRestorePowerProfileOnStartup(bool restore)
        {
            SetBooleanSetting(RestorePowerProfileOnStartupKey, restore);
        }

        // CPU Boost Settings
        public static bool GetCpuBoostEnabled()
        {
            return GetBooleanSetting(CpuBoostEnabledKey, false); // Default to disabled
        }

        public static void SetCpuBoostEnabled(bool enabled)
        {
            SetBooleanSetting(CpuBoostEnabledKey, enabled);
        }

        public static bool GetRestoreCpuBoostOnStartup()
        {
            return GetBooleanSetting(RestoreCpuBoostOnStartupKey, true); // Default to restore
        }

        public static void SetRestoreCpuBoostOnStartup(bool restore)
        {
            SetBooleanSetting(RestoreCpuBoostOnStartupKey, restore);
        }

        // Intelligent Power Switching Settings
        public static Guid? GetDefaultPowerProfile()
        {
            lock (_lock)
            {
                var guidString = GetStringSetting(DefaultPowerProfileKey, "");
                if (!string.IsNullOrEmpty(guidString) && Guid.TryParse(guidString, out var guid))
                {
                    return guid;
                }
                return null;
            }
        }

        public static void SetDefaultPowerProfile(Guid profileId)
        {
            lock (_lock)
            {
                SetStringSetting(DefaultPowerProfileKey, profileId.ToString("D"));
            }
        }

        public static Guid? GetGamingPowerProfile()
        {
            lock (_lock)
            {
                var guidString = GetStringSetting(GamingPowerProfileKey, "");
                if (!string.IsNullOrEmpty(guidString) && Guid.TryParse(guidString, out var guid))
                {
                    return guid;
                }
                return null;
            }
        }

        public static void SetGamingPowerProfile(Guid profileId)
        {
            lock (_lock)
            {
                SetStringSetting(GamingPowerProfileKey, profileId.ToString("D"));
            }
        }

        public static bool GetIntelligentPowerSwitchingEnabled()
        {
            return GetBooleanSetting(IntelligentPowerSwitchingKey, true); // Default to enabled
        }

        public static void SetIntelligentPowerSwitchingEnabled(bool enabled)
        {
            SetBooleanSetting(IntelligentPowerSwitchingKey, enabled);
        }

        // FPS Limiter Settings
        public static int GetSelectedFpsLimit()
        {
            return GetIntegerSetting(SelectedFpsLimitKey, 60);
        }

        public static void SetSelectedFpsLimit(int fpsLimit)
        {
            SetIntegerSetting(SelectedFpsLimitKey, fpsLimit);
        }

        public static bool GetStartRtssWithHudra()
        {
            return GetBooleanSetting(StartRtssWithHudraKey, false);
        }

        public static void SetStartRtssWithHudra(bool enabled)
        {
            SetBooleanSetting(StartRtssWithHudraKey, enabled);
        }

        // Lossless Scaling Settings
        public static LosslessScalingSettings GetLosslessScalingSettings()
        {
            lock (_lock)
            {
                return new LosslessScalingSettings
                {
                    UpscalingEnabled = GetBooleanSetting(LS_UPSCALING_ENABLED_KEY, true),
                    FrameGenMultiplier = (LosslessScalingFrameGen)GetIntegerSetting(LS_FRAME_GEN_MULTIPLIER_KEY, 0),
                    FlowScale = GetIntegerSetting(LS_FLOW_SCALE_KEY, 70)
                };
            }
        }

        public static void SetLosslessScalingSettings(LosslessScalingSettings settings)
        {
            lock (_lock)
            {
                SetBooleanSetting(LS_UPSCALING_ENABLED_KEY, settings.UpscalingEnabled);
                SetIntegerSetting(LS_FRAME_GEN_MULTIPLIER_KEY, (int)settings.FrameGenMultiplier);
                SetIntegerSetting(LS_FLOW_SCALE_KEY, settings.FlowScale);
            }
        }

        // Hotkey settings methods
        public static string GetHideShowHotkeyKey()
        {
            return GetStringSetting(HIDE_SHOW_HOTKEY_KEY, ""); // Default to empty, will use fallback in TurboService
        }

        public static void SetHideShowHotkeyKey(string key)
        {
            SetStringSetting(HIDE_SHOW_HOTKEY_KEY, key);
        }

        public static string GetHideShowHotkeyModifiers()
        {
            return GetStringSetting(HIDE_SHOW_HOTKEY_MODIFIERS, "Win+Alt+Ctrl"); // Default to current combination
        }

        public static void SetHideShowHotkeyModifiers(string modifiers)
        {
            SetStringSetting(HIDE_SHOW_HOTKEY_MODIFIERS, modifiers);
        }

        // Enhanced game detection settings methods
        public static bool IsEnhancedLibraryScanningEnabled()
        {
            return GetBooleanSetting(ENHANCED_LIBRARY_SCANNING_KEY, true); // Default to enabled
        }

        public static void SetEnhancedLibraryScanningEnabled(bool enabled)
        {
            SetBooleanSetting(ENHANCED_LIBRARY_SCANNING_KEY, enabled);
        }

        public static int GetGameDatabaseRefreshInterval()
        {
            return GetIntegerSetting(GAME_DATABASE_REFRESH_INTERVAL_KEY, 15); // Default to 15 minutes
        }

        public static void SetGameDatabaseRefreshInterval(int minutes)
        {
            // Clamp to valid range (5-60 minutes)
            int clampedValue = Math.Max(5, Math.Min(60, minutes));
            SetIntegerSetting(GAME_DATABASE_REFRESH_INTERVAL_KEY, clampedValue);
        }

        // First-run experience methods
        public static bool GetHasCompletedFirstRun()
        {
            return GetBooleanSetting(HAS_COMPLETED_FIRST_RUN_KEY, false);
        }

        public static void SetHasCompletedFirstRun(bool completed)
        {
            SetBooleanSetting(HAS_COMPLETED_FIRST_RUN_KEY, completed);
        }

        public static bool GetEnhancedGameDetectionEnabled()
        {
            return GetBooleanSetting(ENHANCED_GAME_DETECTION_ENABLED_KEY, true); // Default to enabled
        }

        public static void SetEnhancedGameDetectionEnabled(bool enabled)
        {
            SetBooleanSetting(ENHANCED_GAME_DETECTION_ENABLED_KEY, enabled);
        }

        /// <summary>
        /// Reads preferences set by the installer and applies them on first launch.
        /// Should be called early in app startup, before UI is shown.
        ///
        /// Registry keys read (written by Inno Setup installer):
        /// - HKLM\SOFTWARE\HUDRA\FirstRun (DWORD) - Set to 1 by installer, cleared after first run
        /// - HKLM\SOFTWARE\HUDRA\EnableStartup (DWORD) - User's startup preference from installer
        /// </summary>
        public static void ApplyInstallerPreferences()
        {
            try
            {
                // Check if this is a first run after installation (installer sets FirstRun=1)
                using var key = Registry.LocalMachine.OpenSubKey(REGISTRY_PATH, writable: true);
                if (key == null)
                {
                    System.Diagnostics.Debug.WriteLine("No HUDRA registry key found - not installed via installer");
                    return;
                }

                // Check FirstRun flag from installer
                var firstRunValue = key.GetValue("FirstRun");
                bool isFirstRun = firstRunValue is int firstRun && firstRun == 1;

                if (!isFirstRun)
                {
                    System.Diagnostics.Debug.WriteLine("Not first run after installation - skipping installer preferences");
                    return;
                }

                System.Diagnostics.Debug.WriteLine("First launch after installation - applying installer preferences");

                // Clear the FirstRun flag so we don't run this again
                key.SetValue("FirstRun", 0, RegistryValueKind.DWord);

                // Read startup preference from installer (EnableStartup)
                var enableStartupValue = key.GetValue("EnableStartup");
                if (enableStartupValue != null)
                {
                    bool installerRequestedStartup = Convert.ToInt32(enableStartupValue) == 1;
                    System.Diagnostics.Debug.WriteLine($"Installer startup preference: {installerRequestedStartup}");

                    // Sync our setting with what the installer configured
                    // Note: The installer already created/removed the scheduled task via schtasks.exe
                    // We just need to sync our internal setting state
                    SetBooleanSetting(STARTUP_ENABLED_KEY, installerRequestedStartup);

                    // Verify the scheduled task exists if startup was enabled
                    if (installerRequestedStartup)
                    {
                        bool taskExists = StartupService.IsStartupEnabled();
                        if (taskExists)
                        {
                            System.Diagnostics.Debug.WriteLine("Startup task verified - created by installer");
                        }
                        else
                        {
                            // Task should have been created by installer, but if not, create it now
                            System.Diagnostics.Debug.WriteLine("Startup task not found - creating now");
                            StartupService.SetStartupEnabled(true);
                        }
                    }
                }

                // Mark first run complete in our settings
                SetHasCompletedFirstRun(true);

                System.Diagnostics.Debug.WriteLine("Installer preferences applied successfully");
            }
            catch (UnauthorizedAccessException)
            {
                // Can't write to HKLM without admin - this is expected if not running elevated
                // Just skip the FirstRun flag clear; we'll try again next launch
                System.Diagnostics.Debug.WriteLine("Cannot write to registry (not admin) - will retry next launch");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error applying installer preferences: {ex.Message}");
                // Continue with defaults on error
            }
        }

    }
}
