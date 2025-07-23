using HUDRA.Configuration;
using HUDRA.Controls;
using HUDRA.Pages;
using HUDRA.Services;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
        private TdpMonitorService? _tdpMonitor;
        private TurboService? _turboService;
        private MicaController? _micaController;
        private SystemBackdropConfiguration? _backdropConfig;
        private GameDetectionService? _gameDetectionService;
        private Storyboard? _glowPulseStoryboard;

        // Public navigation service access for TDP picker
        public NavigationService NavigationService => _navigationService;

        //Navigation events
        private bool _mainPageInitialized = false;

        // Current page references
        private MainPage? _mainPage;
        private SettingsPage? _settingsPage;

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
            System.Diagnostics.Debug.WriteLine($"=== Page Changed to: {pageType.Name} ===");

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
                    System.Diagnostics.Debug.WriteLine("=== MainPage HandlePageSpecificInitialization ===");
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
                    System.Diagnostics.Debug.WriteLine("=== SettingsPage HandlePageSpecificInitialization ===");
                    if (ContentFrame.Content is SettingsPage settingsPage)
                    {
                        System.Diagnostics.Debug.WriteLine("SettingsPage found in ContentFrame, calling Initialize");
                        _settingsPage = settingsPage;
                        InitializeSettingsPage();
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"ERROR: ContentFrame.Content is not SettingsPage! Type: {ContentFrame.Content?.GetType().Name ?? "null"}");
                    }
                });
            }
        }

        private void InitializeMainPage()
        {
            if (_mainPage == null) return;

            System.Diagnostics.Debug.WriteLine("=== Initializing MainPage ===");

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
                System.Diagnostics.Debug.WriteLine($"Returning to MainPage - preserving TDP: {_currentTdpValue}");

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
                System.Diagnostics.Debug.WriteLine($"Setting preserved TDP value: {_currentTdpValue}");
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
                    System.Diagnostics.Debug.WriteLine($"Ensuring scroll position for preserved TDP: {_currentTdpValue}");
                    _mainPage.TdpPicker.EnsureScrollPositionAfterLayout();
                });
            }

            SetupTdpMonitor();
            System.Diagnostics.Debug.WriteLine("=== MainPage initialization complete ===");
        }

        private void InitializeSettingsPage()
        {
            if (_settingsPage == null) return;

            System.Diagnostics.Debug.WriteLine("=== InitializeSettingsPage called ===");

            try
            {
                _settingsPage.Initialize(_dpiService);
                System.Diagnostics.Debug.WriteLine("=== SettingsPage initialization complete ===");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR in InitializeSettingsPage: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private void UpdateNavigationButtonStates()
        {
            // Update visual states of navigation buttons based on current page
            UpdateButtonState(MainPageNavButton, _currentPageType == typeof(MainPage));
            UpdateButtonState(SettingsNavButton, _currentPageType == typeof(SettingsPage));
        }

        private void UpdateButtonState(Button button, bool isActive)
        {
            if (button == null)
            {
                System.Diagnostics.Debug.WriteLine("UpdateButtonState: button is null");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"UpdateButtonState: {button.Name} - isActive: {isActive}");

            // Better background colors that actually match the content area
            //var activeBackground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(80, 255, 255, 255)); // More opaque white
            //var inactiveBackground = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

            var activeForeground = new SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
            var inactiveForeground = new SolidColorBrush(Microsoft.UI.Colors.White);

            System.Diagnostics.Debug.WriteLine($"Setting {button.Name} background to: {(isActive ? "Semi-opaque white" : "Transparent")}");

            //button.Background = isActive ? activeBackground : inactiveBackground;

            // Handle FontIcon content (both buttons should now use FontIcon)
            if (button.Content is FontIcon icon)
            {
                System.Diagnostics.Debug.WriteLine($"{button.Name} has FontIcon content");
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
            System.Diagnostics.Debug.WriteLine("MainPage navigation button clicked");
            _navigationService.NavigateToMain();
        }

        private void SettingsNavButton_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("Settings navigation button clicked");
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


        private void SetupEventHandlers()
        {
            this.SizeChanged += MainWindow_SizeChanged;
        }

        private void SetupDragHandling()
        {
            // Set up drag handling on the main border itself for better coverage
            MainBorder.PointerPressed += OnMainBorderPointerPressed;
            MainBorder.PointerMoved += OnMainBorderPointerMoved;
            MainBorder.PointerReleased += OnMainBorderPointerReleased;
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
                _windowManager.PositionAboveSystemTray();
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
            System.Diagnostics.Debug.WriteLine("=== SetTdpMonitor called ===");
            _tdpMonitor = tdpMonitor;

            if (_mainPage != null)
            {
                System.Diagnostics.Debug.WriteLine("=== MainPage already loaded, setting up TDP monitor ===");
                SetupTdpMonitor();
            }
        }

        private void SetupTdpMonitor()
        {
            if (_tdpMonitor == null || _mainPage == null) return;

            System.Diagnostics.Debug.WriteLine("=== TDP Monitor Setup Starting ===");

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

            System.Diagnostics.Debug.WriteLine("=== TDP Monitor Setup Complete ===");
        }

        // Main border drag handling for window movement
        private void OnMainBorderPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse ||
                e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch)
            {
                var properties = e.GetCurrentPoint(MainBorder).Properties;
                bool shouldStartDrag = (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse && properties.IsLeftButtonPressed) ||
                                      (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch);

                if (shouldStartDrag)
                {
                    var position = e.GetCurrentPoint(MainBorder);

                    // Check if click is over interactive controls - if so, don't drag
                    try
                    {
                        // Check navigation bar controls
                        if (CheckElementBounds(BatteryPanel, position.Position)) return;
                        if (CheckElementBounds(MainPageNavButton, position.Position)) return;
                        if (CheckElementBounds(SettingsNavButton, position.Position)) return;
                        if (CheckElementBounds(AltTabButton, position.Position)) return;
                        if (CheckElementBounds(CloseButton, position.Position)) return;

                        // Check if clicking on the content frame (where interactive controls are)
                        if (ContentFrame.Content != null)
                        {
                            var frameTransform = ContentFrame.TransformToVisual(MainBorder);
                            var frameBounds = frameTransform.TransformBounds(new Windows.Foundation.Rect(0, 0, ContentFrame.ActualWidth, ContentFrame.ActualHeight));

                            // Allow dragging on content frame, but let individual controls handle their own interactions
                            if (frameBounds.Contains(position.Position))
                            {
                                // Check if we're clicking on specific interactive controls within the frame
                                if (ContentFrame.Content is MainPage mainPage)
                                {
                                    if (CheckElementBounds(mainPage.TdpPicker, position.Position, MainBorder)) return;
                                    if (CheckElementBounds(mainPage.ResolutionPicker, position.Position, MainBorder)) return;
                                    if (CheckElementBounds(mainPage.AudioControls, position.Position, MainBorder)) return;
                                    if (CheckElementBounds(mainPage.BrightnessControls, position.Position, MainBorder)) return;
                                }
                                else if (ContentFrame.Content is SettingsPage settingsPage)
                                {
                                    if (CheckElementBounds(settingsPage.StartupTdpPicker, position.Position, MainBorder)) return;
                                    if (CheckElementBounds(settingsPage.TdpCorrectionToggle, position.Position, MainBorder)) return;
                                    if (CheckElementBounds(settingsPage.UseStartupTdpToggle, position.Position, MainBorder)) return;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // If bounds detection fails, allow dragging
                    }

                    // Start window dragging
                    _isDragging = true;
                    MainBorder.CapturePointer(e.Pointer);

                    if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse)
                    {
                        GetCursorPos(out POINT cursorPos);
                        _lastPointerPosition = new Windows.Graphics.PointInt32(cursorPos.X, cursorPos.Y);
                    }
                    else
                    {
                        var windowId = Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(this));
                        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                        var windowPos = appWindow.Position;

                        _lastTouchPosition = new Windows.Foundation.Point(
                            windowPos.X + position.Position.X,
                            windowPos.Y + position.Position.Y);
                        _touchDragStarted = true;
                    }
                }
            }
        }

        private void OnMainBorderPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (!_isDragging) return;

            var properties = e.GetCurrentPoint(MainBorder).Properties;
            bool shouldContinueDrag = (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse && properties.IsLeftButtonPressed) ||
                                     (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch);

            if (shouldContinueDrag)
            {
                var windowId = Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(this));
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

                if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse)
                {
                    GetCursorPos(out POINT cursorPos);
                    var currentPosition = new Windows.Graphics.PointInt32(cursorPos.X, cursorPos.Y);

                    int deltaX = currentPosition.X - _lastPointerPosition.X;
                    int deltaY = currentPosition.Y - _lastPointerPosition.Y;

                    var currentPos = appWindow.Position;
                    appWindow.Move(new Windows.Graphics.PointInt32(currentPos.X + deltaX, currentPos.Y + deltaY));

                    _lastPointerPosition = currentPosition;
                }
                else
                {
                    if (_touchDragStarted)
                    {
                        var currentTouchPoint = e.GetCurrentPoint(MainBorder);
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
            }
            else
            {
                _isDragging = false;
                _touchDragStarted = false;
            }
        }

        private void OnMainBorderPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            _isDragging = false;
            _touchDragStarted = false;
            MainBorder.ReleasePointerCapture(e.Pointer);
        }

        // Helper method to check if a point is within an element's bounds
        private bool CheckElementBounds(FrameworkElement element, Windows.Foundation.Point point, FrameworkElement? relativeTo = null)
        {
            if (element == null) return false;

            try
            {
                var transform = element.TransformToVisual(relativeTo ?? MainBorder);
                var bounds = transform.TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));
                return bounds.Contains(point);
            }
            catch
            {
                return false;
            }
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
        }

        public void ToggleWindowVisibility() => _windowManager.ToggleVisibility();

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

                System.Diagnostics.Debug.WriteLine("Game detection service initialized");
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

                // Update tooltip
                string gameName = !string.IsNullOrWhiteSpace(gameInfo.WindowTitle)
                    ? gameInfo.WindowTitle
                    : gameInfo.ProcessName;
                ToolTipService.SetToolTip(AltTabButton, $"Return to {gameName}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling game detection: {ex.Message}");
            }

            StartGlowPulseAnimation();
        }

        private void OnGameStopped(object? sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Game stopped - hiding Alt+Tab button");
                AltTabButton.Visibility = Visibility.Collapsed;
                ToolTipService.SetToolTip(AltTabButton, "Return to Game");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling game stopped: {ex.Message}");
            }

            StopGlowPulseAnimation();
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
private void StartGlowPulseAnimation()
{
    try
    {
        // Apply the custom style first
        AltTabButton.Style = (Style)Application.Current.Resources["GlowingGameButtonStyle"];
        
        // Wait for the template to be applied
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            // Wait a bit more for template application
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                
                // Find the GlowLayer in the button's template
                var glowLayer = FindChildByName<Border>(AltTabButton, "GlowLayer");
                if (glowLayer == null)
                {
                    System.Diagnostics.Debug.WriteLine("Could not find GlowLayer in button template");
                    return;
                }

                System.Diagnostics.Debug.WriteLine("Found GlowLayer, starting animation");

                // Create opacity pulsing animation with smaller range
                var opacityAnimation = new DoubleAnimation
                {
                    From = 0.75,
                    To = 0.25,
                    Duration = new Duration(TimeSpan.FromSeconds(1.25)),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                };

                // Create storyboard
                _glowPulseStoryboard = new Storyboard();
                _glowPulseStoryboard.Children.Add(opacityAnimation);

                // Set target for opacity
                Storyboard.SetTarget(opacityAnimation, glowLayer);
                Storyboard.SetTargetProperty(opacityAnimation, "Opacity");

                // Start animation
                _glowPulseStoryboard.Begin();
                System.Diagnostics.Debug.WriteLine("Started glow pulse animation");
            };
            timer.Start();
        });
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"Error starting glow animation: {ex.Message}");
    }
}

        private void StopGlowPulseAnimation()
        {
            try
            {
                _glowPulseStoryboard?.Stop();
                _glowPulseStoryboard = null;

                // Reset button to original style - use the existing global button style
                AltTabButton.ClearValue(FrameworkElement.StyleProperty);

                System.Diagnostics.Debug.WriteLine("Stopped glow pulse animation");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error stopping glow animation: {ex.Message}");
            }
        }

        // Helper method to find child elements by name
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

    }
}