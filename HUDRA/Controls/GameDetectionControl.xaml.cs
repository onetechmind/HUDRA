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
    public sealed partial class GameDetectionControl : UserControl, INotifyPropertyChanged, IGamepadNavigable
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private GamepadNavigationService? _gamepadNavigationService;
        private int _currentFocusedElement = 0; // 0=LibraryScanning, 1=ScanInterval, 2=RefreshButton, 3=ResetButton
        private bool _isFocused = false;

        // IGamepadNavigable implementation
        public bool CanNavigateUp => _currentFocusedElement > 0;
        public bool CanNavigateDown => _currentFocusedElement < 3;
        public bool CanNavigateLeft => false;
        public bool CanNavigateRight => false;
        public bool CanActivate => true;
        public FrameworkElement NavigationElement => this;

        // Slider interface implementations - GameDetection has no sliders
        public bool IsSlider => false;
        public bool IsSliderActivated { get; set; } = false;

        // ComboBox interface implementations
        public bool HasComboBoxes => true;
        public bool IsComboBoxOpen { get; set; } = false;
        public ComboBox? GetFocusedComboBox() => _currentFocusedElement == 1 ? ScanIntervalComboBox : null;
        public int ComboBoxOriginalIndex { get; set; } = -1;
        public bool IsNavigatingComboBox { get; set; } = false;

        public void ProcessCurrentSelection()
        {
            if (_currentFocusedElement == 1 && ScanIntervalComboBox != null)
            {
                // ComboBox selection will be handled by the parent SettingsPage event handlers
                System.Diagnostics.Debug.WriteLine($"🎮 GameDetection: ComboBox selection processed");
            }
        }

        // Focus brush properties for XAML binding
        public Brush LibraryScanningFocusBrush
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

        public Brush ScanIntervalFocusBrush
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true && _currentFocusedElement == 1)
                {
                    return new SolidColorBrush(IsComboBoxOpen ? Microsoft.UI.Colors.DodgerBlue : Microsoft.UI.Colors.DarkViolet);
                }
                return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        public Brush RefreshButtonFocusBrush
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

        public Brush ResetButtonFocusBrush
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

        public GameDetectionControl()
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
                // Skip ScanInterval ComboBox and buttons if library scanning is disabled
                if ((_currentFocusedElement == 2 || _currentFocusedElement == 3) && (!EnhancedLibraryScanningToggle?.IsOn ?? false))
                {
                    _currentFocusedElement = 0; // Jump directly to LibraryScanning
                }
                else
                {
                    _currentFocusedElement--;
                }
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 GameDetection: Moved up to element {_currentFocusedElement}");
            }
        }

        public void OnGamepadNavigateDown()
        {
            if (_currentFocusedElement < 3)
            {
                // Skip ScanInterval ComboBox if library scanning is disabled
                if (_currentFocusedElement == 0 && (!EnhancedLibraryScanningToggle?.IsOn ?? false))
                {
                    _currentFocusedElement = 2; // Jump directly to RefreshButton
                }
                else
                {
                    _currentFocusedElement++;
                }
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 GameDetection: Moved down to element {_currentFocusedElement}");
            }
        }

        public void OnGamepadNavigateLeft()
        {
            // Handle ComboBox navigation
            if (_currentFocusedElement == 1 && IsComboBoxOpen && ScanIntervalComboBox != null)
            {
                var currentIndex = ScanIntervalComboBox.SelectedIndex;
                if (currentIndex > 0)
                {
                    ScanIntervalComboBox.SelectedIndex = currentIndex - 1;
                    System.Diagnostics.Debug.WriteLine($"🎮 GameDetection: ComboBox moved to index {ScanIntervalComboBox.SelectedIndex}");
                }
            }
        }

        public void OnGamepadNavigateRight()
        {
            // Handle ComboBox navigation
            if (_currentFocusedElement == 1 && IsComboBoxOpen && ScanIntervalComboBox != null)
            {
                var currentIndex = ScanIntervalComboBox.SelectedIndex;
                if (currentIndex < ScanIntervalComboBox.Items.Count - 1)
                {
                    ScanIntervalComboBox.SelectedIndex = currentIndex + 1;
                    System.Diagnostics.Debug.WriteLine($"🎮 GameDetection: ComboBox moved to index {ScanIntervalComboBox.SelectedIndex}");
                }
            }
        }

        public void OnGamepadActivate()
        {
            switch (_currentFocusedElement)
            {
                case 0: // EnhancedLibraryScanningToggle
                    if (EnhancedLibraryScanningToggle != null)
                    {
                        EnhancedLibraryScanningToggle.IsOn = !EnhancedLibraryScanningToggle.IsOn;
                        System.Diagnostics.Debug.WriteLine($"🎮 GameDetection: Toggled LibraryScanning to {EnhancedLibraryScanningToggle.IsOn}");
                    }
                    break;

                case 1: // ScanIntervalComboBox
                    if (ScanIntervalComboBox != null && ScanIntervalComboBox.IsEnabled)
                    {
                        if (!IsComboBoxOpen)
                        {
                            // Open ComboBox
                            ComboBoxOriginalIndex = ScanIntervalComboBox.SelectedIndex;
                            ScanIntervalComboBox.IsDropDownOpen = true;
                            IsComboBoxOpen = true;
                            IsNavigatingComboBox = true;
                            UpdateFocusVisuals();
                            System.Diagnostics.Debug.WriteLine($"🎮 GameDetection: Opened ComboBox");
                        }
                        else
                        {
                            // Close ComboBox and apply selection
                            ScanIntervalComboBox.IsDropDownOpen = false;
                            IsComboBoxOpen = false;
                            IsNavigatingComboBox = false;
                            ProcessCurrentSelection();
                            UpdateFocusVisuals();
                            System.Diagnostics.Debug.WriteLine($"🎮 GameDetection: Closed ComboBox and applied selection");
                        }
                    }
                    break;

                case 2: // RefreshDatabaseButton
                    if (RefreshDatabaseButton != null && RefreshDatabaseButton.IsEnabled)
                    {
                        // Programmatically invoke the button's Click event using automation peer
                        var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(RefreshDatabaseButton);
                        var invokeProv = peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)
                            as Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider;
                        invokeProv?.Invoke();
                        System.Diagnostics.Debug.WriteLine($"🎮 GameDetection: Activated Refresh button");
                    }
                    break;

                case 3: // ResetDatabaseButton
                    if (ResetDatabaseButton != null && ResetDatabaseButton.IsEnabled)
                    {
                        // Programmatically invoke the button's Click event using automation peer
                        var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(ResetDatabaseButton);
                        var invokeProv = peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)
                            as Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider;
                        invokeProv?.Invoke();
                        System.Diagnostics.Debug.WriteLine($"🎮 GameDetection: Activated Reset button");
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

            _currentFocusedElement = 0; // Start with LibraryScanning toggle
            _isFocused = true;
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 GameDetection: Received gamepad focus");
        }

        public void OnGamepadFocusLost()
        {
            _isFocused = false;
            IsComboBoxOpen = false;
            IsNavigatingComboBox = false;
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 GameDetection: Lost gamepad focus");
        }

        public void FocusLastElement()
        {
            // Focus the last element (element 3: Display notification toggle)
            _currentFocusedElement = 3;
            _isFocused = true;
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 GameDetection: Focused last element (Display notification)");
        }

        public void AdjustSliderValue(int direction)
        {
            // No sliders in GameDetection control
        }

        private void UpdateFocusVisuals()
        {
            // Dispatch on UI thread to ensure bindings update reliably with gamepad navigation
            DispatcherQueue.TryEnqueue(() =>
            {
                OnPropertyChanged(nameof(LibraryScanningFocusBrush));
                OnPropertyChanged(nameof(ScanIntervalFocusBrush));
                OnPropertyChanged(nameof(RefreshButtonFocusBrush));
                OnPropertyChanged(nameof(ResetButtonFocusBrush));
            });
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}