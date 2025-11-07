using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;

namespace HUDRA.Services.FanControl.Devices
{
    /// <summary>
    /// Fan control implementation for Lenovo Legion Go and Legion Go 2 devices.
    /// Uses WMI (Windows Management Instrumentation) instead of EC communication.
    /// </summary>
    public class LenovoLegionGoDevice : IFanControlDevice
    {
        private bool _disposed = false;
        private bool _isInitialized = false;
        private FanControlMode _currentMode = FanControlMode.Hardware;
        private FanCurvePoint[] _currentUserCurve = Array.Empty<FanCurvePoint>();
        private double _lastUploadedSpeed = -1; // Cache to avoid redundant WMI calls

        public string ManufacturerName => "Lenovo";
        public string DeviceName => "Legion Go";

        public DeviceCapabilities Capabilities { get; } = new DeviceCapabilities
        {
            SupportedFeatures = new HashSet<FanControlCapability>
            {
                FanControlCapability.BasicSpeedControl,
                FanControlCapability.AdvancedCurves
            },
            MinFanSpeed = 0,
            MaxFanSpeed = 100,
            SupportsAutoDetection = true,
            SupportedModels = new[]
            {
                "Legion Go",       // Display name
                "83E1",            // Legion Go 1 model number
                "LNVNB161822",     // Legion Go 1 model ID
                "83N0",            // Legion Go 2 model number (verified)
                "8ASP2",           // Legion Go 2 model number (alternate)
                "8AHP2"            // Legion Go 2 model number (alternate)
            }
        };

        public ECRegisterMap RegisterMap => throw new NotSupportedException(
            "Legion Go uses WMI-based fan control and does not use EC registers");

        public bool IsInitialized => _isInitialized;

        public uint? TurboButtonECAddress => null; // Legion Go doesn't use EC

        // Legion Go fan modes
        private enum LegionMode
        {
            Quiet = 0x01,
            Balanced = 0x02,
            Performance = 0x03,
            Custom = 0xFF
        }

        // WMI Capability IDs for fan control
        private enum CapabilityID : uint
        {
            FanFullSpeed = 0x04020000,        // Full speed override
            CpuCurrentFanSpeed = 0x04030001,  // CPU fan RPM (read-only)
            GpuCurrentFanSpeed = 0x04030002,  // GPU fan RPM (read-only)
            CpuCurrentTemperature = 0x05040000, // CPU temperature (read-only)
            GpuCurrentTemperature = 0x05050000  // GPU temperature (read-only)
        }

        // Default fan table from Lenovo (Balanced mode)
        private static readonly LenovoFanTable DefaultFanTable =
            new LenovoFanTable(new ushort[] { 44, 48, 55, 60, 71, 79, 87, 87, 100, 100 });

        public bool Initialize()
        {
            try
            {
                Debug.WriteLine("Initializing Lenovo Legion Go fan control...");

                if (!IsDeviceSupported())
                {
                    Debug.WriteLine("Device not supported or not a Lenovo Legion Go");
                    return false;
                }

                // Test WMI provider availability
                if (!TestWmiProviderAvailability())
                {
                    Debug.WriteLine("Lenovo WMI providers not available");
                    return false;
                }

                _isInitialized = true;
                Debug.WriteLine("Lenovo Legion Go fan control initialized successfully");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initialize Legion Go fan control: {ex.Message}");
                return false;
            }
        }

