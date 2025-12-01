using HUDRA.Configuration;
using HUDRA.Services;
using HUDRA.Interfaces;
using HUDRA.AttachedProperties;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HUDRA.Controls
{
    public sealed partial class BrightnessControlControl : UserControl, INotifyPropertyChanged, IGamepadNavigable
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<BrightnessChangedEventArgs>? BrightnessChanged;

        private BrightnessService? _brightnessService;
        private bool _isUpdatingSlider = false;
        private GamepadNavigationService? _gamepadNavigationService;
        private bool _isFocused = false;
        private bool _isSliderActivated = false;

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

        // IGamepadNavigable implementation
        public bool CanNavigateUp => false;
        public bool CanNavigateDown => false;
        public bool CanNavigateLeft => false;
        public bool CanNavigateRight => false;
        public bool CanActivate => true; // Enable activation for slider control
        public FrameworkElement NavigationElement => this;
        
        // Slider-specific interface implementations
        public bool IsSlider => true; // BrightnessControl is always a slider
        public bool IsSliderActivated 
        { 
            get => _isSliderActivated; 
            set 
            { 
                _isSliderActivated = value;
                OnPropertyChanged(nameof(FocusBorderBrush));
            } 
        }
        
        // ComboBox interface implementations - BrightnessControl has no ComboBoxes
        public bool HasComboBoxes => false;
        public bool IsComboBoxOpen { get; set; } = false;
        public ComboBox? GetFocusedComboBox() => null;
        public int ComboBoxOriginalIndex { get; set; } = -1;
        public bool IsNavigatingComboBox { get; set; } = false;
        public void ProcessCurrentSelection() { /* Not applicable - no ComboBoxes */ }

        public Brush FocusBorderBrush
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true)
                {
                    // Different color when slider is activated for value adjustment
                    return new SolidColorBrush(_isSliderActivated ? Microsoft.UI.Colors.DodgerBlue : Microsoft.UI.Colors.DarkViolet);
                }
                return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        public Thickness FocusBorderThickness
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true)
                {
                    return new Thickness(2);
                }
                return new Thickness(0);
            }
        }


        public BrightnessControlControl()
        {
            this.InitializeComponent();
        }

        public void Initialize()
        {
            _brightnessService = new BrightnessService();

            // Get gamepad service
            if (Application.Current is App app && app.MainWindow is MainWindow mainWindow)
            {
                _gamepadNavigationService = mainWindow.GamepadNavigationService;
            }

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


        public void Dispose()
        {
            // No auto-set managers or other resources to dispose for brightness control
        }

        // IGamepadNavigable event handlers
        public void OnGamepadNavigateUp() { }
        public void OnGamepadNavigateDown() { }
        public void OnGamepadNavigateLeft() { }
        public void OnGamepadNavigateRight() { }
        
        public void OnGamepadActivate()
        {
            // Slider handles its own gamepad interaction
        }

        public void OnGamepadBack() { }

        public void OnGamepadFocusReceived()
        {
            _isFocused = true;
            UpdateFocusVisuals();
            
            // Give focus to the slider for gamepad control
            if (BrightnessSlider != null)
            {
                BrightnessSlider.Focus(FocusState.Programmatic);
            }
            
            System.Diagnostics.Debug.WriteLine($"🎮 Brightness: Received gamepad focus");
        }

        public void OnGamepadFocusLost()
        {
            _isFocused = false;
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 Brightness: Lost gamepad focus");
        }

        public void FocusLastElement()
        {
            // Not used - BrightnessControlControl is not in a NavigableExpander
        }

        private void UpdateFocusVisuals()
        {
            // Dispatch on UI thread to ensure bindings update reliably with gamepad navigation
            DispatcherQueue.TryEnqueue(() =>
            {
                OnPropertyChanged(nameof(FocusBorderBrush));
                OnPropertyChanged(nameof(FocusBorderThickness));
            });
        }

        public void AdjustSliderValue(int direction)
        {
            if (BrightnessSlider == null) return;
            
            const double increment = 5.0; // 5% increment
            double currentValue = BrightnessSlider.Value;
            double newValue = Math.Clamp(currentValue + (direction * increment), 0, 100);
            
            BrightnessSlider.Value = newValue;
            System.Diagnostics.Debug.WriteLine($"🎮 Brightness: Adjusted brightness to {newValue}% (direction: {direction})");
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
    }}