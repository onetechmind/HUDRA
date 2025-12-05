using HUDRA.Interfaces;
using HUDRA.AttachedProperties;
using HUDRA.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HUDRA.Controls
{
    public sealed partial class StartupOptionsControl : UserControl, INotifyPropertyChanged, IGamepadNavigable
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private GamepadNavigationService? _gamepadNavigationService;
        private int _currentFocusedElement = 0; // Index into visible elements list
        private bool _isFocused = false;

        // RTSS and LS installation status (cached at startup)
        private bool _isRtssInstalled = false;
        private bool _isLsInstalled = false;

        /// <summary>
        /// Whether RTSS is installed (for conditional visibility).
        /// </summary>
        public bool IsRtssInstalled
        {
            get => _isRtssInstalled;
            set { _isRtssInstalled = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Whether Lossless Scaling is installed (for conditional visibility).
        /// </summary>
        public bool IsLsInstalled
        {
            get => _isLsInstalled;
            set { _isLsInstalled = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets the maximum focusable element index (dynamic based on visibility).
        /// Elements: 0=Startup, 1=Minimize, 2=RTSS (if installed), 3=LS (if installed), 4=Hotkey
        /// </summary>
        private int MaxFocusIndex
        {
            get
            {
                int count = 2; // Startup and Minimize are always visible
                if (_isRtssInstalled) count++;
                if (_isLsInstalled) count++;
                count++; // Hotkey is always visible
                return count - 1; // Max index is count - 1
            }
        }

        // IGamepadNavigable implementation
        public bool CanNavigateUp => _currentFocusedElement > 0;
        public bool CanNavigateDown => _currentFocusedElement < MaxFocusIndex;
        public bool CanNavigateLeft => false;
        public bool CanNavigateRight => false;
        public bool CanActivate => true;
        public FrameworkElement NavigationElement => this;

        // Slider interface implementations - StartupOptions has no sliders
        public bool IsSlider => false;
        public bool IsSliderActivated { get; set; } = false;

        // ComboBox interface implementations - StartupOptions has no ComboBoxes
        public bool HasComboBoxes => false;
        public bool IsComboBoxOpen { get; set; } = false;
        public ComboBox? GetFocusedComboBox() => null;
        public int ComboBoxOriginalIndex { get; set; } = -1;
        public bool IsNavigatingComboBox { get; set; } = false;
        public void ProcessCurrentSelection() { /* Not applicable - no ComboBoxes */ }

        // Focus brush properties for XAML binding
        // The focus index mapping is dynamic based on what's installed:
        // 0=Startup, 1=Minimize, 2=RTSS (if installed), 3=LS (if installed), Last=Hotkey

        private int GetVisualElementIndex(int visualIndex)
        {
            // Map visual element positions to focus indices
            // Startup=0, Minimize=1, RTSS=2 (if installed), LS=next (if installed), Hotkey=last
            if (visualIndex == 0) return 0; // Startup
            if (visualIndex == 1) return 1; // Minimize
            if (visualIndex == 2) return _isRtssInstalled ? 2 : -1; // RTSS
            if (visualIndex == 3) // LS
            {
                if (!_isLsInstalled) return -1;
                return _isRtssInstalled ? 3 : 2;
            }
            // Hotkey is always last
            return MaxFocusIndex;
        }

        public Brush StartupFocusBrush
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

        public Brush MinimizeFocusBrush
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true && _currentFocusedElement == 1)
                {
                    return new SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
                }
                return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        public Brush RtssFocusBrush
        {
            get
            {
                int rtssIndex = GetVisualElementIndex(2);
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true && _currentFocusedElement == rtssIndex && rtssIndex >= 0)
                {
                    return new SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
                }
                return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        public Brush LsFocusBrush
        {
            get
            {
                int lsIndex = GetVisualElementIndex(3);
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true && _currentFocusedElement == lsIndex && lsIndex >= 0)
                {
                    return new SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
                }
                return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        public Brush HotkeyFocusBrush
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true && _currentFocusedElement == MaxFocusIndex)
                {
                    return new SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
                }
                return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        public StartupOptionsControl()
        {
            this.InitializeComponent();
            this.DataContext = this;

            // Get cached installation statuses (set during App startup)
            _isRtssInstalled = RtssFpsLimiterService.GetCachedInstallationStatus();
            _isLsInstalled = LosslessScalingService.GetCachedInstallationStatus();

            InitializeGamepadNavigation();
        }

        private void InitializeGamepadNavigation()
        {
            GamepadNavigation.SetIsEnabled(this, true);
            GamepadNavigation.SetNavigationGroup(this, "MainControls");
            GamepadNavigation.SetNavigationOrder(this, 1);
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
            if (_currentFocusedElement > 0)
            {
                _currentFocusedElement--;
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 StartupOptions: Moved up to element {_currentFocusedElement}");
            }
        }

        public void OnGamepadNavigateDown()
        {
            if (_currentFocusedElement < MaxFocusIndex)
            {
                _currentFocusedElement++;
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 StartupOptions: Moved down to element {_currentFocusedElement}");
            }
        }

        public void OnGamepadNavigateLeft()
        {
            // No left/right navigation in StartupOptions
        }

        public void OnGamepadNavigateRight()
        {
            // No left/right navigation in StartupOptions
        }

        public void OnGamepadActivate()
        {
            // Map current focus index to the actual element
            // 0=Startup, 1=Minimize, then RTSS (if installed), LS (if installed), Hotkey (always last)
            int elementIndex = _currentFocusedElement;

            if (elementIndex == 0) // StartupToggle
            {
                if (StartupToggle != null)
                {
                    StartupToggle.IsOn = !StartupToggle.IsOn;
                    System.Diagnostics.Debug.WriteLine($"🎮 StartupOptions: Toggled Startup to {StartupToggle.IsOn}");
                }
            }
            else if (elementIndex == 1) // MinimizeOnStartupToggle
            {
                if (MinimizeOnStartupToggle != null)
                {
                    MinimizeOnStartupToggle.IsOn = !MinimizeOnStartupToggle.IsOn;
                    System.Diagnostics.Debug.WriteLine($"🎮 StartupOptions: Toggled Minimize to {MinimizeOnStartupToggle.IsOn}");
                }
            }
            else if (elementIndex == GetVisualElementIndex(2) && _isRtssInstalled) // StartRtssWithHudraToggle
            {
                if (StartRtssWithHudraToggle != null)
                {
                    StartRtssWithHudraToggle.IsOn = !StartRtssWithHudraToggle.IsOn;
                    System.Diagnostics.Debug.WriteLine($"🎮 StartupOptions: Toggled RTSS to {StartRtssWithHudraToggle.IsOn}");
                }
            }
            else if (elementIndex == GetVisualElementIndex(3) && _isLsInstalled) // StartLsWithHudraToggle
            {
                if (StartLsWithHudraToggle != null)
                {
                    StartLsWithHudraToggle.IsOn = !StartLsWithHudraToggle.IsOn;
                    System.Diagnostics.Debug.WriteLine($"🎮 StartupOptions: Toggled LS to {StartLsWithHudraToggle.IsOn}");
                }
            }
            else if (elementIndex == MaxFocusIndex) // HideShowHotkeySelector (always last)
            {
                if (HideShowHotkeySelector != null)
                {
                    // Delegate to HotkeySelector's gamepad activation (once implemented)
                    System.Diagnostics.Debug.WriteLine($"🎮 StartupOptions: Activated HotkeySelector");

                    // For now, we'll handle basic activation here until HotkeySelector gets gamepad support
                    // This could trigger the Edit button or similar functionality
                }
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

            _currentFocusedElement = 0; // Start with StartupToggle
            _isFocused = true;
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 StartupOptions: Received gamepad focus");
        }

        public void OnGamepadFocusLost()
        {
            _isFocused = false;
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 StartupOptions: Lost gamepad focus");
        }

        public void FocusLastElement()
        {
            // Focus the last element (Hotkey selector)
            _currentFocusedElement = MaxFocusIndex;
            _isFocused = true;
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 StartupOptions: Focused last element (Hotkey)");
        }

        public void AdjustSliderValue(int direction)
        {
            // No sliders in StartupOptions control
        }

        private void UpdateFocusVisuals()
        {
            // Dispatch on UI thread to ensure bindings update reliably with gamepad navigation
            DispatcherQueue.TryEnqueue(() =>
            {
                OnPropertyChanged(nameof(StartupFocusBrush));
                OnPropertyChanged(nameof(MinimizeFocusBrush));
                OnPropertyChanged(nameof(RtssFocusBrush));
                OnPropertyChanged(nameof(LsFocusBrush));
                OnPropertyChanged(nameof(HotkeyFocusBrush));
            });
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}