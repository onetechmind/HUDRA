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
        private static AmdAdlxService? _sharedAmdAdlxService; // Singleton service instance
        private AmdAdlxService? _amdAdlxService => _sharedAmdAdlxService; // Reference to shared instance
        private int _currentFocusedElement = 0; // 0=RSR Toggle, 1=Sharpness Slider, 2=AFMF Toggle, 3=Anti-Lag Toggle
        private bool _isFocused = false;
        private bool _isInitialized = false;
        private bool _isApplyingSettings = false;
        private static bool _initializationFailed = false; // Track if initialization has already failed

        // Debounce timer for RSR sharpness slider
        private DispatcherTimer? _sharpnessDebounceTimer;
        private int _pendingSharpness = -1;

        // IGamepadNavigable implementation
        public bool CanNavigateUp => _currentFocusedElement > 0; // Can navigate up from slider/AFMF/Anti-Lag to previous element
        public bool CanNavigateDown => _currentFocusedElement < 3; // Can navigate down to Anti-Lag toggle
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

        public Brush AfmfToggleFocusBrush
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true && _currentFocusedElement == 2)
                {
                    return new SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
                }
                return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        public Brush AntiLagToggleFocusBrush
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true && _currentFocusedElement == 3)
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

                    // Debounce sharpness changes - only apply after user stops dragging
                    if (_rsrEnabled && _sharpnessDebounceTimer != null)
                    {
                        _pendingSharpness = value;
                        _sharpnessDebounceTimer.Stop();
                        _sharpnessDebounceTimer.Start();
                    }
                }
            }
        }

        private bool _afmfEnabled = false;
        public bool AfmfEnabled
        {
            get => _afmfEnabled;
            set
            {
                if (_afmfEnabled != value && !_isApplyingSettings)
                {
                    _afmfEnabled = value;
                    OnPropertyChanged();

                    // Apply AFMF settings asynchronously
                    _ = ApplyAfmfSettingsAsync(value);
                }
            }
        }

        private bool _antiLagEnabled = false;
        public bool AntiLagEnabled
        {
            get => _antiLagEnabled;
            set
            {
                if (_antiLagEnabled != value && !_isApplyingSettings)
                {
                    _antiLagEnabled = value;
                    OnPropertyChanged();

                    // Apply Anti-Lag settings asynchronously
                    _ = ApplyAntiLagSettingsAsync(value);
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

            // Initialize debounce timer for sharpness slider (500ms delay)
            _sharpnessDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _sharpnessDebounceTimer.Tick += SharpnessDebounceTimer_Tick;
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
            // Cleanup handled in Dispose
        }

        private async void SharpnessDebounceTimer_Tick(object? sender, object e)
        {
            _sharpnessDebounceTimer?.Stop();

            if (_pendingSharpness >= 0 && _rsrEnabled)
            {
                await ApplyRsrSharpnessAsync(_pendingSharpness);
                _pendingSharpness = -1;
            }
        }

        private async Task InitializeAmdServiceAsync()
        {
            try
            {
                // If initialization has already failed once, don't try again
                if (_initializationFailed)
                {
                    return;
                }

                // If service already exists (singleton), reuse it
                if (_sharedAmdAdlxService != null)
                {
                    // Load current state from the existing service
                    await LoadCurrentStateAsync();
                    return;
                }

                // Wrap service creation in try-catch to prevent crashes
                try
                {
                    _sharedAmdAdlxService = new AmdAdlxService();
                }
                catch (Exception serviceEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to create AMD service: {serviceEx.Message}");
                    System.Diagnostics.Debug.WriteLine($"Stack trace: {serviceEx.StackTrace}");
                    _sharedAmdAdlxService = null;
                    _initializationFailed = true; // Mark as failed to prevent retry
                    return;
                }

                // Check if AMD GPU is available
                if (!_sharedAmdAdlxService.IsAmdGpuAvailable())
                {
                    System.Diagnostics.Debug.WriteLine("No AMD GPU detected - AMD Features will be non-functional");
                    // TODO: Consider hiding/disabling the control or showing a warning
                    return;
                }

                // Load current state from driver
                await LoadCurrentStateAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing AMD service: {ex.Message}");
            }
        }

        private async Task LoadCurrentStateAsync()
        {
            if (_amdAdlxService == null)
            {
                return;
            }

            try
            {
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
                }

                // Load current AFMF state
                var (afmfSuccess, afmfEnabled) = await _amdAdlxService.GetAfmfStateAsync();
                if (afmfSuccess)
                {
                    _isApplyingSettings = true;
                    _afmfEnabled = afmfEnabled;
                    OnPropertyChanged(nameof(AfmfEnabled));
                    _isApplyingSettings = false;
                }

                // Load current Anti-Lag state
                var (antiLagSuccess, antiLagEnabled) = await _amdAdlxService.GetAntiLagStateAsync();
                if (antiLagSuccess)
                {
                    _isApplyingSettings = true;
                    _antiLagEnabled = antiLagEnabled;
                    OnPropertyChanged(nameof(AntiLagEnabled));
                    _isApplyingSettings = false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading AMD state: {ex.Message}");
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
                bool success = await _amdAdlxService.SetRsrEnabledAsync(enabled, sharpness);

                if (!success)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to apply RSR settings");

                    // Revert toggle state on failure
                    _isApplyingSettings = true;
                    _rsrEnabled = !enabled;
                    OnPropertyChanged(nameof(RsrEnabled));
                    _isApplyingSettings = false;
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
                // Since RSR is already enabled, just update sharpness
                bool success = await _amdAdlxService.SetRsrEnabledAsync(true, sharpness);

                if (!success)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to apply RSR sharpness");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error applying RSR sharpness: {ex.Message}");
            }
        }

        private async Task ApplyAfmfSettingsAsync(bool enabled)
        {
            if (_amdAdlxService == null)
            {
                System.Diagnostics.Debug.WriteLine("AMD service not initialized");
                return;
            }

            if (!_amdAdlxService.IsAmdGpuAvailable())
            {
                System.Diagnostics.Debug.WriteLine("Cannot apply AFMF: No AMD GPU detected");
                return;
            }

            try
            {
                bool success = await _amdAdlxService.SetAfmfEnabledAsync(enabled);

                if (!success)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to apply AFMF settings");

                    // Revert toggle state on failure
                    _isApplyingSettings = true;
                    _afmfEnabled = !enabled;
                    OnPropertyChanged(nameof(AfmfEnabled));
                    _isApplyingSettings = false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error applying AFMF settings: {ex.Message}");

                // Revert toggle state on error
                _isApplyingSettings = true;
                _afmfEnabled = !enabled;
                OnPropertyChanged(nameof(AfmfEnabled));
                _isApplyingSettings = false;
            }
        }

        private async Task ApplyAntiLagSettingsAsync(bool enabled)
        {
            if (_amdAdlxService == null)
            {
                System.Diagnostics.Debug.WriteLine("AMD service not initialized");
                return;
            }

            if (!_amdAdlxService.IsAmdGpuAvailable())
            {
                System.Diagnostics.Debug.WriteLine("Cannot apply Anti-Lag: No AMD GPU detected");
                return;
            }

            try
            {
                bool success = await _amdAdlxService.SetAntiLagEnabledAsync(enabled);

                if (!success)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to apply Anti-Lag settings");

                    // Revert toggle state on failure
                    _isApplyingSettings = true;
                    _antiLagEnabled = !enabled;
                    OnPropertyChanged(nameof(AntiLagEnabled));
                    _isApplyingSettings = false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error applying Anti-Lag settings: {ex.Message}");

                // Revert toggle state on error
                _isApplyingSettings = true;
                _antiLagEnabled = !enabled;
                OnPropertyChanged(nameof(AntiLagEnabled));
                _isApplyingSettings = false;
            }
        }

        private void InitializeGamepadNavigation()
        {
            GamepadNavigation.SetIsEnabled(this, true);
            GamepadNavigation.SetNavigationGroup(this, "MainControls");
            GamepadNavigation.SetNavigationOrder(this, 11);
            // Not directly navigable at page level - only accessible through parent expander
            GamepadNavigation.SetCanNavigate(this, false);
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

            if (_currentFocusedElement < 3)
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

                case 2: // AFMF Toggle
                    if (AfmfToggle != null)
                    {
                        AfmfToggle.IsOn = !AfmfToggle.IsOn;
                        System.Diagnostics.Debug.WriteLine($"🎮 AmdFeatures: Toggled AFMF to {AfmfToggle.IsOn}");
                    }
                    break;

                case 3: // Anti-Lag Toggle
                    if (AntiLagToggle != null)
                    {
                        AntiLagToggle.IsOn = !AntiLagToggle.IsOn;
                        System.Diagnostics.Debug.WriteLine($"🎮 AmdFeatures: Toggled Anti-Lag to {AntiLagToggle.IsOn}");
                    }
                    break;
            }
        }

        public void OnGamepadBack() { }

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

        public void FocusLastElement()
        {
            // Focus the last element (Anti-Lag toggle)
            _currentFocusedElement = 3;
            _isFocused = true;
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 AmdFeatures: Focused last element (Anti-Lag toggle)");
        }

        private void UpdateFocusVisuals()
        {
            // Dispatch on UI thread to ensure bindings update reliably with gamepad navigation
            DispatcherQueue.TryEnqueue(() =>
            {
                OnPropertyChanged(nameof(RsrToggleFocusBrush));
                OnPropertyChanged(nameof(SharpnessSliderFocusBrush));
                OnPropertyChanged(nameof(AfmfToggleFocusBrush));
                OnPropertyChanged(nameof(AntiLagToggleFocusBrush));
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

            // Clean up debounce timer
            if (_sharpnessDebounceTimer != null)
            {
                _sharpnessDebounceTimer.Stop();
                _sharpnessDebounceTimer.Tick -= SharpnessDebounceTimer_Tick;
                _sharpnessDebounceTimer = null;
            }

            // Don't dispose the shared service - it's a singleton used across all control instances
            // The service will be disposed when the application exits
        }
    }
}
