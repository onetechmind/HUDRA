using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using HUDRA.Services;

namespace HUDRA.Services.FanControl.Devices
{
    public class OneXPlayerX1Device : FanControlDeviceBase
    {
        public override string ManufacturerName => "OneXPlayer";
        public override string DeviceName => "X1 Series";

        public override ECRegisterMap RegisterMap { get; } = new ECRegisterMap
        {
            FanControlAddress = 0x44A,
            FanDutyAddress = 0x44B,
            StatusCommandPort = 0x4E,
            DataPort = 0x4F,
            FanValueMin = 0,
            FanValueMax = 184,
            Protocol = new ECProtocolConfig
            {
                // OneXPlayer X1 specific protocol
                AddressSelectHigh = 0x2E,
                AddressSetHigh = 0x11,
                AddressSelectLow = 0x2E,
                AddressSetLow = 0x10,
                DataSelect = 0x2E,
                DataCommand = 0x12,
                AddressPort = 0x2F,
                ReadDataSelect = 0x2F
            }
        };

        public override DeviceCapabilities Capabilities { get; } = new DeviceCapabilities
        {
            SupportedFeatures = new HashSet<FanControlCapability>
            {
                FanControlCapability.BasicSpeedControl
            },
            MinFanSpeed = 0,
            MaxFanSpeed = 100,
            SupportsAutoDetection = true,
            SupportedModels = new[] { "ONEXPLAYER X1", "ONEXPLAYER X1 MINI", "ONEXPLAYER X1 PRO" }
        };

        public override bool IsDeviceSupported()
        {
            try
            {
                string? manufacturer = GetSystemInfo("Manufacturer");
                string? model = GetSystemInfo("Model");
                string? version = GetSystemInfo("Version");

                Debug.WriteLine($"System Info - Manufacturer: {manufacturer}, Model: {model}, Version: {version}");

                var supportedManufacturers = new[] { "ONE-NETBOOK", "ONEXPLAYER", "ONE NETBOOK" };
                var supportedModels = new[] { "X1", "ONEXPLAYER X1", "ONEXPLAYER X1 MINI", "ONEXPLAYER X1 PRO" };

                bool manufacturerMatch = supportedManufacturers.Any(m =>
                    manufacturer?.Contains(m, StringComparison.OrdinalIgnoreCase) == true);

                bool modelMatch = supportedModels.Any(m =>
                    model?.Contains(m, StringComparison.OrdinalIgnoreCase) == true ||
                    version?.Contains(m, StringComparison.OrdinalIgnoreCase) == true);

                if (manufacturerMatch && modelMatch)
                {
                    Debug.WriteLine("OneXPlayer X1 device detected");
                    return true;
                }

                if (IsOpen && ReadECRegister(RegisterMap.FanControlAddress, RegisterMap, out _))
                {
                    Debug.WriteLine("EC communication successful - assuming compatible device");
                    return true;
                }

                Debug.WriteLine("Device not recognized as OneXPlayer X1");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking device support: {ex.Message}");
                return false;
            }
        }
    }

    public class OneXFlyF1Device : FanControlDeviceBase
    {
        public override string ManufacturerName => "OneXPlayer";
        public override string DeviceName => "F1 Series";


        public override ECRegisterMap RegisterMap { get; } = new ECRegisterMap
        {
            FanControlAddress = 0x44A,
            FanDutyAddress = 0x44B,
            StatusCommandPort = 0x4E,
            DataPort = 0x4F,
            FanValueMin = 11,  // Safety minimum - prevents fan shutdown
            FanValueMax = 255, // Extended range for finer control
            Protocol = new ECProtocolConfig
            {
                // OneXFly F1 uses same protocol as OneXPlayer X1
                AddressSelectHigh = 0x2E,
                AddressSetHigh = 0x11,
                AddressSelectLow = 0x2E,
                AddressSetLow = 0x10,
                DataSelect = 0x2E,
                DataCommand = 0x12,
                AddressPort = 0x2F,
                ReadDataSelect = 0x2F
            }
        };

        public override DeviceCapabilities Capabilities { get; } = new DeviceCapabilities
        {
            SupportedFeatures = new HashSet<FanControlCapability>
            {
                FanControlCapability.BasicSpeedControl
            },
            MinFanSpeed = 5, // ~4.3% (11/255) minimum safe speed, rounded up
            MaxFanSpeed = 100,
            SupportsAutoDetection = true,
            SupportedModels = new[] { "ONEXPLAYER F1", "ONEXPLAYER F1Pro", "OneXFly F1", "OneXFly F1 Pro" }
        };

        public override bool IsDeviceSupported()
        {
            try
            {
                string? manufacturer = GetSystemInfo("Manufacturer");
                string? model = GetSystemInfo("Model");
                string? version = GetSystemInfo("Version");

                DebugLogger.Log($"System Info - Manufacturer: {manufacturer}, Model: {model}, Version: {version}", "F1_DETECT");

                var supportedManufacturers = new[] { "ONE-NETBOOK", "ONEXPLAYER", "ONE NETBOOK" };
                var supportedModels = new[] { "F1", "ONEXPLAYER F1", "F1Pro", "ONEXPLAYER F1Pro", "OneXFly F1", "OneXFly F1 Pro" };

                bool manufacturerMatch = supportedManufacturers.Any(m =>
                    manufacturer?.Contains(m, StringComparison.OrdinalIgnoreCase) == true);

                bool modelMatch = supportedModels.Any(m =>
                    model?.Contains(m, StringComparison.OrdinalIgnoreCase) == true ||
                    version?.Contains(m, StringComparison.OrdinalIgnoreCase) == true);

                if (manufacturerMatch && modelMatch)
                {
                    DebugLogger.Log("OneXFly F1 device detected by manufacturer + model", "F1_DETECT");
                    return true;
                }

                if (IsOpen && ReadECRegister(RegisterMap.FanControlAddress, RegisterMap, out _))
                {
                    DebugLogger.Log("EC communication successful - testing OneXFly F1 compatibility", "F1_DETECT");
                    return true;
                }

                DebugLogger.Log("Device not recognized as OneXFly F1", "F1_DETECT");
                return false;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"Error checking device support: {ex.Message}", "F1_ERROR");
                return false;
            }
        }

        protected override double ApplySafetyConstraints(double percent)
        {
            // OneXFly F1 has a safety minimum of 4.3% (11/255) to prevent fan shutdown
            double safePercent = Math.Max(percent, Capabilities.MinFanSpeed);
            return Math.Clamp(safePercent, Capabilities.MinFanSpeed, 100.0);
        }
    }
}