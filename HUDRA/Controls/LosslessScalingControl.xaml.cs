using HUDRA.Interfaces;
using HUDRA.AttachedProperties;
using HUDRA.Pages;
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
    public sealed partial class LosslessScalingControl : UserControl, INotifyPropertyChanged, IGamepadNavigable
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private GamepadNavigationService? _gamepadNavigationService;
        private int _currentFocusedElement = 0; // 0=Toggle, 1=FrameGen, 2=FlowScale, 3=Apply, 4=Reset, 5=Restore
        private bool _isFocused = false;
        private bool _isSliderActivated = false;
        private int _activeSliderIndex = -1;

        // IGamepadNavigable implementation
        public bool CanNavigateUp => _currentFocusedElement > 0;
        public bool CanNavigateDown => _currentFocusedElement < 5;
        public bool CanNavigateLeft => (_currentFocusedElement >= 3 && _currentFocusedElement < 5) || _isSliderActivated; // Between buttons or adjusting slider
        public bool CanNavigateRight => (_currentFocusedElement >= 3 && _currentFocusedElement < 5) || _isSliderActivated; // Between buttons or adjusting slider
        public bool CanActivate => true;
        public FrameworkElement NavigationElement => this;

        // Slider interface implementations
        public bool IsSlider => _currentFocusedElement == 1 || _currentFocusedElement == 2; // FrameGen or FlowScale
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

        // ComboBox interface implementations - LosslessScaling has no ComboBoxes
        public bool HasComboBoxes => false;
        public bool IsComboBoxOpen { get; set; } = false;
        public ComboBox? GetFocusedComboBox() => null;
        public int ComboBoxOriginalIndex { get; set; } = -1;
        public bool IsNavigatingComboBox { get; set; } = false;
        public void ProcessCurrentSelection() { /* Not applicable - no ComboBoxes */ }

        // Focus brush properties for XAML binding
        public Brush UpscalingToggleFocusBrush
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

        public Brush FrameGenSliderFocusBrush
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

        public Brush FlowScaleSliderFocusBrush
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true && _currentFocusedElement == 2)
                {
                    return new SolidColorBrush(_isSliderActivated ? Microsoft.UI.Colors.DodgerBlue : Microsoft.UI.Colors.DarkViolet);
                }
                return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        public Brush ApplyButtonFocusBrush
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

        public Brush ResetButtonFocusBrush
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true && _currentFocusedElement == 4)
                {
                    return new SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
                }
                return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        public Brush RestoreButtonFocusBrush
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true && _currentFocusedElement == 5)
                {
                    return new SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
                }
                return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        public LosslessScalingControl()
        {
            this.InitializeComponent();
            this.DataContext = this;
            InitializeGamepadNavigation();
        }

        private void InitializeGamepadNavigation()
        {
            GamepadNavigation.SetIsEnabled(this, true);
            GamepadNavigation.SetNavigationGroup(this, "MainControls");
            GamepadNavigation.SetNavigationOrder(this, 21);
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
            // If a slider is activated, adjust value instead of navigating
            if (_isSliderActivated)
            {
                AdjustSliderValue(1); // Increase value
                return;
            }

            if (_currentFocusedElement > 0)
            {
                _currentFocusedElement--;
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 LosslessScaling: Moved up to element {_currentFocusedElement}");
            }
        }

        public void OnGamepadNavigateDown()
        {
            // If a slider is activated, adjust value instead of navigating
            if (_isSliderActivated)
            {
                AdjustSliderValue(-1); // Decrease value
                return;
            }

            if (_currentFocusedElement < 5)
            {
                _currentFocusedElement++;
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 LosslessScaling: Moved down to element {_currentFocusedElement}");
            }
        }

        public void OnGamepadNavigateLeft()
        {
            // If a slider is activated, adjust value
            if (_isSliderActivated)
            {
                AdjustSliderValue(-1); // Decrease value
                return;
            }

            // Navigate between buttons (Apply, Reset, Restore)
            if (_currentFocusedElement >= 4 && _currentFocusedElement <= 5)
            {
                _currentFocusedElement--;
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 LosslessScaling: Moved left to button {_currentFocusedElement}");
            }
        }

        public void OnGamepadNavigateRight()
        {
            // If a slider is activated, adjust value
            if (_isSliderActivated)
            {
                AdjustSliderValue(1); // Increase value
                return;
            }

            // Navigate between buttons (Apply, Reset, Restore)
            if (_currentFocusedElement >= 3 && _currentFocusedElement < 5)
            {
                _currentFocusedElement++;
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 LosslessScaling: Moved right to button {_currentFocusedElement}");
            }
        }

        public void OnGamepadActivate()
        {
            switch (_currentFocusedElement)
            {
                case 0: // Upscaling Toggle
                    if (UpscalingToggle != null)
                    {
                        UpscalingToggle.IsOn = !UpscalingToggle.IsOn;
                        System.Diagnostics.Debug.WriteLine($"🎮 LosslessScaling: Toggled upscaling to {UpscalingToggle.IsOn}");
                    }
                    break;

                case 1: // Frame Generation Slider
                case 2: // Flow Scale Slider
                    if (!_isSliderActivated)
                    {
                        // Activate slider for value adjustment
                        _isSliderActivated = true;
                        _activeSliderIndex = _currentFocusedElement;
                        IsSliderActivated = true;
                        System.Diagnostics.Debug.WriteLine($"🎮 LosslessScaling: Activated slider {_currentFocusedElement} for adjustment");
                    }
                    else
                    {
                        // Deactivate slider
                        _isSliderActivated = false;
                        _activeSliderIndex = -1;
                        IsSliderActivated = false;
                        System.Diagnostics.Debug.WriteLine($"🎮 LosslessScaling: Deactivated slider adjustment");
                    }
                    break;

                case 3: // Apply Button
                    ApplyButton_Click(ApplyButton, new RoutedEventArgs());
                    System.Diagnostics.Debug.WriteLine($"🎮 LosslessScaling: Activated Apply button");
                    break;

                case 4: // Reset Button
                    ResetButton_Click(ResetButton, new RoutedEventArgs());
                    System.Diagnostics.Debug.WriteLine($"🎮 LosslessScaling: Activated Reset button");
                    break;

                case 5: // Restore Button
                    RestoreButton_Click(RestoreButton, new RoutedEventArgs());
                    System.Diagnostics.Debug.WriteLine($"🎮 LosslessScaling: Activated Restore button");
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

            _currentFocusedElement = 0; // Start with Upscaling Toggle
            _isFocused = true;
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 LosslessScaling: Received gamepad focus");
        }

        public void OnGamepadFocusLost()
        {
            _isFocused = false;
            _isSliderActivated = false;
            _activeSliderIndex = -1;
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 LosslessScaling: Lost gamepad focus");
        }

        public void AdjustSliderValue(int direction)
        {
            if (!_isSliderActivated) return;

            if (_currentFocusedElement == 1 && FrameGenSlider != null) // Frame Generation
            {
                double newValue = FrameGenSlider.Value + direction;
                newValue = Math.Clamp(newValue, FrameGenSlider.Minimum, FrameGenSlider.Maximum);
                FrameGenSlider.Value = newValue;
                System.Diagnostics.Debug.WriteLine($"🎮 LosslessScaling: Adjusted Frame Generation to {newValue}");
            }
            else if (_currentFocusedElement == 2 && FlowScaleSlider != null) // Flow Scale
            {
                double increment = 5.0; // 5% increments for Flow Scale
                double newValue = FlowScaleSlider.Value + (direction * increment);
                newValue = Math.Clamp(newValue, FlowScaleSlider.Minimum, FlowScaleSlider.Maximum);
                FlowScaleSlider.Value = newValue;
                System.Diagnostics.Debug.WriteLine($"🎮 LosslessScaling: Adjusted Flow Scale to {newValue}");
            }
        }

        private void UpdateFocusVisuals()
        {
            // Dispatch on UI thread to ensure bindings update reliably with gamepad navigation
            DispatcherQueue.TryEnqueue(() =>
            {
                OnPropertyChanged(nameof(UpscalingToggleFocusBrush));
                OnPropertyChanged(nameof(FrameGenSliderFocusBrush));
                OnPropertyChanged(nameof(FlowScaleSliderFocusBrush));
                OnPropertyChanged(nameof(ApplyButtonFocusBrush));
                OnPropertyChanged(nameof(ResetButtonFocusBrush));
                OnPropertyChanged(nameof(RestoreButtonFocusBrush));
            });
        }

        // Button click handlers - delegate to parent page
        private async void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ScalingPageViewModel viewModel)
            {
                viewModel.IsApplying = true;
                await Task.Delay(1);
                await viewModel.ApplySettingsAsync();
            }
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ScalingPageViewModel viewModel)
            {
                viewModel.ResetSettings();
            }
        }

        private async void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ScalingPageViewModel viewModel)
            {
                viewModel.IsApplying = true;
                await Task.Delay(1);
                await viewModel.RestoreUserSettingsAsync();
            }
        }

        // Helper methods for finding elements
        private T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            if (parent == null) return null;
            return parent is T ? (T)parent : FindParent<T>(parent);
        }

        private T? FindChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T element && element.Name == name)
                    return element;

                var result = FindChildByName<T>(child, name);
                if (result != null)
                    return result;
            }
            return null;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}