using HUDRA.Configuration;
using HUDRA.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HUDRA.Controls
{
    public sealed partial class BrightnessControlControl : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<BrightnessChangedEventArgs>? BrightnessChanged;

        private BrightnessService? _brightnessService;
        private bool _isUpdatingSlider = false;

        private string _brightnessStatusText = "Brightness: Not Set";
        public string BrightnessStatusText
        {
            get => _brightnessStatusText;
            set
            {
                if (_brightnessStatusText != value)
                {
                    _brightnessStatusText = value;
                    OnPropertyChanged();
                }
            }
        }

        // Property for gamepad navigation
        public Slider BrightnessSliderControl => BrightnessSlider;

        public BrightnessControlControl()
        {
            this.InitializeComponent();
        }

        public void Initialize()
        {
            _brightnessService = new BrightnessService();

            SetupEventHandlers();
            LoadCurrentBrightness();
        }

        private void SetupEventHandlers()
        {
            if (BrightnessSlider != null)
            {
                BrightnessSlider.ValueChanged += OnBrightnessSliderValueChanged;
            }
        }

        private void OnBrightnessSliderValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_brightnessService == null || _isUpdatingSlider) return;

            try
            {
                int brightnessLevel = (int)e.NewValue;
                _brightnessService.SetBrightness(brightnessLevel);

                BrightnessStatusText = $"Brightness: {brightnessLevel}%";

                BrightnessChanged?.Invoke(this, new BrightnessChangedEventArgs(
                    brightnessLevel,
                    $"Brightness: {brightnessLevel}%"
                ));
            }
            catch (Exception ex)
            {
                BrightnessStatusText = $"Brightness Error: {ex.Message}";
            }
        }

        private void LoadCurrentBrightness()
        {
            if (_brightnessService == null) return;

            try
            {
                int currentBrightness = _brightnessService.GetBrightness();

                _isUpdatingSlider = true;
                if (BrightnessSlider != null)
                {
                    BrightnessSlider.Value = currentBrightness;
                }
                _isUpdatingSlider = false;

                BrightnessStatusText = $"Brightness: {currentBrightness}%";

                // Fire initial state event
                BrightnessChanged?.Invoke(this, new BrightnessChangedEventArgs(
                    currentBrightness,
                    BrightnessStatusText
                ));
            }
            catch (Exception ex)
            {
                BrightnessStatusText = $"Brightness Error: {ex.Message}";
            }
        }

        // Public method for external control (gamepad)
        public void ChangeBrightnessBy(double delta)
        {
            if (BrightnessSlider == null) return;

            var newValue = Math.Max(0, Math.Min(100, BrightnessSlider.Value + delta));
            BrightnessSlider.Value = newValue;
        }

        public void Dispose()
        {
            // No auto-set managers or other resources to dispose for brightness control
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // Event argument class for brightness changes
    public class BrightnessChangedEventArgs : EventArgs
    {
        public int BrightnessLevel { get; }
        public string StatusMessage { get; }

        public BrightnessChangedEventArgs(int brightnessLevel, string statusMessage)
        {
            BrightnessLevel = brightnessLevel;
            StatusMessage = statusMessage;
        }
    }
}