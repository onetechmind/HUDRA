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
using Windows.System;

namespace HUDRA.Controls
{
    public sealed partial class GameDetectionControl : UserControl, INotifyPropertyChanged, IGamepadNavigable
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private GamepadNavigationService? _gamepadNavigationService;
        private int _currentFocusedElement = 0; // 0=LibraryScanning, 1=ScanInterval, 2=ResetButton, 3=SgdbApiKey, 4=ClearKeyButton, 5=SgdbLinkButton
        private bool _isFocused = false;

        // SGDB API key management
        private readonly SecureStorageService _secureStorage = new();
        private bool _hasApiKey = false;
        private bool _isKeyValid = false;

        // IGamepadNavigable implementation
        public bool CanNavigateUp => _currentFocusedElement > 0;
        public bool CanNavigateDown => _currentFocusedElement < GetMaxNavigableElement();
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

        public Brush AddManualGameButtonFocusBrush
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

        public Brush RefreshButtonFocusBrush
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
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true && _currentFocusedElement == 2)
                {
                    return new SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
                }
                return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        public Brush SgdbApiKeyFocusBrush
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

        public Brush ClearKeyButtonFocusBrush
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

        public Brush SgdbLinkButtonFocusBrush
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

        // Launcher count properties for visual library stats
        private int _battleNetCount;
        public int BattleNetCount
        {
            get => _battleNetCount;
            set
            {
                if (_battleNetCount != value)
                {
                    _battleNetCount = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _epicCount;
        public int EpicCount
        {
            get => _epicCount;
            set
            {
                if (_epicCount != value)
                {
                    _epicCount = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _gogCount;
        public int GOGCount
        {
            get => _gogCount;
            set
            {
                if (_gogCount != value)
                {
                    _gogCount = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _originCount;
        public int OriginCount
        {
            get => _originCount;
            set
            {
                if (_originCount != value)
                {
                    _originCount = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _riotCount;
        public int RiotCount
        {
            get => _riotCount;
            set
            {
                if (_riotCount != value)
                {
                    _riotCount = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _rockstarCount;
        public int RockstarCount
        {
            get => _rockstarCount;
            set
            {
                if (_rockstarCount != value)
                {
                    _rockstarCount = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _steamCount;
        public int SteamCount
        {
            get => _steamCount;
            set
            {
                if (_steamCount != value)
                {
                    _steamCount = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _ubisoftCount;
        public int UbisoftCount
        {
            get => _ubisoftCount;
            set
            {
                if (_ubisoftCount != value)
                {
                    _ubisoftCount = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _xboxCount;
        public int XboxCount
        {
            get => _xboxCount;
            set
            {
                if (_xboxCount != value)
                {
                    _xboxCount = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _manualCount;
        public int ManualCount
        {
            get => _manualCount;
            set
            {
                if (_manualCount != value)
                {
                    _manualCount = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _lastUpdatedText = "Last updated: Never";
        public string LastUpdatedText
        {
            get => _lastUpdatedText;
            set
            {
                if (_lastUpdatedText != value)
                {
                    _lastUpdatedText = value;
                    OnPropertyChanged();
                }
            }
        }

        public GameDetectionControl()
        {
            this.InitializeComponent();
            this.DataContext = this;
            InitializeGamepadNavigation();

            // Wire up SGDB events
            this.Loaded += GameDetectionControl_Loaded;
            SgdbApiKeyBox.LostFocus += SgdbApiKeyBox_LostFocus;
            ClearKeyButton.Click += ClearKeyButton_Click;
            SgdbLinkButton.Click += SgdbLinkButton_Click;
        }

        private async void GameDetectionControl_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadSgdbKeyStateAsync();
        }

        private async Task LoadSgdbKeyStateAsync()
        {
            try
            {
                _hasApiKey = await _secureStorage.HasApiKeyAsync();

                DispatcherQueue.TryEnqueue(() =>
                {
                    if (_hasApiKey)
                    {
                        SgdbApiKeyBox.Password = "********************************"; // Masked placeholder
                        KeyValidationIcon.Glyph = "\uE73E"; // Checkmark
                        KeyValidationIcon.Foreground = new SolidColorBrush(Microsoft.UI.Colors.LimeGreen);
                        KeyValidationIcon.Visibility = Visibility.Visible;
                        ClearKeyButtonBorder.Visibility = Visibility.Visible;
                        _isKeyValid = true;
                    }
                    else
                    {
                        SgdbApiKeyBox.Password = "";
                        KeyValidationIcon.Visibility = Visibility.Collapsed;
                        ClearKeyButtonBorder.Visibility = Visibility.Collapsed;
                        _isKeyValid = false;
                    }

                    UpdateFocusVisuals();
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading SGDB key state: {ex.Message}");
            }
        }

        private async void SgdbApiKeyBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var key = SgdbApiKeyBox.Password;

            // Skip if it's the masked placeholder or empty
            if (key == "********************************" || string.IsNullOrEmpty(key))
                return;

            // Trim whitespace/newlines that may have been pasted
            key = key.Trim().Replace("\r", "").Replace("\n", "");

            if (_secureStorage.ValidateKeyFormat(key))
            {
                try
                {
                    await _secureStorage.SaveApiKeyAsync(key);
                    _hasApiKey = true;
                    _isKeyValid = true;

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        SgdbApiKeyBox.Password = "********************************"; // Show masked version
                        KeyValidationIcon.Glyph = "\uE73E"; // Checkmark
                        KeyValidationIcon.Foreground = new SolidColorBrush(Microsoft.UI.Colors.LimeGreen);
                        KeyValidationIcon.Visibility = Visibility.Visible;
                        ClearKeyButtonBorder.Visibility = Visibility.Visible;
                        UpdateFocusVisuals();
                    });

                    // Reinitialize artwork service with the new key and trigger a rescan
                    await ReinitializeArtworkServiceAsync();
                    await TriggerLibraryRescanAsync();

                    System.Diagnostics.Debug.WriteLine("SGDB API key saved successfully - library rescan triggered");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error saving SGDB API key: {ex.Message}");
                    ShowKeyValidationError();
                }
            }
            else
            {
                ShowKeyValidationError();
            }
        }

        private void ShowKeyValidationError()
        {
            _isKeyValid = false;

            DispatcherQueue.TryEnqueue(() =>
            {
                KeyValidationIcon.Glyph = "\uE711"; // X/Error
                KeyValidationIcon.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red);
                KeyValidationIcon.Visibility = Visibility.Visible;
                ClearKeyButtonBorder.Visibility = Visibility.Collapsed;
            });
        }

        private async void ClearKeyButton_Click(object sender, RoutedEventArgs e)
        {
            await _secureStorage.DeleteApiKeyAsync();
            _hasApiKey = false;
            _isKeyValid = false;

            DispatcherQueue.TryEnqueue(() =>
            {
                SgdbApiKeyBox.Password = "";
                KeyValidationIcon.Visibility = Visibility.Collapsed;
                ClearKeyButtonBorder.Visibility = Visibility.Collapsed;
                UpdateFocusVisuals();

                // Move focus to PasswordBox since Clear button is now hidden
                SgdbApiKeyBox.Focus(FocusState.Programmatic);
            });

            // Disable artwork service (pass null to clear it)
            await ReinitializeArtworkServiceAsync();

            System.Diagnostics.Debug.WriteLine("SGDB API key cleared");
        }

        private async void SgdbLinkButton_Click(object sender, RoutedEventArgs e)
        {
            await Launcher.LaunchUriAsync(new Uri("https://www.steamgriddb.com/profile/preferences/api"));
        }

        private async Task ReinitializeArtworkServiceAsync()
        {
            try
            {
                if (Application.Current is App app && app.MainWindow is MainWindow mainWindow)
                {
                    await mainWindow.ReinitializeArtworkServiceAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reinitializing artwork service: {ex.Message}");
            }
        }

        private async Task TriggerLibraryRescanAsync()
        {
            try
            {
                // Only trigger rescan if Library Scanning is enabled
                if (!SettingsService.IsEnhancedLibraryScanningEnabled())
                {
                    System.Diagnostics.Debug.WriteLine("GameDetectionControl: Rescan skipped - Library Scanning is disabled");
                    return;
                }

                if (Application.Current is App app && app.MainWindow is MainWindow mainWindow)
                {
                    var gameDetectionService = mainWindow.EnhancedGameDetectionService;
                    if (gameDetectionService != null)
                    {
                        // Trigger a full library rescan to re-download artwork with new API key
                        await gameDetectionService.RescanLibraryAsync();
                        System.Diagnostics.Debug.WriteLine("GameDetectionControl: Library rescan completed");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error triggering library rescan: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the maximum navigable element index based on current state.
        /// When no key is saved, Clear button (4) is hidden, so skip from ApiKey (3) to Link (5).
        /// </summary>
        private int GetMaxNavigableElement()
        {
            return 5; // SgdbLinkButton is always the last
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
                int previousElement = _currentFocusedElement - 1;

                // Skip ScanInterval ComboBox if library scanning is disabled
                if (previousElement == 1 && (!EnhancedLibraryScanningToggle?.IsOn ?? false))
                {
                    previousElement = 0; // Jump directly to LibraryScanning
                }

                // Skip Clear button if no key is saved
                if (previousElement == 4 && !_hasApiKey)
                {
                    previousElement = 3; // Jump to ApiKey
                }

                _currentFocusedElement = previousElement;
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 GameDetection: Moved up to element {_currentFocusedElement}");
            }
        }

        public void OnGamepadNavigateDown()
        {
            if (_currentFocusedElement < GetMaxNavigableElement())
            {
                int nextElement = _currentFocusedElement + 1;

                // Skip ScanInterval ComboBox if library scanning is disabled
                if (_currentFocusedElement == 0 && (!EnhancedLibraryScanningToggle?.IsOn ?? false))
                {
                    nextElement = 2; // Jump to ResetButton
                }

                // Skip Clear button if no key is saved
                if (nextElement == 4 && !_hasApiKey)
                {
                    nextElement = 5; // Jump to SgdbLinkButton
                }

                _currentFocusedElement = nextElement;
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

                case 2: // ResetDatabaseButton
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

                case 3: // SgdbApiKeyBox
                    // Focus the PasswordBox to allow keyboard input
                    SgdbApiKeyBox?.Focus(FocusState.Programmatic);
                    System.Diagnostics.Debug.WriteLine($"🎮 GameDetection: Focused API key box");
                    break;

                case 4: // ClearKeyButton
                    if (ClearKeyButton != null && _hasApiKey)
                    {
                        var clearPeer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(ClearKeyButton);
                        var clearInvokeProv = clearPeer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)
                            as Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider;
                        clearInvokeProv?.Invoke();
                        System.Diagnostics.Debug.WriteLine($"🎮 GameDetection: Activated Clear Key button");
                    }
                    break;

                case 5: // SgdbLinkButton
                    SgdbLinkButton_Click(SgdbLinkButton, new RoutedEventArgs());
                    System.Diagnostics.Debug.WriteLine($"🎮 GameDetection: Activated SGDB Link button");
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
            // Focus the last element (element 5: SGDB Link button)
            _currentFocusedElement = 5;
            _isFocused = true;
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 GameDetection: Focused last element (SGDB Link button)");
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
                OnPropertyChanged(nameof(AddManualGameButtonFocusBrush));
                OnPropertyChanged(nameof(SgdbApiKeyFocusBrush));
                OnPropertyChanged(nameof(ClearKeyButtonFocusBrush));
                OnPropertyChanged(nameof(SgdbLinkButtonFocusBrush));
            });
        }

        private async void AddManualGameButton_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("Add Manual Game button clicked");

            // Get the parent SettingsPage to call the dialog handler
            if (Application.Current is App app && app.MainWindow is MainWindow mainWindow)
            {
                // Find SettingsPage in the navigation
                var settingsPageField = typeof(MainWindow).GetField("_settingsPage",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (settingsPageField?.GetValue(mainWindow) is SettingsPage settingsPage)
                {
                    // Call the method to show the add manual game dialog
                    var showDialogMethod = typeof(SettingsPage).GetMethod("ShowAddManualGameDialog",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    if (showDialogMethod != null)
                    {
                        await (System.Threading.Tasks.Task)showDialogMethod.Invoke(settingsPage, null);
                    }
                }
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}