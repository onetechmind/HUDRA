using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using HUDRA.Models;

namespace HUDRA.Services
{
    /// <summary>
    /// Centralized hardware detection service that identifies the device manufacturer and model.
    /// Detection runs once and is stored permanently in settings (hardware can't change on handhelds).
    /// </summary>
    public static class HardwareDetectionService
    {
        private static DetectedDevice? _cachedDevice;

        /// <summary>
        /// Gets the detected device, loading from settings or detecting once if needed.
        /// Hardware can't change on handhelds, so detection only runs once ever.
        /// </summary>
        public static DetectedDevice GetDetectedDevice()
        {
            if (_cachedDevice != null)
                return _cachedDevice;

            // Try loading from settings first
            _cachedDevice = SettingsService.GetDetectedDevice();
            if (_cachedDevice != null)
            {
                Debug.WriteLine($"HardwareDetection: Loaded from settings - {_cachedDevice.Manufacturer} {_cachedDevice.DeviceName}");
                return _cachedDevice;
            }

            // Not in settings - run detection once and store permanently
            Debug.WriteLine("HardwareDetection: No saved device info, running detection...");
            _cachedDevice = DetectDevice();
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

                    // Check for Win Mini (G1217 series) - check before Win 4
                    var winMiniModels = new[] { "G1217", "GPD WIN MINI", "WIN MINI" };
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

                    // Check for X1 series
                    var x1Models = new[] { "X1", "ONEXPLAYER X1" };
                    var f1Models = new[] { "F1", "ONEXFLY" };

                    if (x1Models.Any(m => device.RawModel.Contains(m, StringComparison.OrdinalIgnoreCase) ||
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
