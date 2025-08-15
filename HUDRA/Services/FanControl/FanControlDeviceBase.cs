using System;
using System.Diagnostics;
using System.Linq;
using System.Management;

namespace HUDRA.Services.FanControl
{
    public abstract class FanControlDeviceBase : ECCommunicationBase, IFanControlDevice
    {
        public abstract string ManufacturerName { get; }
        public abstract string DeviceName { get; }
        public abstract ECRegisterMap RegisterMap { get; }
        public abstract DeviceCapabilities Capabilities { get; }
        
        public virtual uint? TurboButtonECAddress => null;
        
        public bool IsInitialized { get; protected set; }

        private FanControlMode _currentMode = FanControlMode.Hardware;

        public virtual bool Initialize()
        {
            try
            {
                Debug.WriteLine($"Initializing {ManufacturerName} {DeviceName} fan control...");

                if (!InitializeEC())
                {
                    Debug.WriteLine("Failed to initialize EC communication");
                    return false;
                }

                if (!IsDeviceSupported())
                {
                    Debug.WriteLine($"Device not supported or not a {ManufacturerName} {DeviceName}");
                    return false;
                }

                IsInitialized = true;
                Debug.WriteLine($"{ManufacturerName} {DeviceName} fan control initialized successfully");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initialize {ManufacturerName} {DeviceName} fan control: {ex.Message}");
                return false;
            }
        }

        public abstract bool IsDeviceSupported();

        public virtual bool SetFanControl(FanControlMode mode)
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

        public virtual bool SetFanDuty(double percent)
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

                // Apply device-specific safety constraints
                double safePercent = ApplySafetyConstraints(percent);
                
                byte dutyValue = PercentageToDuty(safePercent, RegisterMap.FanValueMin, RegisterMap.FanValueMax);

                bool success = WriteECRegister(RegisterMap.FanDutyAddress, RegisterMap, dutyValue);

                if (success)
                {
                    Debug.WriteLine($"Fan duty set to: {safePercent:F1}% (raw value: {dutyValue})");
                }

                return success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting fan duty: {ex.Message}");
                return false;
            }
        }

        public virtual FanStatus GetFanStatus()
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

        /// <summary>
        /// Apply device-specific safety constraints to fan percentage.
        /// Override this method to implement custom safety minimums.
        /// </summary>
        protected virtual double ApplySafetyConstraints(double percent)
        {
            // Default: just clamp to 0-100%
            return Math.Clamp(percent, 0.0, 100.0);
        }

        /// <summary>
        /// Utility method for getting system information via WMI
        /// </summary>
        protected static string? GetSystemInfo(string property)
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

        /// <summary>
        /// Utility method for getting processor information via WMI
        /// </summary>
        protected static string? GetProcessorInfo()
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