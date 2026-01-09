using HUDRA.Controls;
using HUDRA.Models;
using HUDRA.Services.FanControl;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HUDRA.Services
{
    public class FanControlService : IDisposable
    {
        private readonly DispatcherQueue _dispatcher;
        private IFanControlDevice? _device;
        private Timer? _statusTimer;
        private bool _disposed = false;
        private bool _isInitialized = false;
        private TemperatureMonitorService? _temperatureMonitor;
        private bool _temperatureControlEnabled = false;

        public event EventHandler<FanStatusChangedEventArgs>? FanStatusChanged;
        public event EventHandler<string>? DeviceStatusChanged;

        public bool IsDeviceAvailable => _device?.IsInitialized == true;
        public string DeviceInfo => _device != null ? $"{_device.ManufacturerName} {_device.DeviceName}" : "No device";
        public IFanControlDevice? DetectedDevice => _device;
        public FanControlMode CurrentMode { get; private set; } = FanControlMode.Hardware;
        public double CurrentFanSpeed { get; private set; } = 0.0;

        public FanControlService(DispatcherQueue dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public async Task<FanControlResult> InitializeAsync()
        {
            if (_isInitialized)
                return FanControlResult.SuccessResult("Already initialized");

            // Quick check if fan control is even supported on this device
            var detectedDevice = HardwareDetectionService.GetDetectedDevice();
            if (!detectedDevice.SupportsFanControl)
            {
                var message = $"Device ({detectedDevice.Manufacturer} {detectedDevice.DeviceName}) doesn't support fan control";
                Debug.WriteLine($"FanControlService: {message}");
                DeviceStatusChanged?.Invoke(this, message);
                return FanControlResult.FailureResult(message);
            }

            try
            {
                DeviceStatusChanged?.Invoke(this, "Detecting fan control device...");

                // Try to detect and initialize a supported device
                _device = DeviceDetectionService.DetectDevice();

                if (_device == null)
                {
                    var message = "No supported fan control device detected. Fan control will be unavailable.";
                    DeviceStatusChanged?.Invoke(this, message);
                    return FanControlResult.FailureResult(message);
                }

                // Start status monitoring
                StartStatusMonitoring();

                _isInitialized = true;
                var successMessage = $"Fan control initialized: {DeviceInfo}";
                DeviceStatusChanged?.Invoke(this, successMessage);

                Debug.WriteLine(successMessage);
                return FanControlResult.SuccessResult(successMessage);
            }
            catch (Exception ex)
            {
                var errorMessage = $"Failed to initialize fan control: {ex.Message}";
                DeviceStatusChanged?.Invoke(this, errorMessage);
                Debug.WriteLine($"Fan control initialization error: {ex}");
                return FanControlResult.FailureResult(errorMessage, ex);
            }
        }

        public FanControlResult SetFanMode(FanControlMode mode)
        {
            if (!IsDeviceAvailable)
                return FanControlResult.FailureResult("No fan control device available");

            try
            {
                bool success = _device!.SetFanControl(mode);
                if (success)
                {
                    CurrentMode = mode;
                    var message = $"Fan mode set to: {mode}";
                    Debug.WriteLine(message);
                    return FanControlResult.SuccessResult(message);
                }
                else
                {
                    return FanControlResult.FailureResult($"Failed to set fan mode to {mode}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting fan mode: {ex.Message}");
                return FanControlResult.FailureResult($"Error setting fan mode: {ex.Message}", ex);
            }
        }

        public FanControlResult SetFanSpeed(double percentage)
        {
            if (!IsDeviceAvailable)
                return FanControlResult.FailureResult("No fan control device available");

            try
            {
                percentage = Math.Clamp(percentage, 0.0, 100.0);

                // Automatically switch to software mode if needed
                if (CurrentMode != FanControlMode.Software)
                {
                    var modeResult = SetFanMode(FanControlMode.Software);
                    if (!modeResult.Success)
                        return modeResult;
                }

                bool success = _device!.SetFanDuty(percentage);
                if (success)
                {
                    CurrentFanSpeed = percentage;
                    var message = $"Fan speed set to: {percentage:F1}%";
                    Debug.WriteLine(message);
                    return FanControlResult.SuccessResult(message);
                }
                else
                {
                    return FanControlResult.FailureResult($"Failed to set fan speed to {percentage:F1}%");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting fan speed: {ex.Message}");
                return FanControlResult.FailureResult($"Error setting fan speed: {ex.Message}", ex);
            }
        }

        public FanControlResult SetAutoMode()
        {
            return SetFanMode(FanControlMode.Hardware);
        }

        public FanStatus? GetCurrentStatus()
        {
            if (!IsDeviceAvailable)
                return null;

            try
            {
                return _device!.GetFanStatus();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting fan status: {ex.Message}");
                return null;
            }
        }

        private void StartStatusMonitoring()
        {
            // Update fan status every 2 seconds
            _statusTimer = new Timer(UpdateFanStatus, null,
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        }

        private void UpdateFanStatus(object? state)
        {
            if (!IsDeviceAvailable || _disposed)
                return;

            try
            {
                var status = _device!.GetFanStatus();

                _dispatcher.TryEnqueue(() =>
                {
                    FanStatusChanged?.Invoke(this, new FanStatusChangedEventArgs(status));
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating fan status: {ex.Message}");
            }
        }

        public async Task<(bool Success, string Message)> ReinitializeAfterResumeAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("⚡ Reinitializing FanControlService after hibernation resume...");

                // Stop status monitoring temporarily
                _statusTimer?.Dispose();
                _statusTimer = null;

                // Dispose the existing device
                _device?.Dispose();
                _device = null;

                // Reset state
                _isInitialized = false;
                CurrentMode = FanControlMode.Hardware;
                CurrentFanSpeed = 0.0;

                // Re-detect and initialize device
                var initResult = await InitializeAsync();
                
                if (initResult.Success)
                {
                    // Re-enable temperature control if it was previously enabled
                    if (_temperatureControlEnabled && _temperatureMonitor != null)
                    {
                        EnableTemperatureControl(_temperatureMonitor);
                        System.Diagnostics.Debug.WriteLine("🌡️ Temperature-based fan control re-enabled after hibernation resume");
                    }

                    var successMessage = "FanControlService successfully reinitialized after hibernation resume";
                    System.Diagnostics.Debug.WriteLine($"⚡ {successMessage}");
                    return (true, successMessage);
                }
                else
                {
                    var errorMessage = $"Failed to reinitialize FanControlService: {initResult.Message}";
                    System.Diagnostics.Debug.WriteLine($"⚠️ {errorMessage}");
                    return (false, errorMessage);
                }
            }
            catch (Exception ex)
            {
                var errorMessage = $"Exception during FanControlService reinitialization: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"⚠️ {errorMessage}");
                return (false, errorMessage);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    // If temperature control was enabled, reset fans to a safe state
                    if (_temperatureControlEnabled && _device?.IsInitialized == true)
                    {
                        try
                        {
                            DebugLogger.Log("Resetting fans to safe state before dispose", "FAN");

                            // Try to restore hardware control first
                            var hardwareResult = SetFanMode(FanControlMode.Hardware);
                            if (!hardwareResult.Success)
                            {
                                // If hardware mode fails, set to 50% as a safe fallback
                                DebugLogger.Log("Hardware mode failed, setting fans to 50%", "FAN");
                                _device!.SetFanDuty(50);
                            }
                            else
                            {
                                DebugLogger.Log("Restored fans to hardware control", "FAN");
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugLogger.Log($"Failed to reset fans on dispose: {ex.Message}", "FAN");
                            // Continue with disposal even if reset fails
                        }
                    }

                    DisableTemperatureControl();
                    _statusTimer?.Dispose();
                    _device?.Dispose();
                    _disposed = true;
                    Debug.WriteLine("Fan control service disposed");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error during FanControlService disposal: {ex.Message}");
                    _disposed = true; // Mark as disposed even on error
                }
            }
        }

        public void EnableTemperatureControl(TemperatureMonitorService temperatureMonitor)
        {
            try
            {
                _temperatureMonitor = temperatureMonitor;
                _temperatureMonitor.TemperatureChanged += OnTemperatureChanged;
                _temperatureControlEnabled = true;

                System.Diagnostics.Debug.WriteLine("🌡️ Temperature-based fan control enabled");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error enabling temperature control: {ex.Message}");
            }
        }

        /// <summary>
        /// Forces immediate application of the current fan curve based on current temperature.
        /// Call this after changing the fan curve settings to apply immediately without waiting
        /// for the next temperature change event.
        /// </summary>
        public void ApplyCurrentFanCurve()
        {
            if (!_temperatureControlEnabled || _temperatureMonitor == null)
            {
                System.Diagnostics.Debug.WriteLine("Cannot apply fan curve: temperature control not enabled");
                return;
            }

            try
            {
                var fanCurve = SettingsService.GetFanCurve();
                if (!fanCurve.IsEnabled)
                {
                    System.Diagnostics.Debug.WriteLine("Fan curve is disabled, skipping immediate application");
                    return;
                }

                // Get current temperature from monitor
                var currentTemp = _temperatureMonitor.CurrentTemperature.MaxTemperature;
                if (currentTemp <= 0)
                {
                    System.Diagnostics.Debug.WriteLine("Could not get current temperature for fan curve application");
                    return;
                }

                // Interpolate and apply fan speed
                var targetFanSpeed = InterpolateFanSpeedFromCurve(currentTemp, fanCurve.Points);
                SetFanSpeed(targetFanSpeed);

                System.Diagnostics.Debug.WriteLine($"🌡️ Immediate fan curve application: {currentTemp:F1}°C → {targetFanSpeed:F1}%");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error applying fan curve immediately: {ex.Message}");
            }
        }

        public void DisableTemperatureControl()
        {
            try
            {
                if (_temperatureMonitor != null)
                {
                    _temperatureMonitor.TemperatureChanged -= OnTemperatureChanged;
                }
                _temperatureControlEnabled = false;

                System.Diagnostics.Debug.WriteLine("🌡️ Temperature-based fan control disabled");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disabling temperature control: {ex.Message}");
            }
        }

        private void OnTemperatureChanged(object? sender, TemperatureChangedEventArgs e)
        {
            if (!_temperatureControlEnabled) return;

            try
            {
                // Get the current fan curve from settings
                var fanCurve = SettingsService.GetFanCurve();
                if (!fanCurve.IsEnabled) return;

                // Use the maximum temperature for fan control decision
                var currentTemp = e.TemperatureData.MaxTemperature;

                // Interpolate fan speed from curve
                var targetFanSpeed = InterpolateFanSpeedFromCurve(currentTemp, fanCurve.Points);

                // Apply the calculated fan speed
                SetFanSpeed(targetFanSpeed);

                System.Diagnostics.Debug.WriteLine($"Temperature: {currentTemp:F1}°C → Fan Speed: {targetFanSpeed:F1}%");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error applying temperature-based fan control: {ex.Message}");
            }
        }

        private double InterpolateFanSpeedFromCurve(double temperature, FanCurvePoint[] points)
        {
            // Sort points by temperature (should already be sorted, but safety first)
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

                    var interpolatedSpeed = point1.FanSpeed + (speedRange * (tempOffset / tempRange));
                    return Math.Clamp(interpolatedSpeed, 0, 100);
                }
            }

            // Fallback (should never reach here)
            return 50.0;
        }
    }

    public class FanStatusChangedEventArgs : EventArgs
    {
        public FanStatus Status { get; }

        public FanStatusChangedEventArgs(FanStatus status)
        {
            Status = status;
        }
    }

}