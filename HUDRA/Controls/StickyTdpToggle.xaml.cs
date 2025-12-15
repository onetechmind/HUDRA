using HUDRA.Interfaces;
using HUDRA.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HUDRA.Controls
{
    public sealed partial class StickyTdpToggle : UserControl, INotifyPropertyChanged, IGamepadNavigable
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<bool>? StickyTdpChanged;

        private GamepadNavigationService? _gamepadNavigationService;
        private bool _isFocused = false;
        private bool _suppressEvents = false;

        // IGamepadNavigable implementation
        public bool CanNavigateUp => false;
        public bool CanNavigateDown => false;
        public bool CanNavigateLeft => false;
        public bool CanNavigateRight => false;
        public bool CanActivate => true;
        public FrameworkElement NavigationElement => this;

        // Not a slider
        public bool IsSlider => false;
        public bool IsSliderActivated { get; set; } = false;
        public void AdjustSliderValue(int direction) { }

        // No ComboBoxes
        public bool HasComboBoxes => false;
        public bool IsComboBoxOpen { get; set; } = false;
        public ComboBox? GetFocusedComboBox() => null;
        public int ComboBoxOriginalIndex { get; set; } = -1;
        public bool IsNavigatingComboBox { get; set; } = false;
        public void ProcessCurrentSelection() { }

        public Brush FocusBorderBrush
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true)
                {
                    return new SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
                }
                return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        public StickyTdpToggle()
        {
            this.InitializeComponent();
            this.Loaded += StickyTdpToggle_Loaded;
        }

        private void StickyTdpToggle_Loaded(object sender, RoutedEventArgs e)
        {
            Initialize();
        }

        public void Initialize()
        {
            // Get gamepad service
            if (Application.Current is App app && app.MainWindow is MainWindow mainWindow)
            {
                _gamepadNavigationService = mainWindow.GamepadNavigationService;
            }

            // Load current state
            _suppressEvents = true;
            StickyToggle.IsOn = SettingsService.GetTdpCorrectionEnabled();
            _suppressEvents = false;
        }

        /// <summary>
        /// Updates the toggle state without triggering the Toggled event.
        /// Used when syncing UI after Default Profile is applied.
        /// </summary>
        public void UpdateToggleState(bool isEnabled)
        {
            _suppressEvents = true;
            StickyToggle.IsOn = isEnabled;
            _suppressEvents = false;
        }

        private void StickyToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents) return;

            bool isEnabled = StickyToggle.IsOn;
            SettingsService.SetTdpCorrectionEnabled(isEnabled);

            // Start/stop TDP monitor via MainWindow
            if (Application.Current is App app && app.MainWindow is MainWindow mainWindow)
            {
                if (isEnabled)
                {
                    mainWindow.StartTdpMonitor();
                }
                else
                {
                    mainWindow.StopTdpMonitor();
                }
            }

            StickyTdpChanged?.Invoke(this, isEnabled);
            System.Diagnostics.Debug.WriteLine($"Sticky TDP {(isEnabled ? "enabled" : "disabled")}");
        }

        // IGamepadNavigable event handlers
        public void OnGamepadNavigateUp() { }
        public void OnGamepadNavigateDown() { }
        public void OnGamepadNavigateLeft() { }
        public void OnGamepadNavigateRight() { }

        public void OnGamepadActivate()
        {
            // Toggle the switch
            StickyToggle.IsOn = !StickyToggle.IsOn;
            System.Diagnostics.Debug.WriteLine($"🎮 StickyTdp: Toggled via gamepad to {StickyToggle.IsOn}");
        }

        public void OnGamepadBack() { }

        public void OnGamepadFocusReceived()
        {
            _isFocused = true;
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine("🎮 StickyTdp: Received gamepad focus");
        }

        public void OnGamepadFocusLost()
        {
            _isFocused = false;
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine("🎮 StickyTdp: Lost gamepad focus");
        }

        public void FocusLastElement() { }

        private void UpdateFocusVisuals()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                OnPropertyChanged(nameof(FocusBorderBrush));
            });
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
