using HUDRA.Configuration;
using HUDRA.Controls;
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
        private TdpMonitorService? _tdpMonitor;
        private TurboService? _turboService;
        private MicaController? _micaController;
        private SystemBackdropConfiguration? _backdropConfig;
        private GameDetectionService? _gameDetectionService;
        private LosslessScalingService? _losslessScalingService;
        

        // Public navigation service access for TDP picker
        public NavigationService NavigationService => _navigationService;

        // Public window manager access for App.xaml.c
        public WindowManagementService WindowManager => _windowManager;

        //Navigation events
        private bool _mainPageInitialized = false;

        // Current page references
        private MainPage? _mainPage;
        private SettingsPage? _settingsPage;
        private FanCurvePage? _fanCurvePage;

        //Drag Handling
        private bool _isDragging = false;
        private Windows.Graphics.PointInt32 _lastPointerPosition;
        private Windows.Foundation.Point _lastTouchPosition;
        private bool _touchDragStarted = false;

        // Navigation state for visual feedback
        private Type _currentPageType;

        // Store the actual TDP value across navigation
        private int _currentTdpValue = -1; // Initialize to -1 to indicate not set

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
            _batteryService = new BatteryService(DispatcherQueue);
            _powerProfileService = new PowerProfileService();

            // Subscribe to navigation events
            _navigationService.PageChanged += OnPageChanged;
            _batteryService.BatteryInfoUpdated += OnBatteryInfoUpdated;

            InitializeWindow();
            SetupEventHandlers();
            SetupDragHandling();
            SetupTurboService();

            _navigationService.NavigateToMain();

            this.Closed += (s, e) => Cleanup();
        }

        private void InitializeWindow()
        {
            TrySetMicaBackdrop();
            _windowManager.Initialize();
            InitializeGameDetection();
        }

        private void OnPageChanged(object sender, Type pageType)
        {
            _currentPageType = pageType;
            UpdateNavigationButtonStates();
            HandlePageSpecificInitialization(pageType);
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
        }

        private void InitializeMainPage()
        {
            if (_mainPage == null) return;

            if (!_mainPageInitialized)
            {
                // First visit - full initialization
                _mainPage.Initialize(_dpiService, _resolutionService, _audioService, _brightnessService);
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
            
            System.Diagnostics.Debug.WriteLine("=== InitializeFanCurvePage called ===");
            
            try
            {
                _fanCurvePage.Initialize();
                System.Diagnostics.Debug.WriteLine("=== FanCurvePage initialization complete ===");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR in InitializeFanCurvePage: {ex.Message}");
            }
        }

        private void UpdateNavigationButtonStates()
        {
            // Update visual states of navigation buttons based on current page
            UpdateButtonState(MainPageNavButton, _currentPageType == typeof(MainPage));
            UpdateButtonState(FanCurveNavButton, _currentPageType == typeof(FanCurvePage));
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
            _navigationService.NavigateToMain();
        }

        private void FanCurveNavButton_Click(object sender, RoutedEventArgs e)
        {
            _navigationService.NavigateToFanCurve();
        }

        private void SettingsNavButton_Click(object sender, RoutedEventArgs e)
        {
            _navigationService.NavigateToSettings();
        }

        // Existing event handlers
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _windowManager.ToggleVisibility();
        }

        private void AltTabButton_Click(object sender, RoutedEventArgs e)
        {
            _windowManager.ToggleVisibility();

            if (_gameDetectionService?.SwitchToGame() == true)
            {
                System.Diagnostics.Debug.WriteLine("Successfully switched to game");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Game switching failed, using generic Alt+Tab");
                SimulateAltTab();
            }
        }

        private async void LosslessScalingButton_Click(object sender, RoutedEventArgs e)
        {
            if (_losslessScalingService == null || _gameDetectionService == null)
                return;

            try
            {
                // 1. Hide HUDRA
                _windowManager.ToggleVisibility();

                // 2. Switch to game
                if (_gameDetectionService.SwitchToGame() != true)
                {
                    // Game switching failed - show error and abort
                    System.Diagnostics.Debug.WriteLine("Game switching failed - aborting Lossless Scaling activation");
                    await ShowLosslessScalingError("Failed to switch to game window");
                    return;
                }

                // 3. Wait 200ms
                await Task.Delay(200);

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

        private void SetupTurboService()
        {
            try
            {
                _turboService = new TurboService();
                _turboService.TurboButtonPressed += (s, e) =>
                {
                    DispatcherQueue.TryEnqueue(() => _windowManager.ToggleVisibility());
                };
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
            _gameDetectionService?.Dispose();
            _losslessScalingService?.Dispose();
            _powerProfileService?.Dispose();
        }

        public void ToggleWindowVisibility() => _windowManager.ToggleVisibility();

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

                // Initialize intelligent power switching with game detection service
                if (_gameDetectionService != null)
                {
                    _powerProfileService.InitializeIntelligentSwitching(_gameDetectionService);
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
                _gameDetectionService = new GameDetectionService(DispatcherQueue);
                _gameDetectionService.GameDetected += OnGameDetected;
                _gameDetectionService.GameStopped += OnGameStopped;

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

                // Update Alt+Tab tooltip
                string gameName = !string.IsNullOrWhiteSpace(gameInfo.WindowTitle)
                    ? gameInfo.WindowTitle
                    : gameInfo.ProcessName;
                ToolTipService.SetToolTip(AltTabButton, $"Return to {gameName}");

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

                // Update Lossless Scaling button visibility
                UpdateLosslessScalingButtonVisibility();
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
            bool hasGame = _gameDetectionService?.CurrentGame != null;
            bool lsRunning = _losslessScalingService?.IsLosslessScalingRunning() ?? false;

            LosslessScalingButtonVisible = hasGame && lsRunning;

            // Update tooltip when visibility changes
            if (LosslessScalingButtonVisible)
            {
                UpdateLosslessScalingTooltip();
            }
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

                await dialog.ShowAsync();
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
            if (_gameDetectionService?.CurrentGame != null)
            {
                // Prefer window title if available and meaningful
                if (!string.IsNullOrWhiteSpace(_gameDetectionService.CurrentGame.WindowTitle))
                {
                    return _gameDetectionService.CurrentGame.WindowTitle;
                }
                
                // Fallback to process name
                return _gameDetectionService.CurrentGame.ProcessName;
            }

            return "Game";
        }



    }
}