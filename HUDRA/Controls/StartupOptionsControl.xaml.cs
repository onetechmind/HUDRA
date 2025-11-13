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
        private int _currentFocusedElement = 0; // 0=Startup, 1=Minimize, 2=RTSS, 3=Hotkey
        private bool _isFocused = false;

        // IGamepadNavigable implementation
        public bool CanNavigateUp => _currentFocusedElement > 0;
        public bool CanNavigateDown => _currentFocusedElement < 3;
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
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true && _currentFocusedElement == 2)
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
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true && _currentFocusedElement == 3)
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
            if (_currentFocusedElement < 3)
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
            switch (_currentFocusedElement)
            {
                case 0: // StartupToggle
                    if (StartupToggle != null)
                    {
                        StartupToggle.IsOn = !StartupToggle.IsOn;
                        System.Diagnostics.Debug.WriteLine($"🎮 StartupOptions: Toggled Startup to {StartupToggle.IsOn}");
                    }
                    break;

                case 1: // MinimizeOnStartupToggle
                    if (MinimizeOnStartupToggle != null)
                    {
                        MinimizeOnStartupToggle.IsOn = !MinimizeOnStartupToggle.IsOn;
                        System.Diagnostics.Debug.WriteLine($"🎮 StartupOptions: Toggled Minimize to {MinimizeOnStartupToggle.IsOn}");
                    }
                    break;

                case 2: // StartRtssWithHudraToggle
                    if (StartRtssWithHudraToggle != null)
                    {
                        StartRtssWithHudraToggle.IsOn = !StartRtssWithHudraToggle.IsOn;
                        System.Diagnostics.Debug.WriteLine($"🎮 StartupOptions: Toggled RTSS to {StartRtssWithHudraToggle.IsOn}");
                    }
                    break;

                case 3: // HideShowHotkeySelector
                    if (HideShowHotkeySelector != null)
                    {
                        // Delegate to HotkeySelector's gamepad activation (once implemented)
                        System.Diagnostics.Debug.WriteLine($"🎮 StartupOptions: Activated HotkeySelector");

                        // For now, we'll handle basic activation here until HotkeySelector gets gamepad support
                        // This could trigger the Edit button or similar functionality
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
            // Focus the last element (element 3: Auto-start with Windows toggle)
            _currentFocusedElement = 3;
            _isFocused = true;
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 StartupOptions: Focused last element (Auto-start with Windows)");
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
                OnPropertyChanged(nameof(HotkeyFocusBrush));
            });
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}