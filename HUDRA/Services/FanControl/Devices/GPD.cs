using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

namespace HUDRA.Services.FanControl.Devices
{
    public class GPDDevice : FanControlDeviceBase
    {
        public override string ManufacturerName => "GPD";
        public override string DeviceName => "Win 4 Series";

        public override ECRegisterMap RegisterMap { get; } = new ECRegisterMap
        {
            FanControlAddress = 0x275,
            FanDutyAddress = 0x1809,
            StatusCommandPort = 0x4E,
            DataPort = 0x4F,
            FanValueMin = 0,
            FanValueMax = 184,
            Protocol = new ECProtocolConfig
            {
                // Identical to OneXPlayer protocol
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
            SupportedModels = new[] { "G1618-04", "GPD WIN 4", "WIN 4" }
        };

        public override bool IsDeviceSupported()
        {
            try
            {
                string? manufacturer = GetSystemInfo("Manufacturer");
                string? model = GetSystemInfo("Model");
                string? systemFamily = GetSystemInfo("SystemFamily");
                string? version = GetSystemInfo("Version");

                // Check for GPD manufacturer
                bool manufacturerMatch = manufacturer?.Contains("GPD", StringComparison.OrdinalIgnoreCase) == true;

                // Check for GPD Win 4 model identifiers
                var supportedModels = new[] { "G1618-04", "GPD WIN 4", "WIN 4" };
                bool modelMatch = supportedModels.Any(m =>
                    model?.Contains(m, StringComparison.OrdinalIgnoreCase) == true ||
                    version?.Contains(m, StringComparison.OrdinalIgnoreCase) == true ||
                    systemFamily?.Contains(m, StringComparison.OrdinalIgnoreCase) == true);

                // Check for supported APUs (7840U, 8640U, 8840U, HX370)
                bool apuMatch = CheckSupportedAPU();

                if (manufacturerMatch && (modelMatch || apuMatch))
                {
                    return true;
                }

                // Fallback: Test EC communication
                if (IsOpen && ReadECRegister(RegisterMap.FanControlAddress, RegisterMap, out _))
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

                var supportedAPUs = new[] { "7840U", "8640U", "8840U", "HX 370" };
                bool apuSupported = supportedAPUs.Any(apu =>
                    processor.Contains(apu, StringComparison.OrdinalIgnoreCase));

                return apuSupported;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}