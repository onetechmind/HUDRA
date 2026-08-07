using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using HUDRA.Configuration;
using HUDRA.Models;
using HUDRA.Services.FanControl;

namespace HUDRA.Services
{
    /// <summary>
    /// Centralized hardware detection service that identifies the device manufacturer and model.
    /// Detection runs once and is stored permanently in settings (hardware can't change on handhelds).
    /// </summary>
    public static class HardwareDetectionService
    {
        // Bump this version whenever detection logic changes (e.g. new device added, new model string).
        // Any cached result with a lower version is discarded and detection re-runs automatically.
        // v2: OneXPlayer X2 Mini series added.
        private const int CURRENT_DETECTION_VERSION = 2;

        private static DetectedDevice? _cachedDevice;

        /// <summary>
        /// Gets the detected device, loading from settings or detecting once if needed.
        /// Hardware can't change on handhelds, so detection only runs once per schema version.
        /// </summary>
        public static DetectedDevice GetDetectedDevice()
        {
            if (_cachedDevice != null)
                return _cachedDevice;

            // Try loading from settings first
            var stored = SettingsService.GetDetectedDevice();
            if (stored != null && stored.DetectionVersion >= CURRENT_DETECTION_VERSION)
            {
                _cachedDevice = stored;
                Debug.WriteLine($"HardwareDetection: Loaded from settings - {_cachedDevice.Manufacturer} {_cachedDevice.DeviceName}");
                return _cachedDevice;
            }

            if (stored != null)
            {
                Debug.WriteLine($"HardwareDetection: Cached result is version {stored.DetectionVersion}, current is {CURRENT_DETECTION_VERSION} - re-detecting...");
            }
            else
            {
                Debug.WriteLine("HardwareDetection: No saved device info, running detection...");
            }

            _cachedDevice = DetectDevice();
            _cachedDevice.DetectionVersion = CURRENT_DETECTION_VERSION;
            SettingsService.SetDetectedDevice(_cachedDevice);
            return _cachedDevice;
        }

