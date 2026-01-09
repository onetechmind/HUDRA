using System;
using System.Collections.Generic;
using System.Linq;

namespace HUDRA.Services.FanControl.Devices
{
    /// <summary>
    /// Fan control implementation for GPD Win Mini devices.
    /// Supports models with AMD 7640U, 7840U, 8840U, and HX370 APUs.
    /// </summary>
    public class GPDWinMiniDevice : FanControlDeviceBase
    {
        public override string ManufacturerName => "GPD";
        public override string DeviceName => "Win Mini";

        public override ECRegisterMap RegisterMap { get; } = new ECRegisterMap
        {
            FanControlAddress = 0x47A,
            FanDutyAddress = 0x47A,
            StatusCommandPort = 0x4E,
            DataPort = 0x4F,
            FanValueMin = 0,
            FanValueMax = 244,
            Protocol = new ECProtocolConfig
            {
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
            SupportedModels = new[] { "G1617", "GPD WIN MINI", "WIN MINI" }
        };

        private static readonly string[] SupportedAPUs = { "7640U", "7840U", "8840U", "HX 370" };

        private static readonly string[] WinMiniModels = { "G1617", "GPD WIN MINI", "WIN MINI" };

        public override bool IsDeviceSupported()
        {
            try
            {
                string? manufacturer = GetSystemInfo("Manufacturer");
                string? model = GetSystemInfo("Model");
                string? systemFamily = GetSystemInfo("SystemFamily");
                string? version = GetSystemInfo("Version");

                // Must be GPD manufacturer
                bool manufacturerMatch = manufacturer?.Contains("GPD", StringComparison.OrdinalIgnoreCase) == true;

                if (!manufacturerMatch)
                    return false;

                // Check for Win Mini model identifiers
                bool modelMatch = WinMiniModels.Any(m =>
                    model?.Contains(m, StringComparison.OrdinalIgnoreCase) == true ||
                    version?.Contains(m, StringComparison.OrdinalIgnoreCase) == true ||
                    systemFamily?.Contains(m, StringComparison.OrdinalIgnoreCase) == true);

                if (modelMatch)
                    return true;

                // Check for supported APU if model doesn't explicitly match
                // This helps detect Win Mini by APU when model string is generic
                bool apuMatch = CheckSupportedAPU();

                // If APU matches but model is generic, try EC communication test
                if (apuMatch && IsOpen && ReadECRegister(RegisterMap.FanControlAddress, RegisterMap, out _))
                {
                    return true;
                }

                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool CheckSupportedAPU()
        {
            try
            {
                string? processor = GetProcessorInfo();
                if (string.IsNullOrEmpty(processor))
                    return false;

                return SupportedAPUs.Any(apu =>
                    processor.Contains(apu, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
