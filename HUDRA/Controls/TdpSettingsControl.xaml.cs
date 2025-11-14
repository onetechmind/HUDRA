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
    public sealed partial class TdpSettingsControl : UserControl, INotifyPropertyChanged, IGamepadNavigable
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private GamepadNavigationService? _gamepadNavigationService;
        private int _currentFocusedElement = 0; // 0=UseStartupTdpToggle, 1=StartupTdpPicker, 2=TdpCorrectionToggle
        private bool _isFocused = false;

        // IGamepadNavigable implementation
        public bool CanNavigateUp => _currentFocusedElement > 0;
        public bool CanNavigateDown
        {
            get
            {
                // If on Sticky TDP (element 2), we can't navigate down - should go to next expander
                if (_currentFocusedElement == 2) return false;
                // If on TDP picker (element 1), can navigate down to Sticky TDP
                if (_currentFocusedElement == 1) return true;
                // If on UseStartupTdp (element 0), check if TDP picker is enabled
                if (_currentFocusedElement == 0)
                {
                    return UseStartupTdpToggle?.IsOn ?? false ? true : true; // Can navigate to either picker or sticky
                }
                return false;
            }
        }
        public bool CanNavigateLeft
        {
            get
            {
                // Allow left/right navigation when TDP picker is focused
                return _currentFocusedElement == 1 && StartupTdpPicker != null;
            }
        }
        public bool CanNavigateRight
        {
            get
            {
                // Allow left/right navigation when TDP picker is focused
                return _currentFocusedElement == 1 && StartupTdpPicker != null;
            }
        }
        public bool CanActivate => true;
        public FrameworkElement NavigationElement => this;

        // TdpPicker integration - no slider functionality needed for TDP Settings
        public bool IsSlider => false;
        public bool IsSliderActivated { get; set; } = false;

        // ComboBox interface implementations - TdpSettings has no ComboBoxes
        public bool HasComboBoxes => false;
        public bool IsComboBoxOpen { get; set; } = false;
        public ComboBox? GetFocusedComboBox() => null;
        public int ComboBoxOriginalIndex { get; set; } = -1;
        public bool IsNavigatingComboBox { get; set; } = false;
        public void ProcessCurrentSelection() { /* Not applicable - no ComboBoxes */ }

        // Focus brush properties for XAML binding
        public Brush UseStartupTdpFocusBrush
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

        public Brush TdpPickerFocusBrush
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

        public Brush StickyTdpFocusBrush
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

        public TdpSettingsControl()
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
                // Skip TdpPicker (index 1) if not enabled
                if (_currentFocusedElement == 2 && (!UseStartupTdpToggle?.IsOn ?? false))
                {
                    _currentFocusedElement = 0; // Jump directly to UseStartupTdpToggle
                }
                else if (_currentFocusedElement == 1)
                {
                    _currentFocusedElement = 0; // Move to UseStartupTdpToggle
                }
                else
                {
                    _currentFocusedElement--;
                }
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 TdpSettings: Moved up to element {_currentFocusedElement}");
            }
        }

        public void OnGamepadNavigateDown()
        {
            if (_currentFocusedElement < 2)
            {
                // Skip TdpPicker (index 1) if not enabled
                if (_currentFocusedElement == 0 && (!UseStartupTdpToggle?.IsOn ?? false))
                {
                    _currentFocusedElement = 2; // Jump directly to TdpCorrectionToggle
                }
                else if (_currentFocusedElement == 1)
                {
                    _currentFocusedElement = 2; // Move to TdpCorrectionToggle
                }
                else
                {
                    _currentFocusedElement++;
                }
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 TdpSettings: Moved down to element {_currentFocusedElement}");
            }
            else
            {
                // At Sticky TDP (element 2) - don't handle navigation internally
                // Let GamepadNavigationService handle moving to next expander
                System.Diagnostics.Debug.WriteLine($"🎮 TdpSettings: At element {_currentFocusedElement}, letting navigation service handle move to next expander");
            }
        }

        public void OnGamepadNavigateLeft()
        {
            // Delegate to TDP picker if it's focused
            if (_currentFocusedElement == 1 && StartupTdpPicker != null)
            {
                StartupTdpPicker.OnGamepadNavigateLeft();
            }
        }

        public void OnGamepadNavigateRight()
        {
            // Delegate to TDP picker if it's focused
            if (_currentFocusedElement == 1 && StartupTdpPicker != null)
            {
                StartupTdpPicker.OnGamepadNavigateRight();
            }
        }

        public void OnGamepadActivate()
        {
            switch (_currentFocusedElement)
            {
                case 0: // UseStartupTdpToggle
                    if (UseStartupTdpToggle != null)
                    {
                        UseStartupTdpToggle.IsOn = !UseStartupTdpToggle.IsOn;
                        System.Diagnostics.Debug.WriteLine($"🎮 TdpSettings: Toggled UseStartupTdp to {UseStartupTdpToggle.IsOn}");
                    }
                    break;

                case 1: // StartupTdpPicker
                    // TdpPicker handles its own gamepad activation
                    if (StartupTdpPicker != null)
                    {
                        StartupTdpPicker.OnGamepadActivate();
                        System.Diagnostics.Debug.WriteLine($"🎮 TdpSettings: Activated TdpPicker");
                    }
                    break;

                case 2: // TdpCorrectionToggle
                    if (TdpCorrectionToggle != null)
                    {
                        TdpCorrectionToggle.IsOn = !TdpCorrectionToggle.IsOn;
                        System.Diagnostics.Debug.WriteLine($"🎮 TdpSettings: Toggled TdpCorrection to {TdpCorrectionToggle.IsOn}");
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

            _currentFocusedElement = 0; // Start with UseStartupTdpToggle
            _isFocused = true;
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 TdpSettings: Received gamepad focus");
        }

        public void OnGamepadFocusLost()
        {
            _isFocused = false;
            // Also notify TDP picker if it was focused
            if (_currentFocusedElement == 1 && StartupTdpPicker != null)
            {
                StartupTdpPicker.OnGamepadFocusLost();
            }
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 TdpSettings: Lost gamepad focus");
        }

        public void FocusLastElement()
        {
            // Focus the last element (element 2: Sticky TDP toggle)
            _currentFocusedElement = 2;
            _isFocused = true;
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 TdpSettings: Focused last element (Sticky TDP)");
        }

        public void AdjustSliderValue(int direction)
        {
            // No sliders in TDP Settings, but delegate to TdpPicker if focused
            if (_currentFocusedElement == 1 && StartupTdpPicker != null)
            {
                StartupTdpPicker.AdjustSliderValue(direction);
            }
        }

        private void UpdateFocusVisuals()
        {
            // Dispatch on UI thread to ensure bindings update reliably with gamepad navigation
            DispatcherQueue.TryEnqueue(() =>
            {
                OnPropertyChanged(nameof(UseStartupTdpFocusBrush));
                OnPropertyChanged(nameof(TdpPickerFocusBrush));
                OnPropertyChanged(nameof(StickyTdpFocusBrush));

                // Update TDP picker focus state
                if (StartupTdpPicker != null)
                {
                    if (_currentFocusedElement == 1)
                    {
                        StartupTdpPicker.OnGamepadFocusReceived();
                    }
                    else
                    {
                        StartupTdpPicker.OnGamepadFocusLost();
                    }
                }
            });
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}