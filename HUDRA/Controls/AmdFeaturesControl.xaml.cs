using HUDRA.Interfaces;
using HUDRA.AttachedProperties;
using HUDRA.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace HUDRA.Controls
{
    public sealed partial class AmdFeaturesControl : UserControl, INotifyPropertyChanged, IGamepadNavigable, IDisposable
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private GamepadNavigationService? _gamepadNavigationService;
        private AmdAdlxService? _amdAdlxService;
        private int _currentFocusedElement = 0; // 0=RSR Toggle, 1=Sharpness Slider
        private bool _isFocused = false;
        private bool _isInitialized = false;
        private bool _isApplyingSettings = false;

        // IGamepadNavigable implementation
        public bool CanNavigateUp => _currentFocusedElement > 0; // Can navigate up from slider to toggle
        public bool CanNavigateDown => _currentFocusedElement < 1; // Can navigate down from toggle to slider
        public bool CanNavigateLeft => _isSliderActivated; // Can adjust slider when activated
        public bool CanNavigateRight => _isSliderActivated; // Can adjust slider when activated
        public bool CanActivate => true;
        public FrameworkElement NavigationElement => this;

        // Slider interface implementations
        private bool _isSliderActivated = false;
        public bool IsSlider => _currentFocusedElement == 1; // Sharpness slider
        public bool IsSliderActivated
        {
            get => _isSliderActivated;
            set
            {
                _isSliderActivated = value;
                OnPropertyChanged();
                UpdateFocusVisuals();
            }
        }
        public void AdjustSliderValue(int direction)
        {
            if (!_isSliderActivated || _currentFocusedElement != 1) return;

            if (SharpnessSlider != null)
            {
                double increment = 5.0; // 5% increments
                double newValue = SharpnessSlider.Value + (direction * increment);
                newValue = Math.Clamp(newValue, SharpnessSlider.Minimum, SharpnessSlider.Maximum);
                SharpnessSlider.Value = newValue;
                System.Diagnostics.Debug.WriteLine($"🎮 AmdFeatures: Adjusted sharpness to {newValue}");
            }
        }

        // ComboBox interface implementations - AmdFeatures has no ComboBoxes
        public bool HasComboBoxes => false;
        public bool IsComboBoxOpen { get; set; } = false;
        public ComboBox? GetFocusedComboBox() => null;
        public int ComboBoxOriginalIndex { get; set; } = -1;
        public bool IsNavigatingComboBox { get; set; } = false;
        public void ProcessCurrentSelection() { /* Not applicable - no ComboBoxes */ }

        // Focus brush properties for XAML binding
        public Brush RsrToggleFocusBrush
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true && _currentFocusedElement == 0)
                {
                    return new SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
                }
                return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        public Brush SharpnessSliderFocusBrush
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true && _currentFocusedElement == 1)
                {
                    return new SolidColorBrush(_isSliderActivated ? Microsoft.UI.Colors.DodgerBlue : Microsoft.UI.Colors.DarkViolet);
                }
                return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        // Data binding properties
        private bool _rsrEnabled = false;
        public bool RsrEnabled
        {
            get => _rsrEnabled;
            set
            {
                if (_rsrEnabled != value && !_isApplyingSettings)
                {
                    _rsrEnabled = value;
                    OnPropertyChanged();

                    // Apply RSR settings asynchronously (includes current sharpness)
                    _ = ApplyRsrSettingsAsync(value, _rsrSharpness);
                }
            }
        }

        private int _rsrSharpness = 80;
        public int RsrSharpness
        {
            get => _rsrSharpness;
            set
            {
                if (_rsrSharpness != value && !_isApplyingSettings)
                {
                    _rsrSharpness = value;
                    OnPropertyChanged();

                    // Apply sharpness change if RSR is enabled
                    if (_rsrEnabled)
                    {
                        _ = ApplyRsrSharpnessAsync(value);
                    }
                }
            }
        }

        public AmdFeaturesControl()
        {
            this.InitializeComponent();
            this.DataContext = this;
            this.Loaded += AmdFeaturesControl_Loaded;
            this.Unloaded += AmdFeaturesControl_Unloaded;
            InitializeGamepadNavigation();
        }

        private async void AmdFeaturesControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized)
            {
                await InitializeAmdServiceAsync();
                _isInitialized = true;
            }
        }

        private void AmdFeaturesControl_Unloaded(object sender, RoutedEventArgs e)
        {
            // Cleanup if needed
        }

        private async Task InitializeAmdServiceAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Initializing AMD service...");

                _amdAdlxService = new AmdAdlxService();

                // Check if AMD GPU is available
                if (!_amdAdlxService.IsAmdGpuAvailable())
                {
                    System.Diagnostics.Debug.WriteLine("No AMD GPU detected - AMD Features will be non-functional");
                    // TODO: Consider hiding/disabling the control or showing a warning
                    return;
                }

                // Load current RSR state
                var (success, enabled, sharpness) = await _amdAdlxService.GetRsrStateAsync();
                if (success)
                {
                    _isApplyingSettings = true;
                    _rsrEnabled = enabled;
                    _rsrSharpness = sharpness;
                    OnPropertyChanged(nameof(RsrEnabled));
                    OnPropertyChanged(nameof(RsrSharpness));
                    _isApplyingSettings = false;

                    System.Diagnostics.Debug.WriteLine($"Loaded RSR state: enabled={enabled}, sharpness={sharpness}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing AMD service: {ex.Message}");
            }
        }

        private async Task ApplyRsrSettingsAsync(bool enabled, int sharpness)
        {
            if (_amdAdlxService == null)
            {
                System.Diagnostics.Debug.WriteLine("AMD service not initialized");
                return;
            }

            if (!_amdAdlxService.IsAmdGpuAvailable())
            {
                System.Diagnostics.Debug.WriteLine("Cannot apply RSR: No AMD GPU detected");
                // Revert toggle state
                _isApplyingSettings = true;
                _rsrEnabled = !enabled;
                OnPropertyChanged(nameof(RsrEnabled));
                _isApplyingSettings = false;
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"Applying RSR settings: enabled={enabled}, sharpness={sharpness}");

                bool success = await _amdAdlxService.SetRsrEnabledAsync(enabled, sharpness);

                if (!success)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to apply RSR settings");

                    // Revert toggle state on failure
                    _isApplyingSettings = true;
                    _rsrEnabled = !enabled;
                    OnPropertyChanged(nameof(RsrEnabled));
                    _isApplyingSettings = false;

                    // TODO: Show error message to user
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Successfully applied RSR settings: enabled={enabled}, sharpness={sharpness}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error applying RSR settings: {ex.Message}");

                // Revert toggle state on error
                _isApplyingSettings = true;
                _rsrEnabled = !enabled;
                OnPropertyChanged(nameof(RsrEnabled));
                _isApplyingSettings = false;
            }
        }

        private async Task ApplyRsrSharpnessAsync(int sharpness)
        {
            if (_amdAdlxService == null)
            {
                System.Diagnostics.Debug.WriteLine("AMD service not initialized");
                return;
            }

            if (!_amdAdlxService.IsAmdGpuAvailable())
            {
                System.Diagnostics.Debug.WriteLine("Cannot apply sharpness: No AMD GPU detected");
                return;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"Applying RSR sharpness: {sharpness}");

                // Since RSR is already enabled, just update sharpness
                bool success = await _amdAdlxService.SetRsrEnabledAsync(true, sharpness);

                if (!success)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to apply RSR sharpness");
                    // TODO: Show error message to user
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Successfully applied RSR sharpness: {sharpness}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error applying RSR sharpness: {ex.Message}");
            }
        }

        private void InitializeGamepadNavigation()
        {
            GamepadNavigation.SetIsEnabled(this, true);
            GamepadNavigation.SetNavigationGroup(this, "MainControls");
            GamepadNavigation.SetNavigationOrder(this, 11);
        }

        private void InitializeGamepadNavigationService()
        {
            if (Application.Current is App app && app.MainWindow is MainWindow mainWindow)
            {
                _gamepadNavigationService = mainWindow.GamepadNavigationService;
            }
        }

        // IGamepadNavigable event handlers
        public void OnGamepadNavigateUp()
        {
            // If slider is activated, adjust value instead of navigating
            if (_isSliderActivated)
            {
                AdjustSliderValue(1); // Increase value
                return;
            }

            if (_currentFocusedElement > 0)
            {
                _currentFocusedElement--;
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 AmdFeatures: Moved up to element {_currentFocusedElement}");
            }
        }

        public void OnGamepadNavigateDown()
        {
            // If slider is activated, adjust value instead of navigating
            if (_isSliderActivated)
            {
                AdjustSliderValue(-1); // Decrease value
                return;
            }

            if (_currentFocusedElement < 1)
            {
                _currentFocusedElement++;
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 AmdFeatures: Moved down to element {_currentFocusedElement}");
            }
        }

        public void OnGamepadNavigateLeft()
        {
            // If slider is activated, adjust value
            if (_isSliderActivated)
            {
                AdjustSliderValue(-1); // Decrease value
                return;
            }
        }

        public void OnGamepadNavigateRight()
        {
            // If slider is activated, adjust value
            if (_isSliderActivated)
            {
                AdjustSliderValue(1); // Increase value
                return;
            }
        }

        public void OnGamepadActivate()
        {
            switch (_currentFocusedElement)
            {
                case 0: // RSR Toggle
                    if (RsrToggle != null)
                    {
                        RsrToggle.IsOn = !RsrToggle.IsOn;
                        System.Diagnostics.Debug.WriteLine($"🎮 AmdFeatures: Toggled RSR to {RsrToggle.IsOn}");
                    }
                    break;

                case 1: // Sharpness Slider
                    if (!_isSliderActivated)
                    {
                        // Activate slider for value adjustment
                        _isSliderActivated = true;
                        IsSliderActivated = true;
                        System.Diagnostics.Debug.WriteLine($"🎮 AmdFeatures: Activated sharpness slider for adjustment");
                    }
                    else
                    {
                        // Deactivate slider
                        _isSliderActivated = false;
                        IsSliderActivated = false;
                        System.Diagnostics.Debug.WriteLine($"🎮 AmdFeatures: Deactivated sharpness slider adjustment");
                    }
                    break;
            }
        }

        public void OnGamepadFocusReceived()
        {
            // Initialize gamepad service if needed
            if (_gamepadNavigationService == null)
            {
                InitializeGamepadNavigationService();
            }

            _currentFocusedElement = 0; // Start with RSR Toggle
            _isFocused = true;
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 AmdFeatures: Received gamepad focus");
        }

        public void OnGamepadFocusLost()
        {
            _isFocused = false;
            _isSliderActivated = false;
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 AmdFeatures: Lost gamepad focus");
        }

        private void UpdateFocusVisuals()
        {
            // Dispatch on UI thread to ensure bindings update reliably with gamepad navigation
            DispatcherQueue.TryEnqueue(() =>
            {
                OnPropertyChanged(nameof(RsrToggleFocusBrush));
                OnPropertyChanged(nameof(SharpnessSliderFocusBrush));
            });
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            this.Loaded -= AmdFeaturesControl_Loaded;
            this.Unloaded -= AmdFeaturesControl_Unloaded;

            if (_amdAdlxService != null)
            {
                _amdAdlxService.Dispose();
                _amdAdlxService = null;
            }
        }
    }
}
