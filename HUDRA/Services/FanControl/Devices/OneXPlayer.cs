using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;

namespace HUDRA.Services.FanControl.Devices
{
    public class OneXPlayerX1Device : ECCommunicationBase, IFanControlDevice
    {
        public string ManufacturerName => "OneXPlayer";
        public string DeviceName => "X1 Series";
        public bool IsInitialized { get; private set; }

        public ECRegisterMap RegisterMap { get; } = new ECRegisterMap
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

        public DeviceCapabilities Capabilities { get; } = new DeviceCapabilities
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

        private FanControlMode _currentMode = FanControlMode.Hardware;

        public bool Initialize()
        {
            try
            {
                Debug.WriteLine("Initializing OneXPlayer X1 fan control...");

                if (!InitializeEC())
                {
                    Debug.WriteLine("Failed to initialize EC communication");
                    return false;
                }

                if (!IsDeviceSupported())
                {
                    Debug.WriteLine("Device not supported or not a OneXPlayer X1");
                    return false;
                }

                IsInitialized = true;
                Debug.WriteLine("OneXPlayer X1 fan control initialized successfully");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initialize OneXPlayer X1 fan control: {ex.Message}");
                return false;
            }
        }

        public bool IsDeviceSupported()
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

                if (manufacturerMatch || modelMatch)
                {
                    Debug.WriteLine("OneXPlayer device detected");
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
                    Debug.WriteLine($"Fan control mode set to: {mode}");
                }

                return success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting fan control mode: {ex.Message}");
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

                if (success)
                {
                    Debug.WriteLine($"Fan duty set to: {percent:F1}% (raw value: {dutyValue})");
                }

                return success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting fan duty: {ex.Message}");
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
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading fan status: {ex.Message}");
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
    }
}