using HUDRA.Configuration;
using HUDRA.Controls;
using HUDRA.Extensions;
using HUDRA.Models;
using HUDRA.Pages;
using HUDRA.Services;
using HUDRA.Services.Power;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WinRT;
using WinRT.Interop;

namespace HUDRA
{
    public sealed partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly DpiScalingService _dpiService;
        private readonly WindowManagementService _windowManager;
        private readonly AudioService _audioService;
        private readonly BrightnessService _brightnessService;
        private readonly ResolutionService _resolutionService;
        private readonly NavigationService _navigationService;
        private readonly BatteryService _batteryService;
        private readonly PowerProfileService _powerProfileService;
        private readonly RtssFpsLimiterService _fpsLimiterService;
        private readonly GamepadNavigationService _gamepadNavigationService;
        private TdpMonitorService? _tdpMonitor;
        private TurboService? _turboService;
        private MicaController? _micaController;
        private SystemBackdropConfiguration? _backdropConfig;
        private EnhancedGameDetectionService? _enhancedGameDetectionService;
        private LosslessScalingService? _losslessScalingService;
        private EnhancedGameDatabase? _gameDatabase;
        private SteamGridDbArtworkService? _artworkService;


        // Public navigation service access for TDP picker
        public NavigationService NavigationService => _navigationService;

        // Public window manager access for App.xaml.c
        public WindowManagementService WindowManager => _windowManager;
        
        // Public gamepad navigation service access for controls
        public GamepadNavigationService GamepadNavigationService => _gamepadNavigationService;

        //Navigation events
        private bool _mainPageInitialized = false;
        // Tracks if the next page navigation was initiated via gamepad (L1/R1)
        private bool _isGamepadPageNavPending = false;
        // Latched flag for the page that just became active
        private bool _isGamepadNavForCurrentPage = false;

        // Current page references
        private MainPage? _mainPage;
        private SettingsPage? _settingsPage;
        private FanCurvePage? _fanCurvePage;
        private ScalingPage? _scalingPage;
        private LibraryPage? _libraryPage;
        private GameSettingsPage? _gameSettingsPage;

        //Drag Handling
        private bool _isDragging = false;
        private Windows.Graphics.PointInt32 _lastPointerPosition;
        private Windows.Foundation.Point _lastTouchPosition;
        private bool _touchDragStarted = false;

        // Navigation state for visual feedback
        private Type _currentPageType;

        // Store the actual TDP value across navigation
        private int _currentTdpValue = -1; // Initialize to -1 to indicate not set
        private bool _isFirstFpsInitialization = true;

        private string _batteryPercentageText = "0%";
        public string BatteryPercentageText
        {
            get => _batteryPercentageText;
            set { _batteryPercentageText = value; OnPropertyChanged(); }
        }

        private string _batteryToolTip = string.Empty;
        public string BatteryToolTip
        {
            get => _batteryToolTip;
            set { _batteryToolTip = value; OnPropertyChanged(); }
        }

        private SolidColorBrush _batteryTextBrush = new SolidColorBrush(Microsoft.UI.Colors.White);
        public SolidColorBrush BatteryTextBrush
        {
            get => _batteryTextBrush;
            set { _batteryTextBrush = value; OnPropertyChanged(); }
        }

        // Game library scanning progress properties
        private bool _isGameLibraryScanning = false;
        public bool IsGameLibraryScanning
        {
            get => _isGameLibraryScanning;
            set { _isGameLibraryScanning = value; OnPropertyChanged(); }
        }

        private string _scanProgressText = string.Empty;
        public string ScanProgressText
        {
            get => _scanProgressText;
            set { _scanProgressText = value; OnPropertyChanged(); }
        }

        // Power Profile properties
        private ObservableCollection<PowerProfile> _availablePowerProfiles = new();
        public ObservableCollection<PowerProfile> AvailablePowerProfiles
        {
            get => _availablePowerProfiles;
            set { _availablePowerProfiles = value; OnPropertyChanged(); }
        }

        private PowerProfile? _selectedPowerProfile;
        public PowerProfile? SelectedPowerProfile
        {
            get => _selectedPowerProfile;
            set
            {
                if (_selectedPowerProfile != value)
                {
                    _selectedPowerProfile = value;
                    OnPropertyChanged();
                    _ = OnPowerProfileSelectionChanged(value);
                }
            }
        }

        private bool _losslessScalingButtonVisible = false;
        public bool LosslessScalingButtonVisible
        {
            get => _losslessScalingButtonVisible;
            set { _losslessScalingButtonVisible = value; OnPropertyChanged(); }
        }

        private bool _forceQuitButtonVisible = false;
        public bool ForceQuitButtonVisible
        {
            get => _forceQuitButtonVisible;
            set { _forceQuitButtonVisible = value; OnPropertyChanged(); }
        }

        // FPS Limiter properties
        private FpsLimitSettings _fpsSettings = new();
        public FpsLimitSettings FpsSettings
        {
            get => _fpsSettings;
            set { _fpsSettings = value; OnPropertyChanged(); }
        }

        private bool _isRtssSupported = false;
        public bool IsRtssSupported
        {
            get => _isRtssSupported;
            set { _isRtssSupported = value; OnPropertyChanged(); }
        }

        public MainWindow()
        {
            this.InitializeComponent();
            this.Title = "HUDRA";
            LayoutRoot.DataContext = this;

            // Initialize services
            _dpiService = new DpiScalingService(this);
            _windowManager = new WindowManagementService(this, _dpiService);
            _audioService = new AudioService();
            _brightnessService = new BrightnessService();
            _resolutionService = new ResolutionService();
            _navigationService = new NavigationService(ContentFrame);
            _gamepadNavigationService = new GamepadNavigationService();
            _gamepadNavigationService.SetCurrentFrame(ContentFrame);
            _gamepadNavigationService.SetLayoutRoot(LayoutRoot);
            _batteryService = new BatteryService(DispatcherQueue);
            _powerProfileService = new PowerProfileService();
            _fpsLimiterService = new RtssFpsLimiterService();

            // Subscribe to navigation events
            _navigationService.PageChanged += OnPageChanged;
            _batteryService.BatteryInfoUpdated += OnBatteryInfoUpdated;
            _gamepadNavigationService.PageNavigationRequested += OnGamepadPageNavigationRequested;
            _gamepadNavigationService.NavbarButtonRequested += OnGamepadNavbarButtonRequested;

            InitializeWindow();
            SetupEventHandlers();
            SetupDragHandling();
            SetupInputDetection();

            _navigationService.NavigateToMain();

            // Register navbar buttons for spatial navigation
            RegisterNavbarButtons();

            this.Closed += (s, e) => Cleanup();
        }

