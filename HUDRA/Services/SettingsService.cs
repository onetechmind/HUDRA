using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using HUDRA.Configuration;

namespace HUDRA.Services
{
    public static class SettingsService
    {
        private const string TdpCorrectionKey = "TdpCorrectionEnabled";
        private const string StartupTdpKey = "StartupTdp";
        private const string UseStartupTdpKey = "UseStartupTdp";
        private const string LastUsedTdpKey = "LastUsedTdp";
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
    }
}