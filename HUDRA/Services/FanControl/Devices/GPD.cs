using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

namespace HUDRA.Services.FanControl.Devices
{
    public class GPDDevice : ECCommunicationBase, IFanControlDevice
    {
        public string ManufacturerName => "GPD";
        public string DeviceName => "Win 4 Series";
        public bool IsInitialized { get; private set; }

        public ECRegisterMap RegisterMap { get; } = new ECRegisterMap
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

        public DeviceCapabilities Capabilities { get; } = new DeviceCapabilities
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

        private FanControlMode _currentMode = FanControlMode.Hardware;

        public bool Initialize()
        {
            try
            {

                if (!InitializeEC())
                {
                    return false;
                }

                if (!IsDeviceSupported())
                {
                    return false;
                }

                IsInitialized = true;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool IsDeviceSupported()
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

        public bool SetFanControl(FanControlMode mode)
        {
            if (!IsInitialized || !IsOpen)
                return false;

            try
            {
                byte controlValue = mode switch
                {
                    FanControlMode.Software => 1,
                    FanControlMode.Hardware => 0,
                    _ => 0
                };

                bool success = WriteECRegister(RegisterMap.FanControlAddress, RegisterMap, controlValue);

                if (success)
                {
                    _currentMode = mode;
                }

                return success;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool SetFanDuty(double percent)
        {
            if (!IsInitialized || !IsOpen)
                return false;

            try
            {
                if (_currentMode != FanControlMode.Software)
                {
                    if (!SetFanControl(FanControlMode.Software))
                        return false;
                }

                percent = Math.Clamp(percent, 0.0, 100.0);
                byte dutyValue = PercentageToDuty(percent, RegisterMap.FanValueMin, RegisterMap.FanValueMax);

                bool success = WriteECRegister(RegisterMap.FanDutyAddress, RegisterMap, dutyValue);

                return success;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public FanStatus GetFanStatus()
        {
            var status = new FanStatus();

            if (!IsInitialized || !IsOpen)
                return status;

            try
            {
                if (ReadECRegister(RegisterMap.FanControlAddress, RegisterMap, out byte controlValue))
                {
                    status.IsControlEnabled = controlValue != 0;
                }

                if (ReadECRegister(RegisterMap.FanDutyAddress, RegisterMap, out byte dutyValue))
                {
                    status.CurrentDutyPercent = DutyToPercentage(dutyValue, RegisterMap.FanValueMin, RegisterMap.FanValueMax);
                }

                status.LastUpdated = DateTime.Now;
                return status;
            }
            catch (Exception)
            {
                return status;
            }
        }

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

        private static string? GetProcessorInfo()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
                using var collection = searcher.Get();
                var result = collection.Cast<ManagementObject>().FirstOrDefault();
                return result?["Name"]?.ToString();
            }
            catch
            {
                return null;
            }
        }
    }
}