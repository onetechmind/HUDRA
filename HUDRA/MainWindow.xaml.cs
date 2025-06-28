using HUDRA.Services;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Gaming.Input;
using WinRT;
using WinRT.Interop;

namespace HUDRA
{
    public sealed partial class MainWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string _currentTdpDisplayText = "Current TDP: Not Set";
        public string CurrentTdpDisplayText
        {
            get => _currentTdpDisplayText;
            set
            {
                if (_currentTdpDisplayText != value)
                {
                    _currentTdpDisplayText = value;
                    OnPropertyChanged();
                }
            }
        }

        private DesktopAcrylicController? _acrylicController;
        private SystemBackdropConfiguration? _backdropConfig;
        private IntPtr _hwnd;
        private bool _isDragging = false;
        private Windows.Graphics.PointInt32 _lastPointerPosition;
        private int _selectedTdp = 15;
        private bool _isScrolling = false;
        private DispatcherTimer? _autoSetTimer;
        private int _pendingTdpValue;
        private bool _isAutoSetting = false;

        // TDP Configuration - Change these to easily modify the range
        private const int MIN_TDP = 5;
        private const int MAX_TDP = 30;
        private int TotalTdpCount => MAX_TDP - MIN_TDP + 1; // 26 items (5-30)

        // DPI-aware fields
        private double _currentScaleFactor = 1.0;
        private readonly double _baseNumberWidth = 50.0;

        // Properties for DPI scaling
        private double NumberWidth => _baseNumberWidth * _currentScaleFactor;
        private double ItemWidth => NumberWidth + 15.0; // Use fixed 15px spacing from XAML

        private AudioService _audioService;
        private bool _isCurrentlyMuted = false; // Track state in the UI
        private Windows.Foundation.Point _lastTouchPosition;
        private bool _isUsingTouchDrag = false;
        private Windows.Graphics.PointInt32 _touchStartWindowPos;
        private bool _touchDragStarted = false;

        // Gamepad fields
        private DispatcherTimer? _gamepadTimer;
        private bool _gamepadLeftPressed = false;
        private bool _gamepadRightPressed = false;
        private bool _gamepadAPressed = false;
        private bool _gamepadBPressed = false;
        private int _selectedControlIndex = 0; // 0 = TDP selector, 1 = Mute button
        private const int TOTAL_CONTROLS = 2;
        private bool _gamepadUpPressed = false;
        private bool _gamepadDownPressed = false;

