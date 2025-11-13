using HUDRA.Interfaces;
using HUDRA.AttachedProperties;
using HUDRA.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HUDRA.Controls
{
    public sealed partial class AmdFeaturesControl : UserControl, INotifyPropertyChanged, IGamepadNavigable
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private GamepadNavigationService? _gamepadNavigationService;
        private int _currentFocusedElement = 0; // 0=RSR Toggle
        private bool _isFocused = false;

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
                if (_rsrEnabled != value)
                {
                    _rsrEnabled = value;
                    OnPropertyChanged();
                    // TODO: Apply RSR settings when toggled
                    System.Diagnostics.Debug.WriteLine($"🎮 AmdFeatures: RSR toggled to {value}");
                }
            }
        }

        public AmdFeaturesControl()
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
    }
}
