using HUDRA.Services;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
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
        private bool _isWindowVisible = true;
        private bool _isDragging = false;
        private Windows.Graphics.PointInt32 _lastPointerPosition;
        private int _selectedTdp = 15;
        private bool _isScrolling = false;
        private DispatcherTimer? _autoSetTimer;
        private int _pendingTdpValue;
        private bool _isAutoSetting = false;
        private TurboService? _turboService;

        // TDP Configuration - Change these to easily modify the range
        private const int MIN_TDP = 5;
        private const int MAX_TDP = 30;
        private int TotalTdpCount => MAX_TDP - MIN_TDP + 1; // 26 items (5-30)

        // DPI-aware fields
        private double _currentScaleFactor = 1.0;
        private readonly double _baseNumberWidth = 35.0;
        private readonly double _baseSpacing = 0.0;

        // Properties for DPI scaling
        private readonly double _baseBorderPadding = 5.0;  // Match XAML Padding="5,0"
        private readonly double _baseScrollPadding = 10.0; // Match XAML Padding="0,10"

        private double BorderPadding => _baseBorderPadding * _currentScaleFactor;
        private double ScrollPadding => _baseScrollPadding * _currentScaleFactor;
        private double NumberWidth => _baseNumberWidth * _currentScaleFactor;
        private double SpacingWidth => _baseSpacing * _currentScaleFactor;
        private double ItemWidth => NumberWidth + SpacingWidth;

        private AudioService _audioService;
        private bool _isCurrentlyMuted = false; // Track state in the UI
        private bool _suppressVolumeEvent = false;
        private double _lastVolumeBeforeMute = 0; // Remember last volume before muting
        private BrightnessService _brightnessService;
        private bool _suppressBrightnessEvent = false;
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
        private int _selectedControlIndex = 0; // 0 = TDP selector, 1 = Resolution, 2 = Refresh Rate, 3 = Mute button, 4 = Volume slider, 5 = Brightness slider
        private const int TOTAL_CONTROLS = 6;
        private bool _gamepadUpPressed = false;
        private bool _gamepadDownPressed = false;
        private bool _isComboBoxPopupOpen = false;

        // Resolution-related fields
        private ResolutionService _resolutionService;
        private List<ResolutionService.Resolution> _availableResolutions;
        private int _selectedResolutionIndex = 0;
        private bool _isResolutionScrolling = false;
        private DispatcherTimer? _resolutionAutoSetTimer;
        private int _pendingResolutionIndex;
        private bool _isResolutionAutoSetting = false;

        // Refresh rate-related fields
        private List<int> _availableRefreshRates;
        private int _selectedRefreshRateIndex = 0;
        private DispatcherTimer? _refreshRateAutoSetTimer;
        private int _pendingRefreshRateIndex;
        private bool _isRefreshRateAutoSetting = false;

        // References to dynamic padding borders used in the TDP picker
        private Border? _tdpStartPadding;
        private Border? _tdpEndPadding;

        // Add this property for binding
        private string _currentResolutionDisplayText = "Current Resolution: Not Set";
        public string CurrentResolutionDisplayText
        {
            get => _currentResolutionDisplayText;
            set
            {
                if (_currentResolutionDisplayText != value)
                {
                    _currentResolutionDisplayText = value;
                    OnPropertyChanged();
                }
            }
        }

        // ADD this property for status display:
        private string _currentRefreshRateDisplayText = "Refresh Rate: Not Set";
        public string CurrentRefreshRateDisplayText
        {
            get => _currentRefreshRateDisplayText;
            set
            {
                if (_currentRefreshRateDisplayText != value)
                {
                    _currentRefreshRateDisplayText = value;
                    OnPropertyChanged();
                }
            }
        }


        public MainWindow()
        {
            this.InitializeComponent();
            this.Title = "HUDRA";

            LayoutRoot.DataContext = this;
            _hwnd = WindowNative.GetWindowHandle(this);

            this.Closed += (s, e) =>
            {
                _turboService?.Dispose();
            };

            // Initialize DPI awareness BEFORE other setup
            UpdateCurrentDpiScale();
            TrySetAcrylicBackdrop();
            SetInitialSize();
            MakeBorderlessWithRoundedCorners();
            ApplyRoundedCornersToWindow();
            PositionAboveSystemTray();
            SetupDragHandling();
            LoadCurrentTdp();
            InitializeTdpPicker();
            InitializeResolutionPicker();
            InitializeRefreshRatePicker();
            SetupTdpScrollViewerEvents();

            // Initialize audio service and set initial button state
            _audioService = new AudioService();
            _isCurrentlyMuted = _audioService.GetMuteStatus();
            UpdateMuteButtonIcon();

            // Set initial volume slider value
            _suppressVolumeEvent = true;
            var initialVolume = _audioService.GetMasterVolumeScalar() * 100.0;
            VolumeSlider.Value = initialVolume;
            _lastVolumeBeforeMute = initialVolume;
            _suppressVolumeEvent = false;

            // Initialize brightness service and set initial slider value
            _brightnessService = new BrightnessService();
            _suppressBrightnessEvent = true;
            var initialBrightness = _brightnessService.GetBrightness();
            BrightnessSlider.Value = initialBrightness;
            _suppressBrightnessEvent = false;

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
            SetupTurboService();

            // Monitor DPI changes
            this.SizeChanged += MainWindow_SizeChanged;
        }

        private void SetupTurboService()
        {
            try
            {
                _turboService = new TurboService();
                _turboService.TurboButtonPressed += OnTurboButtonPressed;
                System.Diagnostics.Debug.WriteLine("TurboService connected successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TurboService setup failed: {ex.Message}");
                // App continues to work without turbo button functionality
                _turboService = null;
            }
        }

        private void OnTurboButtonPressed(object? sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("MainWindow: Turbo button pressed - toggling visibility");

            // Use DispatcherQueue to ensure UI updates happen on the UI thread
            DispatcherQueue.TryEnqueue(() =>
            {
                ToggleWindowVisibility();
            });
        }

        private void ToggleWindowVisibility()
        {
            try
            {
                var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
                var appWindow = AppWindow.GetFromWindowId(windowId);

                if (_isWindowVisible)
                {
                    appWindow.Hide();
                    System.Diagnostics.Debug.WriteLine("Window hidden via turbo button");
                }
                else
                {
                    appWindow.Show();
                    PositionAboveSystemTray(); // Reposition when showing
                    System.Diagnostics.Debug.WriteLine("Window shown via turbo button");
                }

                _isWindowVisible = !_isWindowVisible;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to toggle window visibility: {ex.Message}");
            }
        }
        public class TransparentBackdrop : SystemBackdrop
        {
            protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
            {
                // Do nothing - keep transparent
            }

            protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
            {
                // Do nothing - keep transparent
            }

            protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
            {
                // Do nothing
            }
        }

        private void MuteButton_Click(object sender, RoutedEventArgs e)
        {
            _audioService.ToggleMute();

            // Toggle our UI state
            _isCurrentlyMuted = !_isCurrentlyMuted;
            UpdateMuteButtonIcon();

            // Reflect mute state on the slider without affecting system volume
            _suppressVolumeEvent = true;
            if (_isCurrentlyMuted)
            {
                _lastVolumeBeforeMute = VolumeSlider.Value;
                VolumeSlider.Value = 0;
            }
            else
            {
                VolumeSlider.Value = _lastVolumeBeforeMute;
            }
            _suppressVolumeEvent = false;
        }

        private void UpdateMuteButtonIcon()
        {
            if (_isCurrentlyMuted)
            {
                MuteButtonIcon.Glyph = "\uE74F"; // Mute
            }
            else
            {
                MuteButtonIcon.Glyph = "\uE767"; // Volume
            }
        }

        private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_suppressVolumeEvent) return;

            if (_isCurrentlyMuted)
            {
                _audioService.ToggleMute();
                _isCurrentlyMuted = false;
                UpdateMuteButtonIcon();
            }

            var level = (float)(e.NewValue / 100.0);
            _audioService.SetMasterVolumeScalar(level);

            _lastVolumeBeforeMute = e.NewValue;
        }

        private void BrightnessSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_suppressBrightnessEvent) return;
            _brightnessService.SetBrightness((int)e.NewValue);
        }

        private void InitializeTdpPicker()
        {
            // Update DPI scaling first
            UpdateCurrentDpiScale();

            // Clear any existing children
            NumbersPanel.Children.Clear();

            // Set DPI-aware spacing
            NumbersPanel.Spacing = SpacingWidth;

            // We'll set the actual padding when layout is complete
            var startPadding = new Border { Width = 100 }; // Temporary value
            NumbersPanel.Children.Add(startPadding);
            _tdpStartPadding = startPadding;

            // Create number TextBlocks dynamically based on TDP range
            for (int tdpValue = MIN_TDP; tdpValue <= MAX_TDP; tdpValue++)
            {
                var textBlock = new TextBlock
                {
                    Text = tdpValue.ToString(),
                    FontSize = 24,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Code"),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    Width = NumberWidth,
                    Opacity = tdpValue == _selectedTdp ? 1.0 : 0.4
                };
                NumbersPanel.Children.Add(textBlock);
            }

            // Add end padding
            var endPadding = new Border { Width = 100 }; // Temporary value
            NumbersPanel.Children.Add(endPadding);
            _tdpEndPadding = endPadding;

            // IMPORTANT: Use multiple layout events to ensure proper positioning
            bool hasScrolledToInitialPosition = false;


            void SetupPaddingAndScroll()
            {
                if (TdpScrollViewer.ActualWidth <= 0) return;

                // Center the selected number regardless of the digit count
                var startPaddingWidth = (TdpScrollViewer.ActualWidth - ItemWidth) / 2;

                if (_tdpStartPadding != null)
                    _tdpStartPadding.Width = startPaddingWidth;
                if (_tdpEndPadding != null)
                    _tdpEndPadding.Width = startPaddingWidth;

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
            var tdpIndex = tdpValue - MIN_TDP;
            var scrollViewerWidth = TdpScrollViewer.ActualWidth;
            var scrollViewerCenter = scrollViewerWidth / 2;
            var startPadding = _tdpStartPadding?.Width ?? ((scrollViewerWidth - ItemWidth) / 2);


            // Calculate the left edge of this number
            var numberLeftEdge = startPadding + (tdpIndex * ItemWidth);

            // Calculate the center of this number
            var numberCenter = numberLeftEdge + (ItemWidth / 2);

            // Calculate scroll position to center this number
            var targetScrollPosition = numberCenter - scrollViewerCenter;

            // Clamp to valid range
            var maxScroll = Math.Max(0, TdpScrollViewer.ExtentWidth - TdpScrollViewer.ViewportWidth);
            targetScrollPosition = Math.Max(0, Math.Min(maxScroll, targetScrollPosition));

            TdpScrollViewer.ScrollToHorizontalOffset(targetScrollPosition);
        }

        private int GetCenteredTdpAdjusted()
        {
            var scrollOffset = TdpScrollViewer.HorizontalOffset;
            var scrollViewerWidth = TdpScrollViewer.ActualWidth;
            var scrollViewerCenter = scrollViewerWidth / 2;
            var startPadding = _tdpStartPadding?.Width ?? ((scrollViewerWidth - ItemWidth) / 2);


            var visibleCenterPosition = scrollOffset + scrollViewerCenter;
            var adjustedPosition = visibleCenterPosition - startPadding - (ItemWidth / 2);
            var slotIndex = Math.Round(adjustedPosition / ItemWidth);

            var tdpValue = (int)(slotIndex + MIN_TDP);
            return Math.Max(MIN_TDP, Math.Min(MAX_TDP, tdpValue));
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

                // Only restart timer if we're not currently processing a TDP change
                if (!_isAutoSetting)
                {
                    _pendingTdpValue = _selectedTdp;
                    _autoSetTimer?.Stop();
                    _autoSetTimer?.Start();
                }
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

                        // Check Resolution ComboBox bounds
                        var resolutionTransform = ResolutionComboBox.TransformToVisual(MainBorder);
                        var resolutionBounds = resolutionTransform.TransformBounds(new Windows.Foundation.Rect(0, 0,
                            ResolutionComboBox.ActualWidth, ResolutionComboBox.ActualHeight));

                        // If click is inside TDP picker, don't start window dragging
                        if (borderBounds.Contains(position.Position))
                        {
                            return; // Exit early - let the ScrollViewer handle this
                        }

                        if (resolutionBounds.Contains(position.Position))
                        {
                            return; // Exit early - let the ComboBox handle this
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

                // Make the acrylic much more transparent so your Border shows through
                _acrylicController.TintColor = Windows.UI.Color.FromArgb(20, 0, 0, 0); // Very transparent black
                _acrylicController.TintOpacity = 0.1f; // Very low opacity
                _acrylicController.LuminosityOpacity = 0.1f; // Very low luminosity

                _acrylicController.AddSystemBackdropTarget(this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
                _acrylicController.SetSystemBackdropConfiguration(_backdropConfig);

                return true;
            }
            return false;
        }

        private async void AutoSetTdp()
        {
            // Capture the current pending value and clear the timer immediately
            int targetTdp = _pendingTdpValue;

            // Stop the timer to prevent multiple calls
            _autoSetTimer?.Stop();

            // Quick check to prevent duplicate calls for the same value
            if (_isAutoSetting) return;

            _isAutoSetting = true;

            try
            {
                // Run the TDP setting operation asynchronously to not block UI
                await Task.Run(() =>
                {
                    var tdpService = new TDPService();
                    int tdpInMilliwatts = targetTdp * 1000;
                    var result = tdpService.SetTdp(tdpInMilliwatts);

                    // Update UI on the UI thread
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (result.Success)
                            CurrentTdpDisplayText = $"Current TDP: {targetTdp}W";
                        else
                            CurrentTdpDisplayText = $"Error: {result.Message}";
                    });
                });
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

                // Only trigger auto-set if not currently processing
                if (!_isAutoSetting)
                {
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

            var gamepad = gamepads[0];
            var reading = gamepad.GetCurrentReading();

            // Declare button states once at the top
            bool upPressed = (reading.Buttons & GamepadButtons.DPadUp) != 0;
            bool downPressed = (reading.Buttons & GamepadButtons.DPadDown) != 0;
            bool leftPressed = (reading.Buttons & GamepadButtons.DPadLeft) != 0;
            bool rightPressed = (reading.Buttons & GamepadButtons.DPadRight) != 0;
            bool aPressed = (reading.Buttons & GamepadButtons.A) != 0;
            bool bPressed = (reading.Buttons & GamepadButtons.B) != 0;

            // Check if any ComboBox popup is open
            _isComboBoxPopupOpen = ResolutionComboBox.IsDropDownOpen || RefreshRateComboBox.IsDropDownOpen;

            // If a popup is open, handle gamepad navigation within the popup
            if (_isComboBoxPopupOpen)
            {
                // Navigate within the open ComboBox
                if (ResolutionComboBox.IsDropDownOpen)
                {
                    if (upPressed && !_gamepadUpPressed)
                    {
                        NavigateComboBoxUp(ResolutionComboBox, ref _selectedResolutionIndex, _availableResolutions.Count);
                    }
                    else if (downPressed && !_gamepadDownPressed)
                    {
                        NavigateComboBoxDown(ResolutionComboBox, ref _selectedResolutionIndex, _availableResolutions.Count);
                    }
                    else if (aPressed && !_gamepadAPressed)
                    {
                        // Select current item and close popup
                        ResolutionComboBox.IsDropDownOpen = false;
                    }
                }
                else if (RefreshRateComboBox.IsDropDownOpen)
                {
                    if (upPressed && !_gamepadUpPressed)
                    {
                        NavigateComboBoxUp(RefreshRateComboBox, ref _selectedRefreshRateIndex, _availableRefreshRates.Count);
                    }
                    else if (downPressed && !_gamepadDownPressed)
                    {
                        NavigateComboBoxDown(RefreshRateComboBox, ref _selectedRefreshRateIndex, _availableRefreshRates.Count);
                    }
                    else if (aPressed && !_gamepadAPressed)
                    {
                        // Select current item and close popup
                        RefreshRateComboBox.IsDropDownOpen = false;
                    }
                }

                // B button closes popup without selecting
                if (bPressed && !_gamepadBPressed)
                {
                    ResolutionComboBox.IsDropDownOpen = false;
                    RefreshRateComboBox.IsDropDownOpen = false;
                }

                // Update button states
                _gamepadUpPressed = upPressed;
                _gamepadDownPressed = downPressed;
                _gamepadAPressed = aPressed;
                _gamepadBPressed = bPressed;
                return; // Exit early - don't process main UI navigation
            }

            // Rest of your existing gamepad logic for main UI navigation
            // Handle left/right based on selected control
            if (_selectedControlIndex == 0) // TDP
            {
                if (leftPressed && !_gamepadLeftPressed)
                    ChangeTdpBy(-1);
                else if (rightPressed && !_gamepadRightPressed)
                    ChangeTdpBy(1);
            }
            else if (_selectedControlIndex == 1) // Resolution
            {
                if (leftPressed && !_gamepadLeftPressed)
                    ChangeResolutionBy(-1);
                else if (rightPressed && !_gamepadRightPressed)
                    ChangeResolutionBy(1);
            }
            else if (_selectedControlIndex == 2) // Refresh Rate
            {
                if (leftPressed && !_gamepadLeftPressed)
                    ChangeRefreshRateBy(-1);
                else if (rightPressed && !_gamepadRightPressed)
                    ChangeRefreshRateBy(1);
            }
            else if (_selectedControlIndex == 3) // Mute Button
            {
                if (aPressed && !_gamepadAPressed)
                    MuteButton_Click(MuteButton, new RoutedEventArgs());
            }
            else if (_selectedControlIndex == 4) // Volume Slider
            {
                if (leftPressed && !_gamepadLeftPressed)
                    ChangeVolumeBy(-2);
                else if (rightPressed && !_gamepadRightPressed)
                    ChangeVolumeBy(2);
            }
            else if (_selectedControlIndex == 5) // Brightness Slider
            {
                if (leftPressed && !_gamepadLeftPressed)
                    ChangeBrightnessBy(-2);
                else if (rightPressed && !_gamepadRightPressed)
                    ChangeBrightnessBy(2);
            }

            _gamepadLeftPressed = leftPressed;
            _gamepadRightPressed = rightPressed;

            // Check D-pad up/down for control selection
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
            if (aPressed && !_gamepadAPressed)
            {
                ActivateSelectedControl();
            }

            _gamepadUpPressed = upPressed;
            _gamepadDownPressed = downPressed;
            _gamepadAPressed = aPressed;
            _gamepadBPressed = bPressed;
        }

        private void NavigateComboBoxUp(ComboBox comboBox, ref int selectedIndex, int totalCount)
        {
            if (totalCount == 0) return;

            selectedIndex = Math.Max(0, selectedIndex - 1);
            comboBox.SelectedIndex = selectedIndex;
        }

        private void NavigateComboBoxDown(ComboBox comboBox, ref int selectedIndex, int totalCount)
        {
            if (totalCount == 0) return;

            selectedIndex = Math.Min(totalCount - 1, selectedIndex + 1);
            comboBox.SelectedIndex = selectedIndex;
        }


        private void ChangeResolutionBy(int delta)
        {
            if (_availableResolutions == null || _availableResolutions.Count == 0) return;

            var newIndex = Math.Max(0, Math.Min(_availableResolutions.Count - 1, _selectedResolutionIndex + delta));

            if (newIndex != _selectedResolutionIndex)
            {
                _selectedResolutionIndex = newIndex;
                ResolutionComboBox.SelectedIndex = newIndex;

                // The SelectionChanged event will handle the auto-set timer
            }
        }

        private void UpdateControlSelection()
        {
            // Reset all borders first
            TdpPickerBorder.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            TdpPickerBorder.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
            TdpPickerBorder.Shadow = null;

            ResolutionComboBox.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            ResolutionComboBox.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);

            RefreshRateComboBox.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            RefreshRateComboBox.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);

            MuteButton.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            MuteButton.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
            MuteButton.Shadow = null;

            VolumeSlider.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            VolumeSlider.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);

            BrightnessSlider.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
            BrightnessSlider.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);

            switch (_selectedControlIndex)
            {
                case 0: // TDP Selector
                    TdpPickerBorder.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
                    TdpPickerBorder.BorderThickness = new Microsoft.UI.Xaml.Thickness(1);
                    TdpPickerBorder.Shadow = new ThemeShadow();
                    break;

                case 1: // Resolution Selector
                    ResolutionComboBox.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
                    ResolutionComboBox.BorderThickness = new Microsoft.UI.Xaml.Thickness(2);
                    break;

                case 2: // Refresh Rate Selector - NEW!
                    RefreshRateComboBox.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
                    RefreshRateComboBox.BorderThickness = new Microsoft.UI.Xaml.Thickness(2);
                    break;

                case 3: // Mute Button (was case 2)
                    MuteButton.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
                    MuteButton.BorderThickness = new Microsoft.UI.Xaml.Thickness(1);
                    MuteButton.Shadow = new ThemeShadow();
                    break;
                case 4: // Volume Slider
                    VolumeSlider.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
                    VolumeSlider.BorderThickness = new Microsoft.UI.Xaml.Thickness(1);
                    break;
                case 5: // Brightness Slider
                    BrightnessSlider.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
                    BrightnessSlider.BorderThickness = new Microsoft.UI.Xaml.Thickness(1);
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
                case 1: // Resolution Selector - open dropdown
                    ResolutionComboBox.IsDropDownOpen = true;
                    break;
                case 2: // Refresh Rate Selector - open dropdown - NEW!
                    RefreshRateComboBox.IsDropDownOpen = true;
                    break;
                case 3: // Mute Button (was case 2)
                    MuteButton_Click(MuteButton, new RoutedEventArgs());
                    break;
                case 4: // Volume Slider
                    // No action on A press
                    break;
                case 5: // Brightness Slider
                    // No action on A press
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
            }
            catch
            {
                _currentScaleFactor = 1.0; // Fallback to 100% scale
            }
        }

        private void UpdateTdpPickerSizing()
        {
            if (NumbersPanel?.Children == null) return;

            var startPaddingWidth = (TdpScrollViewer.ActualWidth - ItemWidth) / 2;

            if (_tdpStartPadding != null)
                _tdpStartPadding.Width = startPaddingWidth;

            if (_tdpEndPadding != null)
                _tdpEndPadding.Width = startPaddingWidth;


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

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool SystemParametersInfo(int uAction, int uParam, ref RECT lpvParam, int fuWinIni);

        private const int SPI_GETWORKAREA = 48;
        private void PositionAboveSystemTray()
        {
            try
            {
                var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
                var appWindow = AppWindow.GetFromWindowId(windowId);

                // Get work area (screen minus taskbar)
                var workArea = new RECT();
                SystemParametersInfo(SPI_GETWORKAREA, 0, ref workArea, 0);

                var padding = (int)(20 * _currentScaleFactor);
                var windowSize = appWindow.Size;

                var x = workArea.Right - windowSize.Width - padding;
                var y = workArea.Bottom - windowSize.Height - padding;

                appWindow.Move(new Windows.Graphics.PointInt32(x, y));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to position window: {ex.Message}");
            }
        }

        private void InitializeResolutionPicker()
        {
            _resolutionService = new ResolutionService();
            _availableResolutions = _resolutionService.GetAvailableResolutions();

            if (_availableResolutions.Count == 0)
            {
                CurrentResolutionDisplayText = "No resolutions available";
                ResolutionComboBox.IsEnabled = false;
                return;
            }

            // Populate ComboBox with strings
            ResolutionComboBox.ItemsSource = _availableResolutions.Select(r => r.DisplayText).ToList();

            // Find and select current resolution
            var currentRes = _resolutionService.GetCurrentResolution();
            if (currentRes.Success)
            {
                var match = _availableResolutions.FindIndex(r =>
                    r.Width == currentRes.CurrentResolution.Width &&
                    r.Height == currentRes.CurrentResolution.Height);

                if (match >= 0)
                {
                    _selectedResolutionIndex = match;
                    ResolutionComboBox.SelectedIndex = match;
                }
                CurrentResolutionDisplayText = $"Resolution: {currentRes.CurrentResolution.DisplayText}";
            }
            else
            {
                _selectedResolutionIndex = 0;
                ResolutionComboBox.SelectedIndex = 0;
                CurrentResolutionDisplayText = "Resolution: Unknown";
            }

            // Initialize auto-set timer
            _resolutionAutoSetTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _resolutionAutoSetTimer.Tick += (s, e) =>
            {
                _resolutionAutoSetTimer.Stop();
                AutoSetResolution();
            };

            ResolutionComboBox.DropDownOpened += (s, e) => _isComboBoxPopupOpen = true;
            ResolutionComboBox.DropDownClosed += (s, e) => _isComboBoxPopupOpen = false;
        }
        private void ResolutionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ResolutionComboBox.SelectedIndex < 0 ||
                _availableResolutions == null ||
                ResolutionComboBox.SelectedIndex >= _availableResolutions.Count ||
                _isResolutionAutoSetting)
                return;

            _selectedResolutionIndex = ResolutionComboBox.SelectedIndex;

            // Update refresh rates for the new resolution
            UpdateRefreshRatesForSelectedResolution();

            // Start the resolution auto-set timer
            _pendingResolutionIndex = _selectedResolutionIndex;
            _resolutionAutoSetTimer?.Stop();
            _resolutionAutoSetTimer?.Start();
        }
        private async void AutoSetResolution()
        {
            int targetIndex = _pendingResolutionIndex;
            _resolutionAutoSetTimer?.Stop();

            if (_isResolutionAutoSetting || targetIndex < 0 || targetIndex >= _availableResolutions.Count)
                return;

            _isResolutionAutoSetting = true;

            try
            {
                await Task.Run(() =>
                {
                    var targetResolution = _availableResolutions[targetIndex];

                    // Determine the refresh rate that should be applied. Use the currently
                    // selected refresh rate if available; otherwise fall back to the one
                    // stored with the resolution entry.
                    int refreshRate = targetResolution.RefreshRate;
                    if (_availableRefreshRates != null &&
                        _selectedRefreshRateIndex >= 0 &&
                        _selectedRefreshRateIndex < _availableRefreshRates.Count)
                    {
                        refreshRate = _availableRefreshRates[_selectedRefreshRateIndex];
                    }

                    var result = _resolutionService.SetRefreshRate(targetResolution, refreshRate);

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (result.Success)
                        {
                            CurrentResolutionDisplayText = $"Resolution: {targetResolution.DisplayText}";
                            CurrentRefreshRateDisplayText = $"Refresh Rate: {refreshRate}Hz";
                        }
                        else
                        {
                            CurrentResolutionDisplayText = $"Resolution Error: {result.Message}";
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                CurrentResolutionDisplayText = $"Resolution Error: {ex.Message}";
            }
            finally
            {
                _isResolutionAutoSetting = false;
            }
        }

        // ADD this method to initialize refresh rates (call it in your constructor after InitializeResolutionPicker()):
        private void InitializeRefreshRatePicker()
        {
            // Get refresh rates for the currently selected resolution
            if (_availableResolutions != null && _selectedResolutionIndex >= 0 && _selectedResolutionIndex < _availableResolutions.Count)
            {
                var currentResolution = _availableResolutions[_selectedResolutionIndex];
                _availableRefreshRates = _resolutionService.GetAvailableRefreshRates(currentResolution);
            }
            else
            {
                _availableRefreshRates = new List<int>();
            }

            if (_availableRefreshRates.Count == 0)
            {
                CurrentRefreshRateDisplayText = "No refresh rates available";
                RefreshRateComboBox.IsEnabled = false;
                return;
            }

            // Populate ComboBox with refresh rate strings
            RefreshRateComboBox.ItemsSource = _availableRefreshRates.Select(rate => $"{rate}Hz").ToList();

            // Find and select current refresh rate
            var currentRefreshRate = _resolutionService.GetCurrentRefreshRate();
            if (currentRefreshRate.Success)
            {
                var match = _availableRefreshRates.FindIndex(rate => rate == currentRefreshRate.RefreshRate);
                if (match >= 0)
                {
                    _selectedRefreshRateIndex = match;
                    RefreshRateComboBox.SelectedIndex = match;
                }
                CurrentRefreshRateDisplayText = $"Refresh Rate: {currentRefreshRate.RefreshRate}Hz";
            }
            else
            {
                _selectedRefreshRateIndex = 0;
                RefreshRateComboBox.SelectedIndex = 0;
                CurrentRefreshRateDisplayText = "Refresh Rate: Unknown";
            }

            // Initialize auto-set timer
            _refreshRateAutoSetTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300) // Same fast response as resolution
            };
            _refreshRateAutoSetTimer.Tick += (s, e) =>
            {
                _refreshRateAutoSetTimer.Stop();
                AutoSetRefreshRate();
            };

            RefreshRateComboBox.DropDownOpened += (s, e) => _isComboBoxPopupOpen = true;
            RefreshRateComboBox.DropDownClosed += (s, e) => _isComboBoxPopupOpen = false;
        }

        private void RefreshRateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RefreshRateComboBox.SelectedIndex < 0 ||
                _availableRefreshRates == null ||
                RefreshRateComboBox.SelectedIndex >= _availableRefreshRates.Count ||
                _isRefreshRateAutoSetting)
                return;

            _selectedRefreshRateIndex = RefreshRateComboBox.SelectedIndex;

            // Start the auto-set timer
            _pendingRefreshRateIndex = _selectedRefreshRateIndex;
            _refreshRateAutoSetTimer?.Stop();
            _refreshRateAutoSetTimer?.Start();
        }

        private async void AutoSetRefreshRate()
        {
            int targetIndex = _pendingRefreshRateIndex;
            _refreshRateAutoSetTimer?.Stop();

            if (_isRefreshRateAutoSetting || targetIndex < 0 || targetIndex >= _availableRefreshRates.Count)
                return;

            _isRefreshRateAutoSetting = true;

            try
            {
                await Task.Run(() =>
                {
                    var targetRefreshRate = _availableRefreshRates[targetIndex];
                    var currentResolution = _availableResolutions[_selectedResolutionIndex];
                    var result = _resolutionService.SetRefreshRate(currentResolution, targetRefreshRate);

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (result.Success)
                            CurrentRefreshRateDisplayText = $"Refresh Rate: {targetRefreshRate}Hz";
                        else
                            CurrentRefreshRateDisplayText = $"Refresh Rate Error: {result.Message}";
                    });
                });
            }
            catch (Exception ex)
            {
                CurrentRefreshRateDisplayText = $"Refresh Rate Error: {ex.Message}";
            }
            finally
            {
                _isRefreshRateAutoSetting = false;
            }
        }
        private void UpdateRefreshRatesForSelectedResolution()
        {
            if (_availableResolutions == null || _selectedResolutionIndex < 0 || _selectedResolutionIndex >= _availableResolutions.Count)
                return;

            var selectedResolution = _availableResolutions[_selectedResolutionIndex];

            // Remember the currently selected refresh rate so we can keep it if
            // the new resolution supports it.
            int previousRate = -1;
            if (_availableRefreshRates != null &&
                _selectedRefreshRateIndex >= 0 &&
                _selectedRefreshRateIndex < _availableRefreshRates.Count)
            {
                previousRate = _availableRefreshRates[_selectedRefreshRateIndex];
            }

            _availableRefreshRates = _resolutionService.GetAvailableRefreshRates(selectedResolution);

            RefreshRateComboBox.ItemsSource = _availableRefreshRates
                .Select(rate => $"{rate}Hz")
                .ToList();

            if (_availableRefreshRates.Count > 0)
            {
                // Try to keep the previously selected refresh rate if it's available
                int match = previousRate >= 0 ? _availableRefreshRates.FindIndex(r => r == previousRate) : -1;
                if (match >= 0)
                {
                    _selectedRefreshRateIndex = match;
                }
                else
                {
                    _selectedRefreshRateIndex = 0;
                }

                RefreshRateComboBox.SelectedIndex = _selectedRefreshRateIndex;
                RefreshRateComboBox.IsEnabled = true;
            }
            else
            {
                RefreshRateComboBox.IsEnabled = false;
                CurrentRefreshRateDisplayText = "No refresh rates available";
            }
        }
        private void ChangeRefreshRateBy(int delta)
        {
            if (_availableRefreshRates == null || _availableRefreshRates.Count == 0) return;

            var newIndex = Math.Max(0, Math.Min(_availableRefreshRates.Count - 1, _selectedRefreshRateIndex + delta));

            if (newIndex != _selectedRefreshRateIndex)
            {
                _selectedRefreshRateIndex = newIndex;
                RefreshRateComboBox.SelectedIndex = newIndex;
                // The SelectionChanged event will handle the auto-set timer
            }
        }

        private void ChangeVolumeBy(int delta)
        {
            var newValue = Math.Max(0, Math.Min(100, VolumeSlider.Value + delta));
            VolumeSlider.Value = newValue;
        }

        private void ChangeBrightnessBy(int delta)
        {
            var newValue = Math.Max(0, Math.Min(100, BrightnessSlider.Value + delta));
            BrightnessSlider.Value = newValue;
        }

    }
}