        /// <summary>
        /// Refreshes the artwork for a specific game in the Library page.
        /// Called from GameSettingsPage after artwork is saved.
        /// </summary>
        /// <param name="processName">ProcessName of the game to refresh</param>
        public void RefreshLibraryGameArtwork(string processName)
        {
            if (_libraryPage != null)
            {
                _libraryPage.RefreshGameArtwork(processName);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"MainWindow: Cannot refresh game artwork - LibraryPage is not initialized");
            }
        }

        private void InitializeWindow()
        {
            TrySetMicaBackdrop();
            _windowManager.Initialize();
            InitializeGameDetection();
        }

        private void OnPageChanged(object sender, Type pageType)
        {
            // Latch and clear the pending gamepad navigation flag for this page
            _isGamepadNavForCurrentPage = _isGamepadPageNavPending;
            _isGamepadPageNavPending = false;

            // Save state and resume gamepad input processing when leaving Library page
            // (Library pauses it to allow native XYFocus to work)
            if (_currentPageType == typeof(LibraryPage) && pageType != typeof(LibraryPage))
            {
                // Save scroll position before leaving (OnNavigatedFrom may not fire due to page caching)
                _libraryPage?.SaveScrollPosition();

                _gamepadNavigationService.ResumeInputProcessing();
                System.Diagnostics.Debug.WriteLine("🎮 Left Library page - saved scroll position and resumed GamepadNavigationService");
            }

            _currentPageType = pageType;
            UpdateNavigationButtonStates();
            HandlePageSpecificInitialization(pageType);
        }

        private void OnGamepadPageNavigationRequested(object sender, GamepadPageNavigationEventArgs e)
        {
            // Mark that this navigation originated from gamepad buttons
            _isGamepadPageNavPending = true;

            // Define page order for navigation
            var pageOrder = new List<Type>
            {
                typeof(MainPage),
                typeof(FanCurvePage),
                typeof(ScalingPage),
                typeof(LibraryPage),
                typeof(SettingsPage)
            };

            // Find current page index
            int currentIndex = pageOrder.IndexOf(_currentPageType);
            if (currentIndex == -1) return; // Current page not in order, ignore

            // Calculate target page index with wrap-around
            int targetIndex;
            if (e.Direction == GamepadPageDirection.Previous)
            {
                targetIndex = currentIndex == 0 ? pageOrder.Count - 1 : currentIndex - 1;
            }
            else // Next
            {
                targetIndex = currentIndex == pageOrder.Count - 1 ? 0 : currentIndex + 1;
            }

            var targetPageType = pageOrder[targetIndex];

            // Navigate to target page using appropriate method
            if (targetPageType == typeof(MainPage))
                _navigationService.NavigateToMain();
            else if (targetPageType == typeof(FanCurvePage))
                _navigationService.NavigateToFanCurve();
            else if (targetPageType == typeof(ScalingPage))
                _navigationService.NavigateToScaling();
            else if (targetPageType == typeof(LibraryPage))
                _navigationService.NavigateToLibrary();
            else if (targetPageType == typeof(SettingsPage))
                _navigationService.NavigateToSettings();
        }

        private void OnGamepadNavbarButtonRequested(object sender, GamepadNavbarButtonEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"🎮 Navbar button requested: {e.Button}");

            switch (e.Button)
            {
                case GamepadNavbarButton.BackToGame:
                    // Only invoke if button is visible
                    if (AltTabButton.Visibility == Microsoft.UI.Xaml.Visibility.Visible)
                    {
                        AltTabButton_Click(AltTabButton, new RoutedEventArgs());
                    }
                    break;

                case GamepadNavbarButton.LosslessScaling:
                    // Only invoke if button is visible
                    if (LosslessScalingButton.Visibility == Microsoft.UI.Xaml.Visibility.Visible)
                    {
                        LosslessScalingButton_Click(LosslessScalingButton, new RoutedEventArgs());
                    }
                    break;
            }
        }

        private void RegisterNavbarButtons()
        {
            // Register navbar buttons in order: Back to Game, Force Quit, Lossless Scaling, Hide
            // This order matches the recommended top-to-bottom layout for spatial navigation
            var navbarButtons = new List<Button>
            {
                AltTabButton,          // Back to Game (top)
                ForceQuitButton,       // Force Quit
                LosslessScalingButton, // Lossless Scaling
                CloseButton            // Hide (bottom)
            };

            _gamepadNavigationService.RegisterNavbarButtons(navbarButtons);
            System.Diagnostics.Debug.WriteLine("🎮 Registered navbar buttons for spatial navigation");
        }

        private void HandlePageSpecificInitialization(Type pageType)
        {
            if (pageType == typeof(MainPage))
            {
                // Wait for navigation to complete then initialize
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (ContentFrame.Content is MainPage mainPage)
                    {
                        _mainPage = mainPage;
                        InitializeMainPage();
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("ERROR: ContentFrame.Content is not MainPage!");
                    }
                });
            }
            else if (pageType == typeof(SettingsPage))
            {
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (ContentFrame.Content is SettingsPage settingsPage)
                    {
                        _settingsPage = settingsPage;
                        InitializeSettingsPage();
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"ERROR: ContentFrame.Content is not SettingsPage! Type: {ContentFrame.Content?.GetType().Name ?? "null"}");
                    }
                });
            }
            else if (pageType == typeof(FanCurvePage))
            {
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (ContentFrame.Content is FanCurvePage fanCurvePage)
                    {
                        _fanCurvePage = fanCurvePage;
                        InitializeFanCurvePage();
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"ERROR: ContentFrame.Content is not FanCurvePage! Type: {ContentFrame.Content?.GetType().Name ?? "null"}");
                    }
                });
            }
            else if (pageType == typeof(ScalingPage))
            {
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (ContentFrame.Content is ScalingPage scalingPage)
                    {
                        _scalingPage = scalingPage;
                        InitializeScalingPage();
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"ERROR: ContentFrame.Content is not ScalingPage! Type: {ContentFrame.Content?.GetType().Name ?? "null"}");
                    }
                });
            }
            else if (pageType == typeof(LibraryPage))
            {
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (ContentFrame.Content is LibraryPage libraryPage)
                    {
                        _libraryPage = libraryPage;
                        InitializeLibraryPage();
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"ERROR: ContentFrame.Content is not LibraryPage! Type: {ContentFrame.Content?.GetType().Name ?? "null"}");
                    }
                });
            }
            else if (pageType == typeof(GameSettingsPage))
            {
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (ContentFrame.Content is GameSettingsPage gameSettingsPage)
                    {
                        _gameSettingsPage = gameSettingsPage;
                        InitializeGameSettingsPage();
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"ERROR: ContentFrame.Content is not GameSettingsPage! Type: {ContentFrame.Content?.GetType().Name ?? "null"}");
                    }
                });
            }
        }

        private void InitializeMainPage()
        {
            if (_mainPage == null) return;

            if (!_mainPageInitialized)
            {
                // First visit - full initialization
                _mainPage.Initialize(_dpiService, _resolutionService, _audioService, _brightnessService, _fpsLimiterService);
                _mainPageInitialized = true;

                // Set up TDP change tracking for the first time
                _mainPage.TdpPicker.TdpChanged += (s, value) =>
                {
                    _currentTdpValue = value;
                    System.Diagnostics.Debug.WriteLine($"Main TDP changed to: {value}");
                };

                // Store the initial TDP value after initialization completes
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    _currentTdpValue = _mainPage.TdpPicker.SelectedTdp;
                    System.Diagnostics.Debug.WriteLine($"Initial TDP value stored: {_currentTdpValue}");

                    // Initialize FPS limiter after MainPage is fully loaded
                    _ = InitializeFpsLimiterAsync();

                    // Auto-start RTSS if enabled
                    _ = StartRtssIfEnabledAsync();
                    
                    // Initialize gamepad navigation for MainPage
                    _gamepadNavigationService.InitializePageNavigation(_mainPage.RootPanel);
                });
            }
            else
            {
                // Subsequent visits - preserve current TDP value

                // Ensure we have a valid TDP value to preserve
                if (_currentTdpValue < HudraSettings.MIN_TDP || _currentTdpValue > HudraSettings.MAX_TDP)
                {
                    // Fallback to determining the correct TDP using startup logic
                    if (SettingsService.GetUseStartupTdp())
                    {
                        _currentTdpValue = SettingsService.GetStartupTdp();
                    }
                    else
                    {
                        _currentTdpValue = SettingsService.GetLastUsedTdp();
                        if (_currentTdpValue < HudraSettings.MIN_TDP || _currentTdpValue > HudraSettings.MAX_TDP)
                        {
                            _currentTdpValue = HudraSettings.DEFAULT_STARTUP_TDP;
                        }
                    }
                    System.Diagnostics.Debug.WriteLine($"Corrected TDP value to: {_currentTdpValue}");
                }

                // Initialize with preserved value flag
                _mainPage.TdpPicker.ResetScrollPositioning();
                _mainPage.TdpPicker.Initialize(_dpiService, autoSetEnabled: true, preserveCurrentValue: true);

                // CRITICAL: Set the TDP value AFTER initialization but BEFORE other controls
                _mainPage.TdpPicker.SelectedTdp = _currentTdpValue;

                // Initialize other controls
                _mainPage.ResolutionPicker.Initialize();
                _mainPage.AudioControls.Initialize();
                _mainPage.BrightnessControls.Initialize();

                // Refresh FPS limiter on subsequent visits
                _ = InitializeFpsLimiterAsync();
                
                // Re-initialize gamepad navigation for MainPage
                _gamepadNavigationService.InitializePageNavigation(_mainPage.RootPanel);

                // Re-establish TDP change tracking
                _mainPage.TdpPicker.TdpChanged += (s, value) =>
                {
                    _currentTdpValue = value;
                    System.Diagnostics.Debug.WriteLine($"Main TDP changed to: {value}");
                };

                // Ensure scroll positioning to the correct value
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    _mainPage.TdpPicker.EnsureScrollPositionAfterLayout();
                });
            }

            SetupTdpMonitor();
        }

        private void InitializeSettingsPage()
        {
            if (_settingsPage == null) return;

            try
            {
                _settingsPage.Initialize(_dpiService);
                _ = LoadPowerProfilesAsync();

                // Initialize gamepad navigation for SettingsPage
                if (_settingsPage?.RootPanel is FrameworkElement root)
                {
                    _gamepadNavigationService.InitializePageNavigation(root, isFromPageNavigation: _isGamepadNavForCurrentPage);
                }

                _isGamepadNavForCurrentPage = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR in InitializeSettingsPage: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private void InitializeFanCurvePage()
        {
            if (_fanCurvePage == null) return;

            try
            {
                _fanCurvePage.Initialize();
                
                // Add a small delay to ensure the control is fully loaded and gamepad navigation is set up
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High, () =>
                {
                    // If navigation did not originate from the gamepad, ensure gamepad mode is deactivated
                    // so we don't apply initial focus borders due to lingering IsGamepadActive state
                    if (!_isGamepadNavForCurrentPage && _gamepadNavigationService.IsGamepadActive)
                    {
                        _gamepadNavigationService.DeactivateGamepadMode();
                    }

                    // Initialize gamepad navigation for FanCurvePage only if navigation came from gamepad
                    _gamepadNavigationService.InitializePageNavigation(
                        _fanCurvePage.FanCurveControl,
                        isFromPageNavigation: _isGamepadNavForCurrentPage
                    );

                    // Reset flag after applying to avoid unintended focusing on subsequent navigations
                    _isGamepadNavForCurrentPage = false;
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR in InitializeFanCurvePage: {ex.Message}");
            }
        }

        private void InitializeScalingPage()
        {
            if (_scalingPage == null) return;

            System.Diagnostics.Debug.WriteLine("=== InitializeScalingPage called ===");

            try
            {
                _scalingPage.Initialize();
                System.Diagnostics.Debug.WriteLine("=== ScalingPage initialization complete ===");

                // After initialization, apply gamepad navigation focus consistent with current nav origin
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High, () =>
                {
                    if (!_isGamepadNavForCurrentPage && _gamepadNavigationService.IsGamepadActive)
                    {
                        _gamepadNavigationService.DeactivateGamepadMode();
                    }

                    if (_scalingPage?.RootPanel is FrameworkElement root)
                    {
                        _gamepadNavigationService.InitializePageNavigation(root, isFromPageNavigation: _isGamepadNavForCurrentPage);
                    }

                    _isGamepadNavForCurrentPage = false;
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR in InitializeScalingPage: {ex.Message}");
            }
        }

        private async void InitializeLibraryPage()
        {
            if (_libraryPage == null) return;

            System.Diagnostics.Debug.WriteLine("=== InitializeLibraryPage called ===");

            try
            {
                // Track if this navigation came from gamepad L1/R1
                bool wasGamepadNav = _isGamepadNavForCurrentPage;
                System.Diagnostics.Debug.WriteLine($"=== InitializeLibraryPage: wasGamepadNav={wasGamepadNav} ===");

                // AWAIT the async initialization
                await _libraryPage.Initialize(_enhancedGameDetectionService!, _gamepadNavigationService, ContentScrollViewer, wasGamepadNav);
                System.Diagnostics.Debug.WriteLine("=== LibraryPage initialization complete ===");

                // Library page uses custom D-pad navigation via GamepadNavigationService raw input forwarding
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High, () =>
                {
                    // Pause the gamepad navigation service to forward raw input to Library page
                    _gamepadNavigationService.PauseInputProcessing();

                    // Note: Focus is now handled in LibraryPage.Initialize() based on whether
                    // this navigation was via gamepad (L1/R1) or mouse/keyboard click

                    _isGamepadNavForCurrentPage = false;
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR in InitializeLibraryPage: {ex.Message}");
            }
        }

        private void InitializeGameSettingsPage()
        {
            if (_gameSettingsPage == null) return;

            System.Diagnostics.Debug.WriteLine("=== InitializeGameSettingsPage called ===");

            try
            {
                // Initialize the page with services
                if (_gameDatabase != null && _artworkService != null)
                {
                    _gameSettingsPage.Initialize(_gameDatabase, _artworkService);

                    // Load the game if one was selected
                    if (!string.IsNullOrEmpty(LibraryPage.SelectedGameProcessName))
                    {
                        _gameSettingsPage.LoadGame(LibraryPage.SelectedGameProcessName);
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("WARNING: GameDatabase or ArtworkService not available for GameSettingsPage");
                }

                System.Diagnostics.Debug.WriteLine("=== GameSettingsPage initialization complete ===");

                // Initialize gamepad navigation
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High, () =>
                {
                    if (!_isGamepadNavForCurrentPage && _gamepadNavigationService.IsGamepadActive)
                    {
                        _gamepadNavigationService.DeactivateGamepadMode();
                    }

                    // Initialize gamepad navigation for GameSettingsPage
                    _gamepadNavigationService.InitializePageNavigation(
                        _gameSettingsPage.BackButton,
                        isFromPageNavigation: _isGamepadNavForCurrentPage
                    );

                    _isGamepadNavForCurrentPage = false;
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR in InitializeGameSettingsPage: {ex.Message}");
            }
        }

        private void UpdateNavigationButtonStates()
        {
            // Update visual states of navigation buttons based on current page
            UpdateButtonState(MainPageNavButton, _currentPageType == typeof(MainPage));
            UpdateButtonState(FanCurveNavButton, _currentPageType == typeof(FanCurvePage));
            UpdateButtonState(ScalingNavButton, _currentPageType == typeof(ScalingPage));
            UpdateButtonState(LibraryNavButton, _currentPageType == typeof(LibraryPage));
            UpdateButtonState(SettingsNavButton, _currentPageType == typeof(SettingsPage));
        }

        private void UpdateButtonState(Button button, bool isActive)
        {
            if (button == null)
            {
                System.Diagnostics.Debug.WriteLine("UpdateButtonState: button is null");
                return;
            }

            var activeForeground = new SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
            var inactiveForeground = new SolidColorBrush(Microsoft.UI.Colors.White);

            // Handle FontIcon content (both buttons should now use FontIcon)
            if (button.Content is FontIcon icon)
            {
                icon.Foreground = isActive ? activeForeground : inactiveForeground;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"{button.Name} has unknown content type: {button.Content?.GetType().Name}");
                // Fallback for other content types
                button.Foreground = isActive ? activeForeground : inactiveForeground;
            }
        }

        // Navigation event handlers
        private void MainPageNavButton_Click(object sender, RoutedEventArgs e)
        {
            // Mouse/touch/keyboard nav: suppress auto-focus when gamepad wakes
            _gamepadNavigationService.SuppressAutoFocusOnNextActivation();
            _navigationService.NavigateToMain();
        }

        private void FanCurveNavButton_Click(object sender, RoutedEventArgs e)
        {
            // Mouse/touch/keyboard nav: suppress auto-focus when gamepad wakes
            _gamepadNavigationService.SuppressAutoFocusOnNextActivation();
            _navigationService.NavigateToFanCurve();
        }

        private void ScalingNavButton_Click(object sender, RoutedEventArgs e)
        {
            // Mouse/touch/keyboard nav: suppress auto-focus when gamepad wakes
            _gamepadNavigationService.SuppressAutoFocusOnNextActivation();
            _navigationService.NavigateToScaling();
        }

        private void LibraryNavButton_Click(object sender, RoutedEventArgs e)
        {
            // Mouse/touch/keyboard nav: suppress auto-focus when gamepad wakes
            _gamepadNavigationService.SuppressAutoFocusOnNextActivation();
            _navigationService.NavigateToLibrary();
        }

        private void SettingsNavButton_Click(object sender, RoutedEventArgs e)
        {
            // Mouse/touch/keyboard nav: suppress auto-focus when gamepad wakes
            _gamepadNavigationService.SuppressAutoFocusOnNextActivation();
            _navigationService.NavigateToSettings();
        }

        // Existing event handlers
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Clear gamepad focus to prevent lingering borders
            _gamepadNavigationService?.ClearFocus();
            _gamepadNavigationService?.DeactivateGamepadMode();

            _windowManager.ToggleVisibility();
        }

        private void AltTabButton_Click(object sender, RoutedEventArgs e)
        {
            // Clear gamepad focus to prevent lingering borders
            _gamepadNavigationService?.ClearFocus();
            _gamepadNavigationService?.DeactivateGamepadMode();

            _windowManager.ToggleVisibility();

            if (_enhancedGameDetectionService?.SwitchToGame() == true)
            {
                System.Diagnostics.Debug.WriteLine("Successfully switched to game");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Game switching failed, using generic Alt+Tab");
                SimulateAltTab();
            }
        }

        private async void ForceQuitButton_Click(object sender, RoutedEventArgs e)
        {
            if (_enhancedGameDetectionService?.CurrentGame == null)
            {
                System.Diagnostics.Debug.WriteLine("No active game to force quit");
                return;
            }

            var currentGame = _enhancedGameDetectionService.CurrentGame;
            string gameName = !string.IsNullOrWhiteSpace(currentGame.WindowTitle)
                ? currentGame.WindowTitle
                : currentGame.ProcessName;

            try
            {
                // Create confirmation dialog
                // Gamepad support: A button = Force Quit, B button = Cancel
                var dialog = new ContentDialog()
                {
                    Title = "Force Quit Game",
                    Content = $"Are you sure you want to force quit {gameName}?\n\n⚠️ Please save your game before proceeding to avoid losing progress.",
                    PrimaryButtonText = "Ⓐ Force Quit",
                    CloseButtonText = "Ⓑ Cancel",
                    DefaultButton = ContentDialogButton.Close, // B button (safer default)
                    XamlRoot = this.Content.XamlRoot,
                    MaxWidth = 448 // Match standard ContentDialog width
                };

                // Show dialog with automatic gamepad support
                var result = await dialog.ShowWithGamepadSupportAsync(_gamepadNavigationService);

                if (result != ContentDialogResult.Primary)
                {
                    System.Diagnostics.Debug.WriteLine("Force quit cancelled by user");
                    return;
                }

                // User confirmed - proceed with force quit
                System.Diagnostics.Debug.WriteLine($"Force quitting game: {gameName} (PID: {currentGame.ProcessId})");

                try
                {
                    var process = System.Diagnostics.Process.GetProcessById(currentGame.ProcessId);

                    // Try graceful close first
                    process.CloseMainWindow();

                    // Wait up to 3 seconds for graceful shutdown
                    bool exited = process.WaitForExit(3000);

                    if (!exited && !process.HasExited)
                    {
                        // Force kill if graceful didn't work
                        System.Diagnostics.Debug.WriteLine("Graceful close failed, forcing termination");
                        process.Kill();
                        process.WaitForExit(2000);
                    }

                    process.Dispose();
                    System.Diagnostics.Debug.WriteLine("Game successfully terminated");
                }
                catch (ArgumentException)
                {
                    System.Diagnostics.Debug.WriteLine("Process no longer exists");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error terminating game process: {ex.Message}");

                    // Create and show error dialog with automatic gamepad support
                    var errorDialog = new ContentDialog()
                    {
                        Title = "Force Quit Failed",
                        Content = $"Failed to terminate the game process.\n\nError: {ex.Message}",
                        CloseButtonText = "OK",
                        XamlRoot = this.Content.XamlRoot
                    };

                    await errorDialog.ShowWithGamepadSupportAsync(_gamepadNavigationService);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in force quit operation: {ex.Message}");
            }
        }

        private async void LosslessScalingButton_Click(object sender, RoutedEventArgs e)
        {
            if (_losslessScalingService == null || _enhancedGameDetectionService == null)
                return;

            try
            {
                // Clear gamepad focus to prevent lingering borders
                _gamepadNavigationService?.ClearFocus();
                _gamepadNavigationService?.DeactivateGamepadMode();

                // 1. Hide HUDRA
                _windowManager.ToggleVisibility();

                // 2. Switch to game
                if (_enhancedGameDetectionService.SwitchToGame() != true)
                {
                    // Game switching failed - show error and abort
                    System.Diagnostics.Debug.WriteLine("Game switching failed - aborting Lossless Scaling activation");
                    await ShowLosslessScalingError("Failed to switch to game window");
                    return;
                }

                // 3. Wait 500ms
                await Task.Delay(500);

                // 4. Execute Lossless Scaling shortcut
                var (hotkey, modifiers) = _losslessScalingService.ParseHotkeyFromSettings();
                bool success = await _losslessScalingService.ExecuteHotkeyAsync(hotkey, modifiers);

                if (!success)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to execute Lossless Scaling hotkey");
                    await ShowLosslessScalingError("Failed to execute Lossless Scaling shortcut");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Successfully executed Lossless Scaling hotkey: {modifiers}+{hotkey}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in Lossless Scaling activation: {ex.Message}");
                await ShowLosslessScalingError("An unexpected error occurred");
            }
        }







        private void SetupEventHandlers()
        {
            this.SizeChanged += MainWindow_SizeChanged;
        }

        private void SetupDragHandling()
        {
            // Set up the logo as the dedicated drag handle
            LogoDragHandle.PointerPressed += OnLogoDragHandlePointerPressed;
            LogoDragHandle.PointerMoved += OnLogoDragHandlePointerMoved;
            LogoDragHandle.PointerReleased += OnLogoDragHandlePointerReleased;

            // Add hover effects for visual feedback
            LogoDragHandle.PointerEntered += OnLogoPointerEntered;
            LogoDragHandle.PointerExited += OnLogoPointerExited;
        }

        private void SetupInputDetection()
        {
            // Detect non-gamepad input to clear gamepad focus
            LayoutRoot.PointerPressed += OnNonGamepadInput;  // Mouse clicks and touch
            LayoutRoot.Tapped += OnNonGamepadInput;          // Additional touch detection
            LayoutRoot.KeyDown += OnNonGamepadInput;         // Keyboard input
        }

        private void OnLogoDragHandlePointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            var pointer = e.Pointer;

            // Start drag for both mouse and touch
            if (pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse ||
                pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch)
            {
                var properties = e.GetCurrentPoint(LogoDragHandle).Properties;
                bool shouldStartDrag = (pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse && properties.IsLeftButtonPressed) ||
                                      (pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch);

                if (shouldStartDrag)
                {
                    StartWindowDrag(e, sender as FrameworkElement);
                }
            }
        }
        private void OnLogoDragHandlePointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (!_isDragging) return;

            if (_isDragging)
            {
                var properties = e.GetCurrentPoint(LogoDragHandle).Properties;
                bool shouldContinueDrag = (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse && properties.IsLeftButtonPressed) ||
                                         (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch);

                if (shouldContinueDrag)
                {
                    MoveWindow(e); // Remove the second parameter
                }
                else
                {
                    EndWindowDrag(e);
                }
            }
        }
        private void OnLogoDragHandlePointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_isDragging)
            {
                EndWindowDrag(e);
            }
        }

        private void OnLogoPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            // Visual feedback - slightly dim the logo on hover
            LogoDragHandle.Opacity = 0.8;
        }

        private void OnLogoPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            // Return to normal opacity
            if (!_isDragging)
            {
                LogoDragHandle.Opacity = 1.0;
            }
        }

        private void OnNonGamepadInput(object sender, object e)
        {
            // Clear gamepad focus when mouse/keyboard/touch is used
            if (_gamepadNavigationService?.IsGamepadActive == true)
            {
                // CRITICAL: Clear gamepad focus borders before deactivating
                // This prevents lingering gamepad focus from showing alongside keyboard focus
                _gamepadNavigationService.ClearFocus();
                _gamepadNavigationService.DeactivateGamepadMode();
                System.Diagnostics.Debug.WriteLine("🎮 Cleared gamepad focus due to mouse/keyboard/touch input");
            }
        }

        public void ConnectTurboService()
        {
            try
            {
                // Get the global TurboService instance from App
                var app = (App)Application.Current;
                _turboService = app.TurboService;
                
                if (_turboService != null)
                {
                    _turboService.TurboButtonPressed += (s, e) =>
                    {
                        DispatcherQueue.TryEnqueue(() => _windowManager.ToggleVisibility());
                    };
                    System.Diagnostics.Debug.WriteLine("TurboService connected to MainWindow successfully");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ TurboService not available from App");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TurboService setup failed: {ex.Message}");
            }
        }

        private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs args)
        {
            var oldScaleFactor = _dpiService.ScaleFactor;
            _dpiService.UpdateScaleFactor();

            if (_dpiService.HasScaleChanged(oldScaleFactor))
            {
                _windowManager.PositionWindow();
            }
        }

        private void SimulateAltTab()
        {
            keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
            keybd_event(VK_TAB, 0, 0, UIntPtr.Zero);
            keybd_event(VK_TAB, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private bool TrySetMicaBackdrop()
        {
            if (MicaController.IsSupported())
            {
                _backdropConfig = new SystemBackdropConfiguration
                {
                    IsInputActive = true,
                    Theme = SystemBackdropTheme.Dark
                };

                _micaController = new MicaController();
                _micaController.AddSystemBackdropTarget(this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
                _micaController.SetSystemBackdropConfiguration(_backdropConfig);
                return true;
            }

            MainBorder.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(240, 30, 30, 45));
            return false;
        }

        public void SetTdpMonitor(TdpMonitorService tdpMonitor)
        {
            _tdpMonitor = tdpMonitor;

            if (_mainPage != null)
            {
                SetupTdpMonitor();
            }
        }

        private void SetupTdpMonitor()
        {
            if (_tdpMonitor == null || _mainPage == null) return;

            bool tdpMonitorStarted = false;

            _mainPage.TdpPicker.TdpChanged += (s, value) =>
            {
                System.Diagnostics.Debug.WriteLine($"TDP Changed Event for Monitor: {value}W");
                _tdpMonitor.UpdateTargetTdp(value);

                if (!tdpMonitorStarted && SettingsService.GetTdpCorrectionEnabled() && value > 0)
                {
                    _tdpMonitor.Start();
                    tdpMonitorStarted = true;
                    System.Diagnostics.Debug.WriteLine($"TDP Monitor started with target: {value}W");
                }
            };

            if (_mainPage.TdpPicker.SelectedTdp > 0)
            {
                _tdpMonitor.UpdateTargetTdp(_mainPage.TdpPicker.SelectedTdp);
                if (SettingsService.GetTdpCorrectionEnabled())
                {
                    _tdpMonitor.Start();
                    tdpMonitorStarted = true;
                }
            }

            _tdpMonitor.TdpDriftDetected += (s, args) =>
            {
                System.Diagnostics.Debug.WriteLine($"TDP drift {args.CurrentTdp}W -> {args.TargetTdp}W (corrected: {args.CorrectionApplied})");
            };
        }

        // Main border drag handling for window movement
        private void StartWindowDrag(Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e, FrameworkElement dragHandle)
        {
            _isDragging = true;
            _touchDragStarted = false;

            // Capture the pointer on the drag handle
            dragHandle.CapturePointer(e.Pointer);

            if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse)
            {
                GetCursorPos(out POINT cursorPos);
                _lastPointerPosition = new Windows.Graphics.PointInt32(cursorPos.X, cursorPos.Y);
            }
            else if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch)
            {
                var windowId = Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(this));
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                var windowPos = appWindow.Position;
                var touchPoint = e.GetCurrentPoint(LogoDragHandle);

                _lastTouchPosition = new Windows.Foundation.Point(
                    windowPos.X + touchPoint.Position.X,
                    windowPos.Y + touchPoint.Position.Y);
                _touchDragStarted = true;
            }
        }

        private void MoveWindow(Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            var windowId = Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(this));
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            var pointer = e.Pointer;

            if (pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse)
            {
                // Mouse logic stays the same
                GetCursorPos(out POINT cursorPos);
                var currentPosition = new Windows.Graphics.PointInt32(cursorPos.X, cursorPos.Y);

                int deltaX = currentPosition.X - _lastPointerPosition.X;
                int deltaY = currentPosition.Y - _lastPointerPosition.Y;

                var currentPos = appWindow.Position;
                appWindow.Move(new Windows.Graphics.PointInt32(currentPos.X + deltaX, currentPos.Y + deltaY));

                _lastPointerPosition = currentPosition;
            }
            else if (pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch && _touchDragStarted)
            {
                // Touch logic stays the same
                var currentTouchPoint = e.GetCurrentPoint(LogoDragHandle);
                var windowPos = appWindow.Position;

                var currentScreenTouch = new Windows.Foundation.Point(
                    windowPos.X + currentTouchPoint.Position.X,
                    windowPos.Y + currentTouchPoint.Position.Y);

                double deltaX = currentScreenTouch.X - _lastTouchPosition.X;
                double deltaY = currentScreenTouch.Y - _lastTouchPosition.Y;

                var newX = windowPos.X + (int)deltaX;
                var newY = windowPos.Y + (int)deltaY;

                appWindow.Move(new Windows.Graphics.PointInt32(newX, newY));
                _lastTouchPosition = currentScreenTouch;
            }
        }
        private void EndWindowDrag(Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            _isDragging = false;
            _touchDragStarted = false;

            LogoDragHandle.ReleasePointerCapture(e.Pointer);

            // Return logo to normal opacity
            LogoDragHandle.Opacity = 1.0;
        }
        private void OnBatteryInfoUpdated(object? sender, BatteryInfo info)
        {
            BatteryPercentageText = $"{info.Percent}%";
            BatteryIcon.Glyph = GetBatteryGlyph(info.Percent, info.IsCharging);
            BatteryIcon.Foreground = new SolidColorBrush(info.IsCharging ? Microsoft.UI.Colors.DarkGreen : Microsoft.UI.Colors.White);
            BatteryTextBrush = new SolidColorBrush(info.IsCharging ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.White);

            string timeStr = info.RemainingDischargeTime == TimeSpan.Zero ? "--" : info.RemainingDischargeTime.ToString(@"hh\:mm");
            BatteryToolTip = $"{info.Percent}% - {(info.IsCharging ? "Charging" : info.OnAc ? "Plugged in" : "On battery")}\nTime remaining: {timeStr}";
        }

        private static string GetBatteryGlyph(int percent, bool charging)
        {
            int index = Math.Clamp(percent / 10, 0, 10);
            if (charging)
            {
                if (index >= 9) return "\uE83E"; // BatteryCharging9
                return char.ConvertFromUtf32(0xE85A + index);
            }
            else
            {
                if (index >= 10) return "\uE83F"; // Battery10
                return char.ConvertFromUtf32(0xE850 + index);
            }
        }

        private void Cleanup()
        {
            _mainPage?.TdpPicker?.Dispose();
            _windowManager?.Dispose();
            _turboService?.Dispose();
            _micaController?.Dispose();
            _tdpMonitor?.Dispose();
            _batteryService?.Dispose();
            _navigationService?.Dispose();
            _gamepadNavigationService?.Dispose();
            _enhancedGameDetectionService?.Dispose();
            _losslessScalingService?.Dispose();
            _powerProfileService?.Dispose();
            _artworkService?.Dispose();
        }

        public void ToggleWindowVisibility()
        {
            // Clear gamepad focus before toggling to prevent lingering focus borders
            if (_gamepadNavigationService != null)
            {
                _gamepadNavigationService.ClearFocus();
                _gamepadNavigationService.DeactivateGamepadMode();
                System.Diagnostics.Debug.WriteLine("🎮 Cleared gamepad focus before window visibility toggle");
            }

            _windowManager.ToggleVisibility();
        }

        public void HandleHibernationResume()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("⚡ MainWindow handling hibernation resume...");

                // Refresh the TDP picker UI to reflect current values
                if (_mainPage?.TdpPicker != null)
                {
                    // Get the current TDP value from settings and ensure the UI reflects it
                    var currentTdp = SettingsService.GetLastUsedTdp();
                    if (currentTdp >= HudraSettings.MIN_TDP && currentTdp <= HudraSettings.MAX_TDP)
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            _currentTdpValue = currentTdp;
                            _mainPage.TdpPicker.SelectedTdp = currentTdp;
                            System.Diagnostics.Debug.WriteLine($"⚡ Updated TDP picker UI to {currentTdp}W after hibernation resume");
                        });
                    }
                }

                // UI will automatically refresh through existing update mechanisms

                System.Diagnostics.Debug.WriteLine("⚡ MainWindow hibernation resume handling completed");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Error in MainWindow hibernation resume handling: {ex.Message}");
            }
        }

        // Power Profile Methods
        private async Task LoadPowerProfilesAsync()
        {
            try
            {
                var profiles = await _powerProfileService.GetAvailableProfilesAsync();
                AvailablePowerProfiles = new ObservableCollection<PowerProfile>(profiles);

                // Set current active profile as selected
                SelectedPowerProfile = profiles.FirstOrDefault(p => p.IsActive);

                // Initialize power profile control in settings page if available
                if (_settingsPage?.PowerProfileControl != null)
                {
                    await _settingsPage.PowerProfileControl.InitializeAsync();

                    // Set up event handler for power profile changes
                    _settingsPage.PowerProfileControl.PowerProfileChanged += OnPowerProfileControlChanged;
                }

                // Initialize intelligent power switching with enhanced game detection service
                if (_enhancedGameDetectionService != null)
                {
                    _powerProfileService.InitializeIntelligentSwitching(_enhancedGameDetectionService);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load power profiles: {ex.Message}");
            }
        }

        private async Task OnPowerProfileSelectionChanged(PowerProfile? profile)
        {
            if (profile == null) return;

            try
            {
                var success = await _powerProfileService.SetActiveProfileAsync(profile.Id);
                if (success)
                {
                    // Update active state for all profiles
                    foreach (var p in AvailablePowerProfiles)
                        p.IsActive = p.Id == profile.Id;

                    // Save preference
                    SettingsService.SetPreferredPowerProfile(profile.Id);

                    System.Diagnostics.Debug.WriteLine($"Power profile changed to: {profile.Name}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to change power profile to: {profile.Name}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to change power profile: {ex.Message}");
            }
        }

        private void OnPowerProfileControlChanged(object? sender, PowerProfileChangedEventArgs e)
        {
            if (e.IsApplied)
            {
                // Update the main window's selected profile
                SelectedPowerProfile = e.Profile;
                System.Diagnostics.Debug.WriteLine($"Power profile control changed to: {e.Profile.Name}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Failed to apply power profile: {e.Profile.Name}");
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // P/Invoke declarations
        [DllImport("user32.dll", SetLastError = true)]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        private const byte VK_MENU = 0x12;
        private const byte VK_TAB = 0x09;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private void InitializeGameDetection()
        {
            try
            {
                _enhancedGameDetectionService = new EnhancedGameDetectionService(DispatcherQueue);
                _enhancedGameDetectionService.GameDetected += OnGameDetected;
                _enhancedGameDetectionService.GameStopped += OnGameStopped;

                // Subscribe to scanning progress events for visual indicator
                _enhancedGameDetectionService.ScanProgressChanged += OnScanProgressChanged;
                _enhancedGameDetectionService.ScanningStateChanged += OnScanningStateChanged;
                _enhancedGameDetectionService.DatabaseReady += OnDatabaseReady;

                // Initialize game database for GameSettingsPage
                _gameDatabase = _enhancedGameDetectionService.Database;

                // Initialize SteamGridDB artwork service for GameSettingsPage
                // Using the same API key as EnhancedGameDetectionService
                try
                {
                    _artworkService = new SteamGridDbArtworkService("89b83ee6250e718cb40766bde7bcdf1d");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"MainWindow: Failed to initialize artwork service: {ex.Message}");
                    _artworkService = null;
                }

                // Initially hide the Alt+Tab button until a game is detected
                AltTabButton.Visibility = Visibility.Collapsed;

                // Initialize Lossless Scaling service
                _losslessScalingService = new LosslessScalingService();
                _losslessScalingService.LosslessScalingStatusChanged += OnLosslessScalingStatusChanged;

                // Initially hide the Lossless Scaling button
                LosslessScalingButtonVisible = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize game detection: {ex.Message}");
                AltTabButton.Visibility = Visibility.Visible;
            }
        }

        private void OnGameDetected(object? sender, GameInfo? gameInfo)
        {
            if (gameInfo == null) return;

            try
            {
                System.Diagnostics.Debug.WriteLine($"Game detected: {gameInfo.WindowTitle} ({gameInfo.ProcessName})");

                // Show the Alt+Tab button with game controller icon
                AltTabButton.Visibility = Visibility.Visible;
                UpdateAltTabButtonToGameIcon();

                // Update Lossless Scaling button visibility
                UpdateLosslessScalingButtonVisibility();

                // Update Force Quit button visibility
                UpdateForceQuitButtonVisibility();

                // Update FPS limiter for game detection
                if (_mainPage?.FpsLimiter != null)
                {
                    _mainPage.FpsLimiter.IsGameRunning = true;

                }

                // Update Alt+Tab tooltip
                string gameName = !string.IsNullOrWhiteSpace(gameInfo.WindowTitle)
                    ? gameInfo.WindowTitle
                    : gameInfo.ProcessName;
                ToolTipService.SetToolTip(AltTabButton, $"Return to {gameName}");

                // Update Force Quit tooltip
                ToolTipService.SetToolTip(ForceQuitButton, $"Force Quit {gameName}");

                // Update Lossless Scaling tooltip with game name
                UpdateLosslessScalingTooltip();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling game detection: {ex.Message}");
            }

        }

        private void OnGameStopped(object? sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Game stopped - hiding Alt+Tab button");
                AltTabButton.Visibility = Visibility.Collapsed;
                ToolTipService.SetToolTip(AltTabButton, "Return to Game");
                ToolTipService.SetToolTip(ForceQuitButton, "Force Quit Game");

                // Update Lossless Scaling button visibility
                UpdateLosslessScalingButtonVisibility();

                // Update Force Quit button visibility
                UpdateForceQuitButtonVisibility();

                // Update FPS limiter for game stopped
                if (_mainPage?.FpsLimiter != null)
                {
                    _mainPage.FpsLimiter.IsGameRunning = false;

                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling game stopped: {ex.Message}");
            }

        }

        private void UpdateAltTabButtonToGameIcon()
        {
            // Use a game controller icon when a game is detected
            var gameIcon = new FontIcon
            {
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets"),
                Glyph = "\uE7FC", // Game controller icon
                FontSize = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            AltTabButton.Content = gameIcon;
        }

        private void OnLosslessScalingStatusChanged(object? sender, bool isRunning)
        {
            try
            {
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
                {
                    UpdateLosslessScalingButtonVisibility();
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling Lossless Scaling status change: {ex.Message}");
            }
        }

        private void UpdateLosslessScalingButtonVisibility()
        {
            bool hasGame = _enhancedGameDetectionService?.CurrentGame != null;
            bool lsRunning = _losslessScalingService?.IsLosslessScalingRunning() ?? false;

            LosslessScalingButtonVisible = hasGame && lsRunning;

            // Update tooltip when visibility changes
            if (LosslessScalingButtonVisible)
            {
                UpdateLosslessScalingTooltip();
            }
        }

        private void UpdateForceQuitButtonVisibility()
        {
            bool hasGame = _enhancedGameDetectionService?.CurrentGame != null;
            ForceQuitButtonVisible = hasGame;
        }

        private async Task ShowLosslessScalingError(string message)
        {
            try
            {
                var dialog = new ContentDialog()
                {
                    Title = "Lossless Scaling Error",
                    Content = message,
                    CloseButtonText = "OK",
                    XamlRoot = this.Content.XamlRoot
                };

                await dialog.ShowWithGamepadSupportAsync(_gamepadNavigationService);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to show error dialog: {ex.Message}");
            }
        }

        private void UpdateLosslessScalingTooltip()
        {
            try
            {
                string gameName = GetCurrentGameName();
                string tooltip = $"Scale {gameName}";
                ToolTipService.SetToolTip(LosslessScalingButton, tooltip);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating Lossless Scaling tooltip: {ex.Message}");
                // Fallback to generic tooltip
                ToolTipService.SetToolTip(LosslessScalingButton, "Activate Lossless Scaling");
            }
        }

        private string GetCurrentGameName()
        {
            if (_enhancedGameDetectionService?.CurrentGame != null)
            {
                // Prefer window title if available and meaningful
                if (!string.IsNullOrWhiteSpace(_enhancedGameDetectionService.CurrentGame.WindowTitle))
                {
                    return _enhancedGameDetectionService.CurrentGame.WindowTitle;
                }

                // Fallback to process name
                return _enhancedGameDetectionService.CurrentGame.ProcessName;
            }

            return "Game";
        }

        // Enhanced game detection event handlers
        private void OnScanProgressChanged(object? sender, string progress)
        {
            ScanProgressText = progress;
        }

        private void OnScanningStateChanged(object? sender, bool isScanning)
        {
            IsGameLibraryScanning = isScanning;
        }

        private void OnDatabaseReady(object? sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("Enhanced game detection: Database ready");
        }

        private async Task InitializeFpsLimiterAsync()
        {
            try
            {
                var rtssDetection = await _fpsLimiterService.DetectRtssInstallationAsync();
                IsRtssSupported = rtssDetection.IsInstalled;

                if (IsRtssSupported)
                {
                    // Get current refresh rate from resolution service
                    var currentRefreshRateResult = _resolutionService.GetCurrentRefreshRate();
                    int currentRefreshRate = currentRefreshRateResult.Success ? currentRefreshRateResult.RefreshRate : 120;

                    // Generate FPS options based on refresh rate
                    FpsSettings.AvailableFpsOptions = _fpsLimiterService.CalculateFpsOptionsFromRefreshRate(currentRefreshRate);
                    FpsSettings.IsRtssAvailable = true;
                    FpsSettings.RtssInstallPath = rtssDetection.InstallPath;
                    FpsSettings.RtssVersion = rtssDetection.Version;

                    // Load saved settings
                    var savedFpsLimit = SettingsService.GetSelectedFpsLimit();

                    // Set selected option (default to "Unlimited" (0) for new users)
                    FpsSettings.SelectedFpsLimit = FpsSettings.AvailableFpsOptions.Contains(savedFpsLimit)
                        ? savedFpsLimit
                        : 0; // Default to "Unlimited" for new users

                    // Update the UI control if MainPage is initialized
                    if (_mainPage?.FpsLimiter != null)
                    {
                        _mainPage.FpsLimiter.FpsSettings = FpsSettings;
                        _mainPage.FpsLimiter.IsRtssSupported = IsRtssSupported;
                        _mainPage.FpsLimiter.UpdateFpsOptions(FpsSettings.AvailableFpsOptions);

                        // Set up event handler for FPS changes
                        _mainPage.FpsLimiter.FpsLimitChanged += OnFpsLimitChanged;
                    }

                    System.Diagnostics.Debug.WriteLine($"RTSS detected: {rtssDetection.InstallPath}");
                    System.Diagnostics.Debug.WriteLine($"FPS options available: {string.Join(", ", FpsSettings.AvailableFpsOptions)}");

                    // Only apply saved FPS limit on first initialization (app startup), not on navigation
                    if (_isFirstFpsInitialization)
                    {
                        _isFirstFpsInitialization = false; // Ensure this only runs once
                        
                        System.Diagnostics.Debug.WriteLine($"Applying previously selected FPS limit on startup: {FpsSettings.SelectedFpsLimit}");
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                // Wait for RTSS to fully initialize if it was just started
                                System.Diagnostics.Debug.WriteLine("Waiting 3 seconds for RTSS to fully initialize...");
                                await Task.Delay(3000);
                                
                                bool success;
                                if (FpsSettings.SelectedFpsLimit > 0)
                                {
                                    success = await _fpsLimiterService.SetGlobalFpsLimitAsync(FpsSettings.SelectedFpsLimit);
                                    if (success)
                                    {
                                        FpsSettings.IsCurrentlyLimited = true;
                                        System.Diagnostics.Debug.WriteLine($"✅ Applied saved FPS limit on startup: {FpsSettings.SelectedFpsLimit}");
                                    }
                                }
                                else
                                {
                                    // "Unlimited" selected - disable FPS limiting
                                    success = await _fpsLimiterService.DisableGlobalFpsLimitAsync();
                                    if (success)
                                    {
                                        FpsSettings.IsCurrentlyLimited = false;
                                        System.Diagnostics.Debug.WriteLine($"✅ Disabled FPS limiting on startup (Unlimited selected)");
                                    }
                                }
                                
                                if (!success)
                                {
                                    System.Diagnostics.Debug.WriteLine($"❌ Failed to apply saved FPS setting on startup: {FpsSettings.SelectedFpsLimit}");
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"❌ Exception applying saved FPS setting on startup: {ex.Message}");
                            }
                        });
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Skipping FPS limit application - not first initialization");
                    }
                }
                else
                {
                    FpsSettings.IsRtssAvailable = false;
                    System.Diagnostics.Debug.WriteLine("RTSS not detected - FPS limiting not available");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize FPS limiter: {ex.Message}");
                IsRtssSupported = false;
                FpsSettings.IsRtssAvailable = false;
            }
        }

        private async void OnFpsLimitChanged(object? sender, FpsLimitChangedEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"FPS limit changed: {e.FpsLimit}");

                // Always save the user's selection
                FpsSettings.SelectedFpsLimit = e.FpsLimit;
                SettingsService.SetSelectedFpsLimit(e.FpsLimit);

                System.Diagnostics.Debug.WriteLine($"Saved FPS settings: SelectedFpsLimit now = {FpsSettings.SelectedFpsLimit}");

                // Apply FPS limiting immediately regardless of game state
                bool success;
                if (e.FpsLimit > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Applying FPS limit immediately: {e.FpsLimit}");
                    success = await _fpsLimiterService.SetGlobalFpsLimitAsync(e.FpsLimit);
                    if (success)
                    {
                        FpsSettings.IsCurrentlyLimited = true;
                        System.Diagnostics.Debug.WriteLine($"✅ Applied RTSS FPS limit: {e.FpsLimit}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Failed to apply RTSS FPS limit: {e.FpsLimit}");
                    }
                }
                else
                {
                    // User selected "Unlimited" - disable FPS limiting
                    System.Diagnostics.Debug.WriteLine("Disabling FPS limit (Unlimited selected)");
                    success = await _fpsLimiterService.DisableGlobalFpsLimitAsync();
                    if (success)
                    {
                        FpsSettings.IsCurrentlyLimited = false;
                        System.Diagnostics.Debug.WriteLine("✅ Disabled RTSS FPS limit (Unlimited)");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("❌ Failed to disable RTSS FPS limit");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to handle FPS change: {ex.Message}");
            }
        }


        private async Task StartRtssIfEnabledAsync()
        {
            try
            {
                if (SettingsService.GetStartRtssWithHudra())
                {
                    System.Diagnostics.Debug.WriteLine("Auto-starting RTSS as per user settings");
                    await _fpsLimiterService.StartRtssIfNeededAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to auto-start RTSS: {ex.Message}");
            }
        }

    }
}
