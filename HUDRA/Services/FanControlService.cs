using HUDRA.Services.FanControl;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
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

        public event EventHandler<FanStatusChangedEventArgs>? FanStatusChanged;
        public event EventHandler<string>? DeviceStatusChanged;

        public bool IsDeviceAvailable => _device?.IsInitialized == true;
        public string DeviceInfo => _device != null ? $"{_device.ManufacturerName} {_device.DeviceName}" : "No device";
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

        public void Dispose()
        {
            if (!_disposed)
            {
                _statusTimer?.Dispose();
                _device?.Dispose();
                _disposed = true;
                Debug.WriteLine("Fan control service disposed");
            }
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