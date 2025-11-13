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
        private int _currentFocusedElement = 0; // 0=RSR Toggle
        private bool _isFocused = false;
        private bool _isInitialized = false;
        private bool _isApplyingSettings = false;

        // IGamepadNavigable implementation
        public bool CanNavigateUp => false; // Single element, no vertical navigation within control
        public bool CanNavigateDown => false;
        public bool CanNavigateLeft => false;
        public bool CanNavigateRight => false;
        public bool CanActivate => true;
        public FrameworkElement NavigationElement => this;

        // Slider interface implementations - AmdFeatures has no sliders
        public bool IsSlider => false;
        public bool IsSliderActivated { get; set; } = false;
        public void AdjustSliderValue(int direction) { /* Not applicable - no sliders */ }

        // ComboBox interface implementations - AmdFeatures has no ComboBoxes
        public bool HasComboBoxes => false;
        public bool IsComboBoxOpen { get; set; } = false;
        public ComboBox? GetFocusedComboBox() => null;
        public int ComboBoxOriginalIndex { get; set; } = -1;
        public bool IsNavigatingComboBox { get; set; } = false;
        public void ProcessCurrentSelection() { /* Not applicable - no ComboBoxes */ }

        // Focus brush property for XAML binding
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

                    // Apply RSR settings asynchronously
                    _ = ApplyRsrSettingAsync(value);
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
                    OnPropertyChanged(nameof(RsrEnabled));
                    _isApplyingSettings = false;

                    System.Diagnostics.Debug.WriteLine($"Loaded RSR state: enabled={enabled}, sharpness={sharpness}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing AMD service: {ex.Message}");
            }
        }

        private async Task ApplyRsrSettingAsync(bool enabled)
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
                System.Diagnostics.Debug.WriteLine($"Applying RSR setting: {enabled}");

                // Apply with default sharpness of 80%
                bool success = await _amdAdlxService.SetRsrEnabledAsync(enabled, sharpness: 80);

                if (!success)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to apply RSR setting");

                    // Revert toggle state on failure
                    _isApplyingSettings = true;
                    _rsrEnabled = !enabled;
                    OnPropertyChanged(nameof(RsrEnabled));
                    _isApplyingSettings = false;

                    // TODO: Show error message to user
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Successfully applied RSR setting: {enabled}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error applying RSR setting: {ex.Message}");

                // Revert toggle state on error
                _isApplyingSettings = true;
                _rsrEnabled = !enabled;
                OnPropertyChanged(nameof(RsrEnabled));
                _isApplyingSettings = false;
            }
        }

        private void InitializeGamepadNavigation()
        {
            GamepadNavigation.SetIsEnabled(this, true);
            GamepadNavigation.SetNavigationGroup(this, "MainControls");
            GamepadNavigation.SetNavigationOrder(this, 1);
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
            // Single element - no internal navigation
            System.Diagnostics.Debug.WriteLine($"🎮 AmdFeatures: Navigate up (no-op)");
        }

        public void OnGamepadNavigateDown()
        {
            // Single element - no internal navigation
            System.Diagnostics.Debug.WriteLine($"🎮 AmdFeatures: Navigate down (no-op)");
        }

        public void OnGamepadNavigateLeft()
        {
            // Single element - no internal navigation
            System.Diagnostics.Debug.WriteLine($"🎮 AmdFeatures: Navigate left (no-op)");
        }

        public void OnGamepadNavigateRight()
        {
            // Single element - no internal navigation
            System.Diagnostics.Debug.WriteLine($"🎮 AmdFeatures: Navigate right (no-op)");
        }

        public void OnGamepadActivate()
        {
            // Toggle RSR when activated
            if (RsrToggle != null)
            {
                RsrToggle.IsOn = !RsrToggle.IsOn;
                System.Diagnostics.Debug.WriteLine($"🎮 AmdFeatures: Toggled RSR to {RsrToggle.IsOn}");
            }
        }

        public void OnGamepadFocusReceived()
        {
            // Initialize gamepad service if needed
            if (_gamepadNavigationService == null)
            {
                InitializeGamepadNavigationService();
            }

            _currentFocusedElement = 0; // Focus on RSR Toggle
            _isFocused = true;
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 AmdFeatures: Received gamepad focus");
        }

        public void OnGamepadFocusLost()
        {
            _isFocused = false;
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 AmdFeatures: Lost gamepad focus");
        }

        private void UpdateFocusVisuals()
        {
            // Dispatch on UI thread to ensure bindings update reliably with gamepad navigation
            DispatcherQueue.TryEnqueue(() =>
            {
                OnPropertyChanged(nameof(RsrToggleFocusBrush));
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
