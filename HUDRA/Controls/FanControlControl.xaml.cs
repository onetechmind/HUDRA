using HUDRA.Services;
using HUDRA.Services.FanControl;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;
using System.Threading.Tasks;

namespace HUDRA.Controls
{
    public sealed partial class FanControlControl : UserControl
    {
        public event EventHandler<FanControlChangedEventArgs>? FanControlChanged;

        private FanControlService? _fanControlService;
        private bool _isUpdatingControls = false;
        private bool _isInitialized = false;

        public FanControlControl()
        {
            this.InitializeComponent();
        }

        public async void Initialize()
        {
            if (_isInitialized) return;

            try
            {
                _fanControlService = new FanControlService(DispatcherQueue);

                // Subscribe to events
                _fanControlService.FanStatusChanged += OnFanStatusChanged;
                _fanControlService.DeviceStatusChanged += OnDeviceStatusChanged;

                // Show device status during initialization
                DeviceStatusText.Visibility = Visibility.Visible;
                UpdateDeviceStatus("Initializing fan control...");

                // Initialize the service
                var result = await _fanControlService.InitializeAsync();

                if (result.Success)
                {
                    UpdateFanStatus("Fan: Auto Mode");
                    UpdateDeviceStatus($"Device: {_fanControlService.DeviceInfo}");

                    // Hide device status after a delay if successful
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                    timer.Tick += (s, e) =>
                    {
                        timer.Stop();
                        DeviceStatusText.Visibility = Visibility.Collapsed;
                    };
                    timer.Start();
                }
                else
                {
                    UpdateFanStatus("Fan: Unavailable");
                    UpdateDeviceStatus(result.Message);

                    // Disable controls if initialization failed
                    ManualModeToggle.IsEnabled = false;
                    FanSpeedSlider.IsEnabled = false;
                }

                _isInitialized = true;
                FanControlChanged?.Invoke(this, new FanControlChangedEventArgs(
                    result.Success ? FanControlMode.Hardware : FanControlMode.Hardware,
                    0,
                    result.Message
                ));
            }
            catch (Exception ex)
            {
                UpdateFanStatus("Fan: Error");
                UpdateDeviceStatus($"Error: {ex.Message}");
                DeviceStatusText.Visibility = Visibility.Visible;
            }
        }

        private void UpdateFanStatus(string status)
        {
            FanStatusText.Text = status;
        }

        private void UpdateDeviceStatus(string status)
        {
            DeviceStatusText.Text = status;
        }

        private void ManualModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_fanControlService == null || _isUpdatingControls) return;

            try
            {
                bool isManualMode = ManualModeToggle.IsOn;
                var mode = isManualMode ? FanControlMode.Software : FanControlMode.Hardware;

                var result = _fanControlService.SetFanMode(mode);

                if (result.Success)
                {
                    // Show/hide fan speed slider based on mode
                    FanSpeedPanel.Visibility = isManualMode ? Visibility.Visible : Visibility.Collapsed;

                    if (isManualMode)
                    {
                        // Set initial fan speed when switching to manual mode
                        var speedResult = _fanControlService.SetFanSpeed(FanSpeedSlider.Value);
                        UpdateFanStatus($"Fan: {FanSpeedSlider.Value:F0}% (Manual)");
                    }
                    else
                    {
                        UpdateFanStatus("Fan: Auto Mode");
                    }

                    FanControlChanged?.Invoke(this, new FanControlChangedEventArgs(
                        mode,
                        isManualMode ? FanSpeedSlider.Value : 0,
                        result.Message
                    ));
                }
                else
                {
                    // Revert toggle if operation failed
                    _isUpdatingControls = true;
                    ManualModeToggle.IsOn = !isManualMode;
                    _isUpdatingControls = false;

                    UpdateFanStatus($"Fan Error: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                UpdateFanStatus($"Fan Error: {ex.Message}");
            }
        }

        private void FanSpeedSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_fanControlService == null || _isUpdatingControls || !ManualModeToggle.IsOn) return;

            try
            {
                double speed = e.NewValue;
                var result = _fanControlService.SetFanSpeed(speed);

                if (result.Success)
                {
                    UpdateFanStatus($"Fan: {speed:F0}% (Manual)");

                    FanControlChanged?.Invoke(this, new FanControlChangedEventArgs(
                        FanControlMode.Software,
                        speed,
                        result.Message
                    ));
                }
                else
                {
                    UpdateFanStatus($"Fan Error: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                UpdateFanStatus($"Fan Error: {ex.Message}");
            }
        }

        private void OnFanStatusChanged(object? sender, FanStatusChangedEventArgs e)
        {
            // Update UI with current fan status (useful for monitoring)
            if (!_isUpdatingControls)
            {
                _isUpdatingControls = true;

                // Update display without triggering events
                if (ManualModeToggle.IsOn)
                {
                    UpdateFanStatus($"Fan: {e.Status.CurrentDutyPercent:F0}% (Manual)");
                    FanSpeedSlider.Value = e.Status.CurrentDutyPercent;
                }
                else
                {
                    UpdateFanStatus($"Fan: {e.Status.CurrentDutyPercent:F0}% (Auto)");
                }

                _isUpdatingControls = false;
            }
        }

        private void OnDeviceStatusChanged(object? sender, string message)
        {
            UpdateDeviceStatus(message);
        }

        public void Dispose()
        {
            if (_fanControlService != null)
            {
                _fanControlService.FanStatusChanged -= OnFanStatusChanged;
                _fanControlService.DeviceStatusChanged -= OnDeviceStatusChanged;
                _fanControlService.Dispose();
            }
        }
    }

    // Event argument class for fan control changes
    public class FanControlChangedEventArgs : EventArgs
    {
        public FanControlMode Mode { get; }
        public double Speed { get; }
        public string StatusMessage { get; }

        public FanControlChangedEventArgs(FanControlMode mode, double speed, string statusMessage)
        {
            Mode = mode;
            Speed = speed;
            StatusMessage = statusMessage;
        }
    }
}