        public bool IsDeviceSupported()
        {
            try
            {
                string? manufacturer = GetSystemInfo("Manufacturer");
                string? model = GetSystemInfo("Model");

                Debug.WriteLine($"System Info - Manufacturer: {manufacturer}, Model: {model}");

                // Check manufacturer
                if (!"LENOVO".Equals(manufacturer, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // Check if model matches any supported Legion Go models
                bool modelMatch = Capabilities.SupportedModels.Any(m =>
                    model?.Contains(m, StringComparison.OrdinalIgnoreCase) == true);

                if (modelMatch)
                {
                    Debug.WriteLine($"Detected Lenovo Legion Go: {model}");
                    return true;
                }

                Debug.WriteLine("Device not recognized as Lenovo Legion Go");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking device support: {ex.Message}");
                return false;
            }
        }

        public bool SetFanControl(FanControlMode mode)
        {
            if (!_isInitialized)
                return false;

            try
            {
                if (mode == FanControlMode.Hardware)
                {
                    // Hardware mode: Restore Lenovo's default (Balanced mode + factory curve)
                    Debug.WriteLine("Setting fan control to Hardware mode (Balanced + default curve)");

                    if (!SetSmartFanMode((int)LegionMode.Balanced))
                    {
                        Debug.WriteLine("Failed to set Balanced mode");
                        return false;
                    }

                    if (!SetFanTable(DefaultFanTable))
                    {
                        Debug.WriteLine("Failed to set default fan table");
                        return false;
                    }
                }
                else // Software mode
                {
                    // Software mode: Switch to Custom mode
                    // The actual fan table will be uploaded when SetFanDuty() is called
                    Debug.WriteLine("Setting fan control to Software mode (Custom)");

                    if (!SetSmartFanMode((int)LegionMode.Custom))
                    {
                        Debug.WriteLine("Failed to set Custom mode");
                        return false;
                    }
                }

                _currentMode = mode;
                _lastUploadedSpeed = -1; // Reset cache when mode changes
                Debug.WriteLine($"Fan control mode set to: {mode}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting fan control mode: {ex.Message}");
                return false;
            }
        }

        public bool SetFanDuty(double percent)
        {
            if (!_isInitialized)
                return false;

            try
            {
                // Auto-switch to Software mode if needed
                if (_currentMode != FanControlMode.Software)
                {
                    if (!SetFanControl(FanControlMode.Software))
                        return false;
                }

                // Optimization: Skip WMI call if speed hasn't changed significantly
                // This reduces redundant uploads since temperature polling happens every 2 seconds
                double roundedPercent = Math.Round(percent, 1);
                if (Math.Abs(roundedPercent - _lastUploadedSpeed) < 0.5)
                {
                    return true; // No significant change, skip update
                }

                // NOTE: For Legion Go, we create a flat fan table at the requested percentage.
                // This is because HUDRA's architecture polls temperature and sets fan speed,
                // rather than uploading a curve once and letting firmware handle it.
                // Future enhancement: Upload the full curve once and let Legion Go firmware
                // handle temperature monitoring natively.
                var fanSpeeds = new ushort[10];
                ushort speedValue = (ushort)Math.Clamp(Math.Round(percent), 0, 100);

                for (int i = 0; i < 10; i++)
                {
                    fanSpeeds[i] = speedValue;
                }

                var fanTable = new LenovoFanTable(fanSpeeds);
                bool success = SetFanTable(fanTable);

                if (success)
                {
                    _lastUploadedSpeed = roundedPercent;
                    Debug.WriteLine($"Fan speed set to: {percent:F1}%");
                }

                return success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting fan duty: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Sets a custom fan curve by converting HUDRA's temperature-based curve
        /// to Legion Go's 10-point fan table.
        /// </summary>
        public bool SetFanCurve(FanCurvePoint[] curvePoints)
        {
            if (!_isInitialized || curvePoints == null || curvePoints.Length == 0)
                return false;

            try
            {
                // Store the current user curve
                _currentUserCurve = curvePoints;

                // Auto-switch to Software mode if needed
                if (_currentMode != FanControlMode.Software)
                {
                    if (!SetFanControl(FanControlMode.Software))
                        return false;
                }

                // Convert the curve to a fan table
                var fanTable = ConvertCurveToFanTable(curvePoints);

                bool success = SetFanTable(fanTable);

                if (success)
                {
                    Debug.WriteLine($"Fan curve applied: {fanTable}");
                }

                return success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting fan curve: {ex.Message}");
                return false;
            }
        }

        public FanStatus GetFanStatus()
        {
            var status = new FanStatus();

            if (!_isInitialized)
                return status;

            try
            {
                // Get current fan mode
                int currentMode = GetSmartFanMode();
                status.IsControlEnabled = currentMode == (int)LegionMode.Custom;

                // For Legion Go, we can't easily read the current duty cycle
                // We could potentially read fan RPM, but that's more complex
                status.CurrentDutyPercent = 0; // Not easily available via WMI

                status.LastUpdated = DateTime.Now;
                return status;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading fan status: {ex.Message}");
                return status;
            }
        }

        #region WMI Methods

        /// <summary>
        /// Sets the Lenovo smart fan mode (Quiet/Balanced/Performance/Custom).
        /// </summary>
        private bool SetSmartFanMode(int fanMode)
        {
            try
            {
                bool success = WmiHelper.Call(
                    scope: "root\\WMI",
                    query: "SELECT * FROM LENOVO_GAMEZONE_DATA",
                    methodName: "SetSmartFanMode",
                    methodParams: new Dictionary<string, object>
                    {
                        { "Data", fanMode }
                    }
                );

                if (success)
                {
                    Debug.WriteLine($"Smart fan mode set to: {(LegionMode)fanMode}");
                }

                return success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in SetSmartFanMode: {ex.Message}, Mode: {fanMode}");
                return false;
            }
        }

        /// <summary>
        /// Gets the current Lenovo smart fan mode.
        /// </summary>
        private int GetSmartFanMode()
        {
            try
            {
                int? mode = WmiHelper.Call<int>(
                    scope: "root\\WMI",
                    query: "SELECT * FROM LENOVO_GAMEZONE_DATA",
                    methodName: "GetSmartFanMode",
                    methodParams: new Dictionary<string, object>(),
                    resultSelector: pdc => Convert.ToInt32(pdc["Data"].Value)
                );

                return mode ?? -1;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in GetSmartFanMode: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Uploads a custom fan table to the device firmware.
        /// </summary>
        private bool SetFanTable(LenovoFanTable fanTable)
        {
            try
            {
                byte[] tableBytes = fanTable.GetBytes();

                bool success = WmiHelper.Call(
                    scope: "root\\WMI",
                    query: "SELECT * FROM LENOVO_FAN_METHOD",
                    methodName: "Fan_Set_Table",
                    methodParams: new Dictionary<string, object>
                    {
                        { "FanTable", tableBytes }
                    }
                );

                if (!success)
                {
                    Debug.WriteLine($"Failed to set fan table: {BitConverter.ToString(tableBytes)}");
                }

                return success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in SetFanTable: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Enables or disables full-speed fan override mode.
        /// </summary>
        private bool SetFanFullSpeed(bool enabled)
        {
            try
            {
                bool success = WmiHelper.Call(
                    scope: "root\\WMI",
                    query: "SELECT * FROM LENOVO_OTHER_METHOD",
                    methodName: "SetFeatureValue",
                    methodParams: new Dictionary<string, object>
                    {
                        { "IDs", (uint)CapabilityID.FanFullSpeed },
                        { "value", enabled ? 1 : 0 }
                    }
                );

                if (success)
                {
                    Debug.WriteLine($"Fan full speed set to: {enabled}");
                }

                return success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in SetFanFullSpeed: {ex.Message}, Enabled: {enabled}");
                return false;
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Tests if Lenovo WMI providers are available.
        /// </summary>
        private bool TestWmiProviderAvailability()
        {
            try
            {
                // Try to get the current fan mode as a connectivity test
                int mode = GetSmartFanMode();
                return mode != -1;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Converts HUDRA's temperature-based fan curve to Legion Go's 10-point fan table.
        /// Samples the curve at 10 evenly-spaced temperature points.
        /// </summary>
        private LenovoFanTable ConvertCurveToFanTable(FanCurvePoint[] curvePoints)
        {
            // Sample temperatures spanning the typical operating range
            // These correspond to the 10 entries in the Legion Go fan table
            var sampleTemps = new[] { 30, 40, 50, 60, 65, 70, 75, 80, 85, 90 };

            var fanSpeeds = new ushort[10];

            for (int i = 0; i < 10; i++)
            {
                double speed = InterpolateFanSpeed(sampleTemps[i], curvePoints);
                fanSpeeds[i] = (ushort)Math.Clamp(Math.Round(speed), 0, 100);
            }

            return new LenovoFanTable(fanSpeeds);
        }

        /// <summary>
        /// Interpolates fan speed from a temperature-based curve.
        /// </summary>
        private double InterpolateFanSpeed(double temperature, FanCurvePoint[] points)
        {
            // Sort points by temperature
            var sortedPoints = points.OrderBy(p => p.Temperature).ToArray();

            // If temperature is below the first point, use the first point's fan speed
            if (temperature <= sortedPoints[0].Temperature)
            {
                return sortedPoints[0].FanSpeed;
            }

            // If temperature is above the last point, use the last point's fan speed
            if (temperature >= sortedPoints[^1].Temperature)
            {
                return sortedPoints[^1].FanSpeed;
            }

            // Find the two points to interpolate between
            for (int i = 0; i < sortedPoints.Length - 1; i++)
            {
                var point1 = sortedPoints[i];
                var point2 = sortedPoints[i + 1];

                if (temperature >= point1.Temperature && temperature <= point2.Temperature)
                {
                    // Linear interpolation
                    var tempRange = point2.Temperature - point1.Temperature;
                    var speedRange = point2.FanSpeed - point1.FanSpeed;
                    var tempOffset = temperature - point1.Temperature;

                    return point1.FanSpeed + (speedRange * (tempOffset / tempRange));
                }
            }

            // Fallback (should never reach here)
            return 50.0;
        }

        /// <summary>
        /// Gets system information via WMI.
        /// </summary>
        private static string? GetSystemInfo(string property)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM Win32_ComputerSystem");
                using var collection = searcher.Get();
                var result = collection.Cast<ManagementObject>().FirstOrDefault();
                return result?[property]?.ToString();
            }
            catch
            {
                return null;
            }
        }

        #endregion

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    // Reset to hardware control on app exit
                    if (_isInitialized)
                    {
                        SetFanFullSpeed(false);
                        SetFanControl(FanControlMode.Hardware);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error during Legion Go cleanup: {ex.Message}");
                }

                _disposed = true;
                Debug.WriteLine("Legion Go fan control disposed");
            }
        }
    }
}