        public MainWindow()
        {
            this.InitializeComponent();
            this.Title = "HUDRA Control Center";

            LayoutRoot.DataContext = this;
            _hwnd = WindowNative.GetWindowHandle(this);

            // Initialize DPI awareness BEFORE other setup
            UpdateCurrentDpiScale();

            TrySetAcrylicBackdrop();
            SetInitialSize();
            MakeBorderlessWithRoundedCorners();
            ApplyRoundedCornersToWindow();
            SetupDragHandling();
            LoadCurrentTdp();
            InitializeTdpPicker();
            SetupTdpScrollViewerEvents();

            // Initialize audio service and set initial button state
            _audioService = new AudioService();

            // Initialize auto-set timer (add this after InitializeTdpPicker)
            _autoSetTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1000) // 1 second delay
            };
            _autoSetTimer.Tick += (s, e) =>
            {
                _autoSetTimer.Stop();
                AutoSetTdp();
            };

            SetupGamepadInput();

            // Monitor DPI changes
            this.SizeChanged += MainWindow_SizeChanged;
        }

        private void MuteButton_Click(object sender, RoutedEventArgs e)
        {
            _audioService.ToggleMute();

            // Toggle our UI state
            _isCurrentlyMuted = !_isCurrentlyMuted;

            // Update button text
            if (_isCurrentlyMuted)
            {
                MuteButton.Content = "Unmute";
            }
            else
            {
                MuteButton.Content = "Mute";
            }
        }

        private void InitializeTdpPicker()
        {
            // Update DPI scaling first
            UpdateCurrentDpiScale();

            // Clear any existing children
            NumbersPanel.Children.Clear();

            // We'll set the actual padding when layout is complete
            var startPadding = new Border { Width = 100 }; // Temporary value
            NumbersPanel.Children.Add(startPadding);

            // Create number TextBlocks dynamically based on TDP range
            for (int tdpValue = MIN_TDP; tdpValue <= MAX_TDP; tdpValue++)
            {
                var textBlock = new TextBlock
                {
                    Text = tdpValue.ToString(),
                    FontSize = 24,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Width = NumberWidth,
                    Opacity = tdpValue == _selectedTdp ? 1.0 : 0.4
                };
                NumbersPanel.Children.Add(textBlock);
            }

            // Add end padding
            var endPadding = new Border { Width = 100 }; // Temporary value
            NumbersPanel.Children.Add(endPadding);

            // IMPORTANT: Use multiple layout events to ensure proper positioning
            bool hasScrolledToInitialPosition = false;

            void SetupPaddingAndScroll()
            {
                if (TdpScrollViewer.ActualWidth <= 0) return;

                // Apply the same padding adjustment here too
                var paddingAdjustment = 7.0 * _currentScaleFactor;
                var adjustedHalfWidth = (TdpScrollViewer.ActualWidth / 2) + paddingAdjustment;

                startPadding.Width = adjustedHalfWidth;
                endPadding.Width = adjustedHalfWidth;

                System.Diagnostics.Debug.WriteLine($"Setting up adjusted padding: {adjustedHalfWidth:F1}, adjustment: {paddingAdjustment:F1}");

                DebugItemPositions();
                NumbersPanel.UpdateLayout();

                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                timer.Tick += (sender, args) =>
                {
                    timer.Stop();
                    ScrollToTdpSimple(_selectedTdp);
                };
                timer.Start();
            }

            LayoutRoot.Loaded += (s, e) => SetupPaddingAndScroll();
            TdpScrollViewer.Loaded += (s, e) => SetupPaddingAndScroll();
            TdpScrollViewer.SizeChanged += (s, e) =>
            {
                if (e.NewSize.Width > 0 && !hasScrolledToInitialPosition)
                {
                    SetupPaddingAndScroll();
                    hasScrolledToInitialPosition = true;
                }
            };
        }

        private void ScrollToTdpSimple(int tdpValue)
        {
            // TEST: Add obvious debug to verify changes are working
            System.Diagnostics.Debug.WriteLine("*** ScrollToTdpSimple called - CHANGES ARE WORKING ***");

            // ... rest of method stays the same for now ...

            // Wait for ScrollViewer to be properly sized
            if (TdpScrollViewer.ActualWidth <= 0)
            {
                System.Diagnostics.Debug.WriteLine("ScrollViewer not ready, deferring scroll");
                return;
            }

            var tdpIndex = tdpValue - MIN_TDP;
            var scrollViewerWidth = TdpScrollViewer.ActualWidth;
            var scrollViewerCenter = scrollViewerWidth / 2;
            var startPadding = scrollViewerCenter;

            // Calculate the left edge of this number
            var numberLeftEdge = startPadding + (tdpIndex * ItemWidth);

            // Calculate the center of this number
            var numberCenter = numberLeftEdge + (NumberWidth / 2);

            // Calculate scroll position to center this number
            var targetScrollPosition = numberCenter - scrollViewerCenter;

            // Clamp to valid range
            var maxScroll = Math.Max(0, TdpScrollViewer.ExtentWidth - TdpScrollViewer.ViewportWidth);
            targetScrollPosition = Math.Max(0, Math.Min(maxScroll, targetScrollPosition));

            System.Diagnostics.Debug.WriteLine($"ScrollToTdp({tdpValue}): targetScroll={targetScrollPosition:F1}");

            TdpScrollViewer.ScrollToHorizontalOffset(targetScrollPosition);
        }

        private int GetCenteredTdpAdjusted()
        {
            var scrollOffset = TdpScrollViewer.HorizontalOffset;
            var scrollViewerWidth = TdpScrollViewer.ActualWidth;
            var scrollViewerCenter = scrollViewerWidth / 2;

            // Apply the same padding adjustment
            var paddingAdjustment = 7.0 * _currentScaleFactor; // Same value as above
            var startPadding = scrollViewerCenter + paddingAdjustment;

            // The absolute position of the center of the visible area
            var visibleCenterPosition = scrollOffset + scrollViewerCenter;

            // Calculate which number's center is closest to the visible center
            var adjustedPosition = visibleCenterPosition - startPadding - (NumberWidth / 2);
            var slotIndex = Math.Round(adjustedPosition / ItemWidth);

            var tdpValue = (int)(slotIndex + MIN_TDP);
            var result = Math.Max(MIN_TDP, Math.Min(MAX_TDP, tdpValue));

            // Verify our calculation
            var calculatedIndex = result - MIN_TDP;
            var calculatedNumberCenter = startPadding + (calculatedIndex * ItemWidth) + (NumberWidth / 2);
            var distance = Math.Abs(visibleCenterPosition - calculatedNumberCenter);

            System.Diagnostics.Debug.WriteLine($"GetCenteredTdpAdjusted: paddingAdj={paddingAdjustment:F1}, TDP={result}, distance={distance:F1}");

            return result;
        }
        private async void LoadCurrentTdp()
        {
            try
            {
                var tdpService = new TDPService();
                var result = tdpService.GetCurrentTdp();

                if (result.Success)
                {
                    // Clamp to our valid range
                    _selectedTdp = Math.Max(MIN_TDP, Math.Min(MAX_TDP, result.TdpWatts));
                    CurrentTdpDisplayText = $"Current TDP: {_selectedTdp}W";

                    System.Diagnostics.Debug.WriteLine($"Loaded current TDP: {_selectedTdp}W");

                    // Update the picker wheel to show current value
                    UpdateNumberOpacity();

                    // If the ScrollViewer is ready, scroll to the correct position
                    if (TdpScrollViewer.ActualWidth > 0)
                    {
                        ScrollToTdpSimple(_selectedTdp);
                    }
                }
                else
                {
                    CurrentTdpDisplayText = $"TDP Status: {result.Message}";
                }
            }
            catch (Exception ex)
            {
                CurrentTdpDisplayText = $"Error reading TDP: {ex.Message}";
            }
        }

        private void TdpScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (_isScrolling) return;

            var centeredTdp = GetCenteredTdpAdjusted();

            // Update selection if it changed
            if (centeredTdp != _selectedTdp)
            {
                _selectedTdp = centeredTdp;
                UpdateNumberOpacity();

                // Trigger TDP setting with delay
                _pendingTdpValue = _selectedTdp;
                _autoSetTimer?.Stop();
                _autoSetTimer?.Start();
            }

            // Snap to position when scrolling stops
            if (!e.IsIntermediate)
            {
                SnapToCurrentTdp();
            }
        }

        private void SnapToCurrentTdp()
        {
            _isScrolling = true;
            ScrollToTdpSimple(_selectedTdp);

            // Reset scrolling flag after animation
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            timer.Tick += (s, args) =>
            {
                _isScrolling = false;
                timer.Stop();
            };
            timer.Start();
        }

        private void UpdateNumberOpacity()
        {
            // Update all number TextBlocks (skip first and last elements which are padding borders)
            for (int i = 1; i <= TotalTdpCount; i++) // Now correctly uses dynamic count
            {
                if (i < NumbersPanel.Children.Count - 1 && NumbersPanel.Children[i] is TextBlock textBlock)
                {
                    var tdpValue = (i - 1) + MIN_TDP; // Convert index back to TDP value
                    textBlock.Opacity = tdpValue == _selectedTdp ? 1.0 : 0.4;
                    textBlock.FontSize = tdpValue == _selectedTdp ? 28 : 24;
                }
            }
        }


        private void SetupDragHandling()
        {
            MainBorder.PointerPressed += OnPointerPressed;
            MainBorder.PointerMoved += OnPointerMoved;
            MainBorder.PointerReleased += OnPointerReleased;
        }

        private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // Handle both mouse and touch input
            if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse ||
                e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch)
            {
                var properties = e.GetCurrentPoint(MainBorder).Properties;

                // For mouse, check left button. For touch, primary contact is always "pressed"
                bool shouldStartDrag = (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse && properties.IsLeftButtonPressed) ||
                                      (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch);

                if (shouldStartDrag)
                {
                    // Check if the click is over the TDP picker area
                    var position = e.GetCurrentPoint(MainBorder);

                    try
                    {
                        // Get the bounds of the TDP picker border using the named element
                        var transform = TdpPickerBorder.TransformToVisual(MainBorder);
                        var borderBounds = transform.TransformBounds(new Windows.Foundation.Rect(0, 0,
                            TdpPickerBorder.ActualWidth, TdpPickerBorder.ActualHeight));

                        // If click is inside TDP picker, don't start window dragging
                        if (borderBounds.Contains(position.Position))
                        {
                            return; // Exit early - let the ScrollViewer handle this
                        }

                        // Also check if clicking on the mute button
                        var muteButtonTransform = MuteButton.TransformToVisual(MainBorder);
                        var muteButtonBounds = muteButtonTransform.TransformBounds(new Windows.Foundation.Rect(0, 0,
                            MuteButton.ActualWidth, MuteButton.ActualHeight));

                        if (muteButtonBounds.Contains(position.Position))
                        {
                            return; // Let the button handle the click
                        }
                    }
                    catch
                    {
                        // If bounds detection fails, allow dragging
                    }

                    // Start window dragging for both mouse and touch
                    _isDragging = true;
                    _isUsingTouchDrag = e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch;
                    MainBorder.CapturePointer(e.Pointer);

                    if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse)
                    {
                        GetCursorPos(out POINT cursorPos);
                        _lastPointerPosition = new Windows.Graphics.PointInt32(cursorPos.X, cursorPos.Y);
                    }
                    else // Touch
                    {
                        // For touch, use screen coordinates directly
                        var screenPoint = position.Position;

                        // Convert to screen coordinates
                        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
                        var appWindow = AppWindow.GetFromWindowId(windowId);
                        var windowPos = appWindow.Position;

                        _lastTouchPosition = new Windows.Foundation.Point(
                            windowPos.X + screenPoint.X,
                            windowPos.Y + screenPoint.Y);

                        _touchDragStarted = true;
                    }
                }
            }
        }

        private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_isDragging && (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse ||
                               e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch))
            {
                var properties = e.GetCurrentPoint(MainBorder).Properties;

                // For mouse, check if button is still pressed. For touch, if we're getting moved events, contact is maintained
                bool shouldContinueDrag = (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse && properties.IsLeftButtonPressed) ||
                                         (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch);

                if (shouldContinueDrag)
                {
                    var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
                    var appWindow = AppWindow.GetFromWindowId(windowId);

                    if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse)
                    {
                        // Mouse handling (unchanged)
                        GetCursorPos(out POINT cursorPos);
                        var currentPosition = new Windows.Graphics.PointInt32(cursorPos.X, cursorPos.Y);

                        int deltaX = currentPosition.X - _lastPointerPosition.X;
                        int deltaY = currentPosition.Y - _lastPointerPosition.Y;

                        var currentPos = appWindow.Position;
                        appWindow.Move(new Windows.Graphics.PointInt32(
                            currentPos.X + deltaX,
                            currentPos.Y + deltaY));

                        _lastPointerPosition = currentPosition;
                    }
                    else // Touch - use delta-based movement like mouse
                    {
                        if (_touchDragStarted)
                        {
                            var currentTouchPoint = e.GetCurrentPoint(MainBorder);
                            var windowPos = appWindow.Position;

                            // Current touch position in screen coordinates
                            var currentScreenTouch = new Windows.Foundation.Point(
                                windowPos.X + currentTouchPoint.Position.X,
                                windowPos.Y + currentTouchPoint.Position.Y);

                            // Calculate delta from last position
                            double deltaX = currentScreenTouch.X - _lastTouchPosition.X;
                            double deltaY = currentScreenTouch.Y - _lastTouchPosition.Y;

                            // Move window by delta
                            var newX = windowPos.X + (int)deltaX;
                            var newY = windowPos.Y + (int)deltaY;

                            appWindow.Move(new Windows.Graphics.PointInt32(newX, newY));

                            // Update last position for next delta calculation
                            _lastTouchPosition = currentScreenTouch;
                        }
                    }
                }
                else
                {
                    _isDragging = false;
                    _isUsingTouchDrag = false;
                    _touchDragStarted = false;
                }
            }
        }

        private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _isDragging = false;
            _isUsingTouchDrag = false;
            _touchDragStarted = false;
            MainBorder.ReleasePointerCapture(e.Pointer);
        }

        private void SetInitialSize()
        {
            var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            // Scale the base logical size by current DPI
            var logicalWidth = 320.0;
            var logicalHeight = 450.0;
            var scaledWidth = (int)Math.Round(logicalWidth * _currentScaleFactor);
            var scaledHeight = (int)Math.Round(logicalHeight * _currentScaleFactor);

            appWindow.Resize(new Windows.Graphics.SizeInt32(scaledWidth, scaledHeight));
        }

        private void MakeBorderlessWithRoundedCorners()
        {
            var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);

            var presenter = appWindow.Presenter as OverlappedPresenter;
            if (presenter != null)
            {
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.SetBorderAndTitleBar(false, false);
            }

            appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
        }

        private void ApplyRoundedCornersToWindow()
        {
            var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            // Set window corner preference to rounded
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                // This sets the window itself to have rounded corners
                presenter.SetBorderAndTitleBar(false, false);
            }

            // Use Windows 11 rounded corners if available
            try
            {
                var hwnd = WindowNative.GetWindowHandle(this);
                var preference = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(hwnd, 33, ref preference, sizeof(int)); // DWMWA_WINDOW_CORNER_PREFERENCE
            }
            catch
            {
                // Fallback for older Windows versions - rounded corners not supported
            }
        }

        private bool TrySetAcrylicBackdrop()
        {
            if (DesktopAcrylicController.IsSupported())
            {
                _backdropConfig = new SystemBackdropConfiguration
                {
                    IsInputActive = true,
                    Theme = SystemBackdropTheme.Dark
                };

                _acrylicController = new DesktopAcrylicController();
                _acrylicController.AddSystemBackdropTarget(this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
                _acrylicController.SetSystemBackdropConfiguration(_backdropConfig);

                return true;
            }
            return false;
        }

        private async void AutoSetTdp()
        {
            if (_isAutoSetting) return; // Rate limiting - prevent multiple simultaneous calls

            _isAutoSetting = true;

            try
            {
                var tdpService = new TDPService();
                int targetTdp = _pendingTdpValue;
                int tdpInMilliwatts = targetTdp * 1000;

                var result = tdpService.SetTdp(tdpInMilliwatts);

                if (result.Success)
                    CurrentTdpDisplayText = $"Current TDP: {targetTdp}W";
                else
                    CurrentTdpDisplayText = $"Error: {result.Message}";
            }
            catch (Exception ex)
            {
                CurrentTdpDisplayText = $"Error: {ex.Message}";
            }
            finally
            {
                _isAutoSetting = false;
            }
        }

        private void SetupTdpScrollViewerEvents()
        {
            double _lastPointerX = 0;
            bool _isManualScrolling = false;

            // Mouse/pen pointer events (existing logic)
            TdpScrollViewer.PointerPressed += (s, e) =>
            {
                _lastPointerX = e.GetCurrentPoint(TdpScrollViewer).Position.X;

                // Only set manual scrolling for mouse, not touch
                if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse)
                {
                    _isManualScrolling = true;
                    _isScrolling = true;
                    TdpScrollViewer.CapturePointer(e.Pointer);
                    e.Handled = true;
                }
            };

            TdpScrollViewer.PointerMoved += (s, e) =>
            {
                if (_isManualScrolling && e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse)
                {
                    var currentX = e.GetCurrentPoint(TdpScrollViewer).Position.X;
                    var deltaX = _lastPointerX - currentX;
                    deltaX *= 0.8;

                    var newScrollPosition = TdpScrollViewer.HorizontalOffset + deltaX;
                    var maxScroll = TdpScrollViewer.ExtentWidth - TdpScrollViewer.ViewportWidth;
                    newScrollPosition = Math.Max(0, Math.Min(maxScroll, newScrollPosition));

                    TdpScrollViewer.ScrollToHorizontalOffset(newScrollPosition);
                    _lastPointerX = currentX;
                }
            };

            TdpScrollViewer.PointerReleased += (s, e) =>
            {
                if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse && _isManualScrolling)
                {
                    _isManualScrolling = false;
                    _isScrolling = false;
                    TdpScrollViewer.ReleasePointerCapture(e.Pointer);
                    HandleScrollEnd();
                }
            };

            // Backup method: Use a scroll position change detector
            double _lastScrollPosition = 0;
            DispatcherTimer? _scrollStopTimer = null;

            TdpScrollViewer.ViewChanged += (s, e) =>
            {
                var currentPosition = ((ScrollViewer)s).HorizontalOffset;

                // If we're not manually scrolling (mouse) and position changed
                if (!_isManualScrolling && Math.Abs(currentPosition - _lastScrollPosition) > 0.1)
                {
                    _lastScrollPosition = currentPosition;

                    // Reset the timer - scroll is still happening
                    _scrollStopTimer?.Stop();
                    _scrollStopTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                    _scrollStopTimer.Tick += (sender, args) =>
                    {
                        _scrollStopTimer.Stop();
                        HandleScrollEnd();
                    };
                    _scrollStopTimer.Start();
                }
            };
        }

        private void HandleScrollEnd()
        {
            var centeredTdp = GetCenteredTdpAdjusted();

            if (centeredTdp != _selectedTdp)
            {
                _selectedTdp = centeredTdp;
                UpdateNumberOpacity();

                _pendingTdpValue = _selectedTdp;
                _autoSetTimer?.Stop();
                _autoSetTimer?.Start();
            }

            SnapToCurrentTdp();
        }

        private void ChangeTdpBy(int delta)
        {
            var newTdp = Math.Max(MIN_TDP, Math.Min(MAX_TDP, _selectedTdp + delta));

            if (newTdp != _selectedTdp)
            {
                _selectedTdp = newTdp;
                UpdateNumberOpacity();

                // Scroll to new position
                ScrollToTdpSimple(_selectedTdp);

                // Trigger auto-set after scroll animation
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                timer.Tick += (s, args) =>
                {
                    timer.Stop();
                    _pendingTdpValue = _selectedTdp;
                    _autoSetTimer?.Stop();
                    _autoSetTimer?.Start();
                };
                timer.Start();
            }
        }

        private void SetupGamepadInput()
        {
            // Check for gamepads every 100ms
            _gamepadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _gamepadTimer.Tick += GamepadTimer_Tick;
            _gamepadTimer.Start();

            // Initialize UI selection
            UpdateControlSelection();
        }

        private void GamepadTimer_Tick(object sender, object e)
        {
            var gamepads = Gamepad.Gamepads;
            if (gamepads.Count == 0) return;

            var gamepad = gamepads[0]; // Use first gamepad
            var reading = gamepad.GetCurrentReading();

            // Check D-pad for TDP control (when TDP selector is selected)
            if (_selectedControlIndex == 0)
            {
                bool leftPressed = (reading.Buttons & GamepadButtons.DPadLeft) != 0;
                bool rightPressed = (reading.Buttons & GamepadButtons.DPadRight) != 0;

                // Only trigger on button press (not hold)
                if (leftPressed && !_gamepadLeftPressed)
                {
                    ChangeTdpBy(-1);
                }
                else if (rightPressed && !_gamepadRightPressed)
                {
                    ChangeTdpBy(1);
                }

                _gamepadLeftPressed = leftPressed;
                _gamepadRightPressed = rightPressed;
            }

            // Check D-pad up/down for control selection
            bool upPressed = (reading.Buttons & GamepadButtons.DPadUp) != 0;
            bool downPressed = (reading.Buttons & GamepadButtons.DPadDown) != 0;

            if (upPressed && !_gamepadUpPressed)
            {
                _selectedControlIndex = (_selectedControlIndex - 1 + TOTAL_CONTROLS) % TOTAL_CONTROLS;
                UpdateControlSelection();
            }
            else if (downPressed && !_gamepadDownPressed)
            {
                _selectedControlIndex = (_selectedControlIndex + 1) % TOTAL_CONTROLS;
                UpdateControlSelection();
            }

            // Check A button for activation
            bool aPressed = (reading.Buttons & GamepadButtons.A) != 0;
            if (aPressed && !_gamepadAPressed)
            {
                ActivateSelectedControl();
            }

            _gamepadUpPressed = upPressed;
            _gamepadDownPressed = downPressed;
            _gamepadAPressed = aPressed;
        }

        private void UpdateControlSelection()
        {
            switch (_selectedControlIndex)
            {
                case 0: // TDP Selector
                    TdpPickerBorder.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
                    TdpPickerBorder.BorderThickness = new Microsoft.UI.Xaml.Thickness(1);
                    TdpPickerBorder.Shadow = new ThemeShadow();

                    // Remove effects from mute button
                    MuteButton.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    MuteButton.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
                    MuteButton.Shadow = null;
                    break;

                case 1: // Mute Button
                        // Remove effects from TDP picker
                    TdpPickerBorder.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    TdpPickerBorder.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
                    TdpPickerBorder.Shadow = null;

                    // Add effects to mute button
                    MuteButton.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
                    MuteButton.BorderThickness = new Microsoft.UI.Xaml.Thickness(1);
                    MuteButton.Shadow = new ThemeShadow();
                    break;
            }
        }

        private Border? FindTdpPickerBorder()
        {
            try
            {
                var stackPanel = (StackPanel)LayoutRoot.Children[0];
                var tdpSection = (StackPanel)stackPanel.Children[0];
                return (Border)tdpSection.Children[1];
            }
            catch
            {
                return null;
            }
        }

        private void ActivateSelectedControl()
        {
            switch (_selectedControlIndex)
            {
                case 0: // TDP Selector - no specific action needed, use left/right to control
                    break;
                case 1: // Mute Button
                    MuteButton_Click(MuteButton, new RoutedEventArgs());
                    break;
            }
        }

        // DPI Helper Methods
        private void UpdateCurrentDpiScale()
        {
            try
            {
                var hwnd = WindowNative.GetWindowHandle(this);
                var dpi = GetDpiForWindow(hwnd);
                _currentScaleFactor = dpi / 96.0; // 96 DPI = 100% scale

                System.Diagnostics.Debug.WriteLine($"Current DPI scale factor: {_currentScaleFactor:F2}");
            }
            catch
            {
                _currentScaleFactor = 1.0; // Fallback to 100% scale
            }
        }

        private void UpdateTdpPickerSizing()
        {
            if (NumbersPanel?.Children == null) return;

            var halfScrollViewerWidth = TdpScrollViewer.ActualWidth / 2;

            // Update start padding
            if (NumbersPanel.Children.Count > 0 && NumbersPanel.Children[0] is Border startPadding)
            {
                startPadding.Width = halfScrollViewerWidth;
            }

            // Update end padding (last element)
            var lastIndex = NumbersPanel.Children.Count - 1;
            if (lastIndex > 0 && NumbersPanel.Children[lastIndex] is Border endPadding)
            {
                endPadding.Width = halfScrollViewerWidth;
            }

            // Update number TextBlocks with current DPI-scaled width
            for (int i = 1; i <= TotalTdpCount; i++)
            {
                if (i < NumbersPanel.Children.Count - 1 && NumbersPanel.Children[i] is TextBlock textBlock)
                {
                    textBlock.Width = NumberWidth;
                }
            }
        }

        private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs args)
        {
            // Check if DPI changed and update accordingly
            var oldScaleFactor = _currentScaleFactor;
            UpdateCurrentDpiScale();

            if (Math.Abs(_currentScaleFactor - oldScaleFactor) > 0.01)
            {
                System.Diagnostics.Debug.WriteLine($"DPI changed from {oldScaleFactor:F2} to {_currentScaleFactor:F2}");
                UpdateTdpPickerSizing();

                // Re-scroll to current position with new scaling
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                timer.Tick += (s, args) =>
                {
                    timer.Stop();
                    ScrollToTdpSimple(_selectedTdp);
                };
                timer.Start();
            }
        }
        private void DebugItemPositions()
        {
            if (TdpScrollViewer.ActualWidth <= 0) return;

            var scrollViewerWidth = TdpScrollViewer.ActualWidth;
            var startPadding = scrollViewerWidth / 2;

            System.Diagnostics.Debug.WriteLine($"=== TDP Item Positions Debug ===");
            System.Diagnostics.Debug.WriteLine($"ScrollViewer Width: {scrollViewerWidth:F1}");
            System.Diagnostics.Debug.WriteLine($"NumberWidth (DPI-scaled): {NumberWidth:F1}");
            System.Diagnostics.Debug.WriteLine($"XAML Spacing: 15.0");
            System.Diagnostics.Debug.WriteLine($"ItemWidth (calculated): {ItemWidth:F1}");
            System.Diagnostics.Debug.WriteLine($"Start Padding: {startPadding:F1}");
            System.Diagnostics.Debug.WriteLine($"DPI Scale Factor: {_currentScaleFactor:F2}");

            for (int tdp = MIN_TDP; tdp <= Math.Min(MIN_TDP + 5, MAX_TDP); tdp++)
            {
                var index = tdp - MIN_TDP;
                var leftEdge = startPadding + (index * ItemWidth);
                var center = leftEdge + (NumberWidth / 2);
                var rightEdge = leftEdge + NumberWidth;

                System.Diagnostics.Debug.WriteLine($"TDP {tdp}: Left={leftEdge:F1}, Center={center:F1}, Right={rightEdge:F1}");
            }
            System.Diagnostics.Debug.WriteLine($"================================");
        }

        // P/Invoke declarations
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }
    }
}