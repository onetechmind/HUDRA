using HUDRA.Interfaces;
using HUDRA.AttachedProperties;
using HUDRA.Models;
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
    public sealed partial class DefaultProfileControl : UserControl, INotifyPropertyChanged, IGamepadNavigable
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? DefaultsCleared;
        public event EventHandler? DefaultsSaved;

        private GamepadNavigationService? _gamepadNavigationService;
        private GameProfileService? _gameProfileService;
        private int _currentFocusedElement = 0; // 0=Save, 1=Clear (if visible)
        private bool _isFocused = false;
        private bool _hasDefaults = false;

        // IGamepadNavigable implementation
        public bool CanNavigateUp => false;
        public bool CanNavigateDown => false;
        public bool CanNavigateLeft => _currentFocusedElement > 0;
        public bool CanNavigateRight => _currentFocusedElement < MaxFocusIndex;
        public bool CanActivate => true;
        public FrameworkElement NavigationElement => this;

        // Maximum focus index depends on whether Clear button is visible
        private int MaxFocusIndex => _hasDefaults ? 1 : 0;

        // Slider interface implementations - no sliders
        public bool IsSlider => false;
        public bool IsSliderActivated { get; set; } = false;

        // ComboBox interface implementations - no ComboBoxes
        public bool HasComboBoxes => false;
        public bool IsComboBoxOpen { get; set; } = false;
        public ComboBox? GetFocusedComboBox() => null;
        public int ComboBoxOriginalIndex { get; set; } = -1;
        public bool IsNavigatingComboBox { get; set; } = false;
        public void ProcessCurrentSelection() { /* Not applicable */ }

        // Focus brush properties
        public Brush SaveButtonFocusBrush
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

        public Brush ClearButtonFocusBrush
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

        public Visibility ClearButtonVisibility => _hasDefaults ? Visibility.Visible : Visibility.Collapsed;

        public DefaultProfileControl()
        {
            this.InitializeComponent();
            this.DataContext = this;
            InitializeGamepadNavigation();
            UpdateDefaultProfileSummary();
        }

        private void InitializeGamepadNavigation()
        {
            GamepadNavigation.SetIsEnabled(this, true);
            GamepadNavigation.SetNavigationGroup(this, "MainControls");
            GamepadNavigation.SetNavigationOrder(this, 1);
            GamepadNavigation.SetCanNavigate(this, false);
        }

        private void InitializeGamepadNavigationService()
        {
            if (Application.Current is App app && app.MainWindow is MainWindow mainWindow)
            {
                _gamepadNavigationService = mainWindow.GamepadNavigationService;
            }
        }

        public void Initialize(GameProfileService? gameProfileService)
        {
            _gameProfileService = gameProfileService;
            UpdateDefaultProfileSummary();
        }

        public void UpdateDefaultProfileSummary()
        {
            if (NoDefaultsMessage == null || DefaultProfileGrid == null) return;

            var defaults = SettingsService.GetDefaultProfile();
            _hasDefaults = defaults != null;

            if (defaults == null)
            {
                NoDefaultsMessage.Visibility = Visibility.Visible;
                DefaultProfileGrid.Visibility = Visibility.Collapsed;
                OnPropertyChanged(nameof(ClearButtonVisibility));
                return;
            }

            NoDefaultsMessage.Visibility = Visibility.Collapsed;
            DefaultProfileGrid.Visibility = Visibility.Visible;
            OnPropertyChanged(nameof(ClearButtonVisibility));

            // TDP
            if (DefaultTdpValue != null)
            {
                DefaultTdpValue.Text = $"{defaults.TdpWatts}W";
            }

            // Resolution
            if (DefaultResolutionValue != null)
            {
                DefaultResolutionValue.Text = $"{defaults.ResolutionWidth}x{defaults.ResolutionHeight}";
            }

            // Refresh Rate
            if (DefaultRefreshRateValue != null)
            {
                DefaultRefreshRateValue.Text = $"{defaults.RefreshRateHz}Hz";
            }

            // FPS Limit
            if (DefaultFpsLimitValue != null)
            {
                DefaultFpsLimitValue.Text = defaults.FpsLimit > 0 ? $"{defaults.FpsLimit} FPS" : "Unlimited";
            }

            // AMD Feature Badges
            var enabledBg = new SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
            var disabledBg = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 51, 51, 51));
            var enabledFg = new SolidColorBrush(Microsoft.UI.Colors.White);
            var disabledFg = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 136, 136, 136));

            if (RsrBadge != null)
            {
                RsrBadge.Background = defaults.RsrEnabled ? enabledBg : disabledBg;
                if (RsrBadge.Child is TextBlock rsrText)
                {
                    rsrText.Foreground = defaults.RsrEnabled ? enabledFg : disabledFg;
                }
            }

            if (AfmfBadge != null)
            {
                AfmfBadge.Background = defaults.AfmfEnabled ? enabledBg : disabledBg;
                if (AfmfBadge.Child is TextBlock afmfText)
                {
                    afmfText.Foreground = defaults.AfmfEnabled ? enabledFg : disabledFg;
                }
            }

            if (AntiLagBadge != null)
            {
                AntiLagBadge.Background = defaults.AntiLagEnabled ? enabledBg : disabledBg;
                if (AntiLagBadge.Child is TextBlock antiLagText)
                {
                    antiLagText.Foreground = defaults.AntiLagEnabled ? enabledFg : disabledFg;
                }
            }

            // Fan Curve
            if (DefaultFanCurveValue != null)
            {
                var fanText = defaults.FanCurveEnabled ? defaults.FanCurvePreset : "Disabled";
                DefaultFanCurveValue.Text = fanText;
            }

            // Ensure focus is on a valid element
            if (_currentFocusedElement > MaxFocusIndex)
            {
                _currentFocusedElement = MaxFocusIndex;
            }
            UpdateFocusVisuals();
        }

        private async void SaveDefaultsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as Button;
                if (button != null)
                {
                    button.IsEnabled = false;
                    button.Content = "Saving...";
                }

                // Get or create GameProfileService
                if (_gameProfileService == null)
                {
                    var app = Application.Current as App;
                    var mainWindow = app?.MainWindow;
                    if (mainWindow != null)
                    {
                        var field = typeof(MainWindow).GetField("_gameProfileService",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        _gameProfileService = field?.GetValue(mainWindow) as GameProfileService;
                    }
                }

                if (_gameProfileService != null)
                {
                    var defaults = await _gameProfileService.CaptureCurrentSettingsAsync();
                    SettingsService.SetDefaultProfile(defaults);
                    UpdateDefaultProfileSummary();
                    DefaultsSaved?.Invoke(this, EventArgs.Empty);
                    System.Diagnostics.Debug.WriteLine("Default profile saved successfully");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Cannot save defaults - GameProfileService not available");
                }

                if (button != null)
                {
                    button.Content = "Saved!";
                    await Task.Delay(1500);
                    button.Content = "Save Defaults";
                    button.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving default profile: {ex.Message}");

                var button = sender as Button;
                if (button != null)
                {
                    button.Content = "Error - try again";
                    await Task.Delay(2000);
                    button.Content = "Save Defaults";
                    button.IsEnabled = true;
                }
            }
        }

        private async void ClearDefaultsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as Button;
                if (button != null)
                {
                    button.IsEnabled = false;
                    button.Content = "Clearing...";
                }

                SettingsService.ClearDefaultProfile();
                UpdateDefaultProfileSummary();
                DefaultsCleared?.Invoke(this, EventArgs.Empty);

                // Reset focus to Save button since Clear is now hidden
                _currentFocusedElement = 0;
                UpdateFocusVisuals();

                System.Diagnostics.Debug.WriteLine("Default profile cleared successfully");

                if (button != null)
                {
                    button.Content = "Cleared!";
                    await Task.Delay(1000);
                    button.Content = "Clear Defaults";
                    button.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing default profile: {ex.Message}");

                var button = sender as Button;
                if (button != null)
                {
                    button.Content = "Error";
                    await Task.Delay(1500);
                    button.Content = "Clear Defaults";
                    button.IsEnabled = true;
                }
            }
        }

        // IGamepadNavigable event handlers
        public void OnGamepadNavigateUp()
        {
            // No vertical navigation within this control
        }

        public void OnGamepadNavigateDown()
        {
            // No vertical navigation within this control
        }

        public void OnGamepadNavigateLeft()
        {
            if (_currentFocusedElement > 0)
            {
                _currentFocusedElement--;
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 DefaultProfile: Moved left to element {_currentFocusedElement}");
            }
        }

        public void OnGamepadNavigateRight()
        {
            if (_currentFocusedElement < MaxFocusIndex)
            {
                _currentFocusedElement++;
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 DefaultProfile: Moved right to element {_currentFocusedElement}");
            }
        }

        public void OnGamepadActivate()
        {
            if (_currentFocusedElement == 0)
            {
                // Activate Save button
                SaveDefaultsButton_Click(SaveDefaultsButton, new RoutedEventArgs());
                System.Diagnostics.Debug.WriteLine($"🎮 DefaultProfile: Activated Save button");
            }
            else if (_currentFocusedElement == 1 && _hasDefaults)
            {
                // Activate Clear button
                ClearDefaultsButton_Click(ClearDefaultsButton, new RoutedEventArgs());
                System.Diagnostics.Debug.WriteLine($"🎮 DefaultProfile: Activated Clear button");
            }
        }

        public void OnGamepadBack() { }

        public void OnGamepadFocusReceived()
        {
            if (_gamepadNavigationService == null)
            {
                InitializeGamepadNavigationService();
            }

            _currentFocusedElement = 0; // Start with Save button
            _isFocused = true;
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 DefaultProfile: Received gamepad focus");
        }

        public void OnGamepadFocusLost()
        {
            _isFocused = false;
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 DefaultProfile: Lost gamepad focus");
        }

        public void FocusLastElement()
        {
            _currentFocusedElement = MaxFocusIndex;
            _isFocused = true;
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 DefaultProfile: Focused last element");
        }

        public void AdjustSliderValue(int direction)
        {
            // No sliders in this control
        }

        private void UpdateFocusVisuals()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                OnPropertyChanged(nameof(SaveButtonFocusBrush));
                OnPropertyChanged(nameof(ClearButtonFocusBrush));
            });
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