        private static DetectedDevice DetectDevice()
        {
            var device = new DetectedDevice();

            try
            {
                // Get raw WMI info
                device.RawManufacturer = GetSystemInfo("Manufacturer") ?? "";
                device.RawModel = GetSystemInfo("Model") ?? "";
                device.RawVersion = GetSystemInfo("Version") ?? "";

                Debug.WriteLine($"HardwareDetection: Raw info - Manufacturer: {device.RawManufacturer}, Model: {device.RawModel}, Version: {device.RawVersion}");

                // Detect Lenovo
                if (device.RawManufacturer.Contains("LENOVO", StringComparison.OrdinalIgnoreCase))
                {
                    device.Manufacturer = DeviceManufacturer.Lenovo;
                    device.SupportsLenovoWmi = CheckLenovoWmiAvailable();

                    // Check for Legion Go specifically
                    var legionGoModels = new[] { "83E1", "LNVNB161822", "83N0", "8ASP2", "8AHP2", "Legion Go" };
                    if (legionGoModels.Any(m => device.RawModel.Contains(m, StringComparison.OrdinalIgnoreCase)))
                    {
                        device.DeviceName = "Legion Go";
                        device.SupportsFanControl = true;
                    }
                    else
                    {
                        device.DeviceName = "Lenovo Device";
                    }
                }
                // Detect GPD
                else if (device.RawManufacturer.Contains("GPD", StringComparison.OrdinalIgnoreCase))
                {
                    device.Manufacturer = DeviceManufacturer.GPD;

                    // Check for Win Mini (G1617 series) - check before Win 4
                    var winMiniModels = new[] { "G1617", "GPD WIN MINI", "WIN MINI" };
                    var win4Models = new[] { "G1618-04", "GPD WIN 4", "WIN 4" };

                    if (winMiniModels.Any(m => device.RawModel.Contains(m, StringComparison.OrdinalIgnoreCase) ||
                                               device.RawVersion.Contains(m, StringComparison.OrdinalIgnoreCase)))
                    {
                        device.DeviceName = "Win Mini";
                        device.SupportsFanControl = true;
                    }
                    else if (win4Models.Any(m => device.RawModel.Contains(m, StringComparison.OrdinalIgnoreCase) ||
                                                  device.RawVersion.Contains(m, StringComparison.OrdinalIgnoreCase)))
                    {
                        device.DeviceName = "Win 4 Series";
                        device.SupportsFanControl = true;
                    }
                    else
                    {
                        device.DeviceName = "GPD Device";
                    }
                }
                // Detect OneXPlayer
                else if (device.RawManufacturer.Contains("ONE-NETBOOK", StringComparison.OrdinalIgnoreCase) ||
                         device.RawManufacturer.Contains("ONEXPLAYER", StringComparison.OrdinalIgnoreCase) ||
                         device.RawManufacturer.Contains("ONE NETBOOK", StringComparison.OrdinalIgnoreCase))
                {
                    device.Manufacturer = DeviceManufacturer.OneXPlayer;

                    // X2 Mini before X1 (most specific). The device reports
                    // "ONEXPLAYER X2Mini PRO" - no space in "X2Mini".
                    var x2Models = new[] { "X2MINI", "X2 MINI" };
                    var x1Models = new[] { "X1", "ONEXPLAYER X1" };
                    var f1Models = new[] { "F1", "ONEXFLY", "APEX" };

                    if (x2Models.Any(m => device.RawModel.Contains(m, StringComparison.OrdinalIgnoreCase) ||
                                          device.RawVersion.Contains(m, StringComparison.OrdinalIgnoreCase)))
                    {
                        device.DeviceName = "X2 Series";
                        device.SupportsFanControl = true;
                    }
                    else if (x1Models.Any(m => device.RawModel.Contains(m, StringComparison.OrdinalIgnoreCase) ||
                                          device.RawVersion.Contains(m, StringComparison.OrdinalIgnoreCase)))
                    {
                        device.DeviceName = "X1 Series";
                        device.SupportsFanControl = true;
                    }
                    else if (f1Models.Any(m => device.RawModel.Contains(m, StringComparison.OrdinalIgnoreCase) ||
                                               device.RawVersion.Contains(m, StringComparison.OrdinalIgnoreCase)))
                    {
                        device.DeviceName = "F1 Series";
                        device.SupportsFanControl = true;
                    }
                    else
                    {
                        device.DeviceName = "OneXPlayer Device";
                    }
                }

                Debug.WriteLine($"HardwareDetection: Detected {device.Manufacturer} {device.DeviceName}, FanControl: {device.SupportsFanControl}, LenovoWmi: {device.SupportsLenovoWmi}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HardwareDetection: Error during detection - {ex.Message}");
            }

            return device;
        }

        private static bool CheckLenovoWmiAvailable()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM LENOVO_OTHER_METHOD");
                return searcher.Get().Count > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Returns the TDP limits appropriate for the detected device.
        /// Falls back to HudraSettings constants if no device is detected or
        /// the detected device has not been initialized by FanControlService yet.
        /// </summary>
        public static (int MinTdp, int MaxTdp) GetTdpLimits()
        {
            try
            {
                var fanDevice = DeviceDetectionService.LastDetectedDevice;
                if (fanDevice?.IsInitialized == true)
                {
                    return (fanDevice.Capabilities.MinTdpWatts, fanDevice.Capabilities.MaxTdpWatts);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HardwareDetection: Failed to get device TDP limits, using defaults. {ex.Message}");
            }

            return (HudraSettings.MIN_TDP, HudraSettings.MAX_TDP);
        }

        private static string? GetSystemInfo(string property)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem");
                foreach (ManagementObject obj in searcher.Get())
                {
                    return obj[property]?.ToString();
                }
            }
            catch { }
            return null;
        }
    }
}
