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
        private const double ItemWidth = 65.0; // 50 width + 15 spacing
        private AudioService _audioService;
        private bool _isCurrentlyMuted = false; // Track state in the UI
        private Windows.Foundation.Point _lastTouchPosition;
        private bool _isUsingTouchDrag = false;
        private Windows.Graphics.PointInt32 _touchStartWindowPos;
        private bool _touchDragStarted = false;

        //gamepad fields
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
            // Add padding elements at start and end for proper centering
            var startPadding = new Border { Width = 115 }; // Half of container width minus half of item width
            NumbersPanel.Children.Add(startPadding);

            // Create number TextBlocks from 5 to 30
            for (int i = 5; i <= 30; i++)
            {
                var textBlock = new TextBlock
                {
                    Text = i.ToString(),
                    FontSize = 24,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Width = 50,
                    Opacity = i == _selectedTdp ? 1.0 : 0.4 // Highlight actual loaded TDP
                };
                NumbersPanel.Children.Add(textBlock);
            }

            var endPadding = new Border { Width = 115 };
            NumbersPanel.Children.Add(endPadding);

            // Set initial scroll position to center on current TDP
            // Set initial scroll position to center on current TDP
            LayoutRoot.Loaded += (s, e) =>
            {
                var targetIndex = _selectedTdp - 5;
                // Use the same calculation as ViewChanged - center the number in the scroll viewer
                var numberCenterPosition = 115 + (targetIndex * ItemWidth) + 25; // +25 for half the number width
                var scrollViewerWidth = TdpScrollViewer.ActualWidth;
                var targetScrollPosition = numberCenterPosition - (scrollViewerWidth / 2);
                TdpScrollViewer.ScrollToHorizontalOffset(targetScrollPosition);
            };
        }

        private async void LoadCurrentTdp()
        {
            try
            {
                var tdpService = new TDPService();
                var result = tdpService.GetCurrentTdp();

                if (result.Success)
                {
                    // Clamp to our valid range (5-30)
                    _selectedTdp = Math.Max(5, Math.Min(30, result.TdpWatts));
                    CurrentTdpDisplayText = $"Current TDP: {_selectedTdp}W";

                    // Update the picker wheel to show current value
                    UpdateNumberOpacity();

                    
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

            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer == null) return;

            // Calculate which number is closest to center
            var scrollOffset = scrollViewer.HorizontalOffset;
            var scrollViewerWidth = scrollViewer.ActualWidth;
            var centerPosition = scrollOffset + (scrollViewerWidth / 2);

            var itemWidth = ItemWidth;
            var adjustedOffset = centerPosition - 115; // Subtract start padding
            var centerIndex = Math.Round(adjustedOffset / itemWidth);
            var selectedValue = (int)(centerIndex + 5);

            // Clamp to valid range
            selectedValue = Math.Max(5, Math.Min(30, selectedValue));

            if (selectedValue != _selectedTdp)
            {
                _selectedTdp = selectedValue;
                UpdateNumberOpacity();

                // Auto-set TDP with delay and rate limiting
                _pendingTdpValue = _selectedTdp;
                _autoSetTimer?.Stop(); // Cancel any existing timer
                _autoSetTimer?.Start(); // Start new countdown
            }

            // Add snapping when scrolling stops
            if (!e.IsIntermediate) // This indicates the scroll has stopped
            {
                var targetIndex = _selectedTdp - 5;
                // Calculate position to center the selected number in the scroll viewer
                var numberCenterPosition = 115 + (targetIndex * itemWidth) + 25; // +25 for half the number width (50/2)
                var targetScrollPosition = numberCenterPosition - (scrollViewerWidth / 2);

                // Only snap if we're not already at the correct position (avoid infinite loops)
                if (Math.Abs(scrollOffset - targetScrollPosition) > 1)
                {
                    _isScrolling = true;
                    scrollViewer.ScrollToHorizontalOffset(targetScrollPosition);

                    // Reset the scrolling flag after a short delay
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                    timer.Tick += (s, args) =>
                    {
                        _isScrolling = false;
                        timer.Stop();
                    };
                    timer.Start();
                }
            }
        }

        private void UpdateNumberOpacity()
        {
            // Skip the first and last elements (padding borders)
            for (int i = 1; i <= 26; i++) // Numbers are at indices 1-26 (after start padding)
            {
                var textBlock = NumbersPanel.Children[i] as TextBlock;
                if (textBlock != null)
                {
                    var value = i + 4; // i=1 corresponds to value=5, so value = i+4
                    textBlock.Opacity = value == _selectedTdp ? 1.0 : 0.4;
                    textBlock.FontSize = value == _selectedTdp ? 28 : 24;
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
            appWindow.Resize(new Windows.Graphics.SizeInt32(320, 450));
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
            var scrollOffset = TdpScrollViewer.HorizontalOffset;
            var scrollViewerWidth = TdpScrollViewer.ActualWidth;
            var centerPosition = scrollOffset + (scrollViewerWidth / 2);

            var adjustedOffset = centerPosition - 115;
            var centerIndex = Math.Round(adjustedOffset / ItemWidth);
            var selectedValue = (int)(centerIndex + 5);

            selectedValue = Math.Max(5, Math.Min(30, selectedValue));

            if (selectedValue != _selectedTdp)
            {
                _selectedTdp = selectedValue;
                UpdateNumberOpacity();

                _pendingTdpValue = _selectedTdp;
                _autoSetTimer?.Stop();
                _autoSetTimer?.Start();
            }

            // Force snapping
            var targetIndex = _selectedTdp - 5;
            var numberCenterPosition = 115 + (targetIndex * ItemWidth) + 25;
            var targetScrollPosition = numberCenterPosition - (scrollViewerWidth / 2);

            if (Math.Abs(scrollOffset - targetScrollPosition) > 1)
            {
                _isScrolling = true;
                TdpScrollViewer.ScrollToHorizontalOffset(targetScrollPosition);

                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                timer.Tick += (sender, args) =>
                {
                    _isScrolling = false;
                    timer.Stop();
                };
                timer.Start();
            }
        }

        private void ChangeTdpBy(int delta)
        {
            var newTdp = Math.Max(5, Math.Min(30, _selectedTdp + delta));

            if (newTdp != _selectedTdp)
            {
                _selectedTdp = newTdp;
                UpdateNumberOpacity();

                // Scroll to the new position
                var targetIndex = _selectedTdp - 5;
                var numberCenterPosition = 115 + (targetIndex * ItemWidth) + 25;
                var scrollViewerWidth = TdpScrollViewer.ActualWidth;
                var targetScrollPosition = numberCenterPosition - (scrollViewerWidth / 2);

                _isScrolling = true;
                TdpScrollViewer.ScrollToHorizontalOffset(targetScrollPosition);

                // Reset scrolling flag and trigger auto-set
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                timer.Tick += (s, args) =>
                {
                    _isScrolling = false;
                    timer.Stop();

                    // Trigger auto-set
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

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    }
}