using HUDRA.Configuration;
using HUDRA.Controls;
using HUDRA.Pages;
using HUDRA.Services;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Numerics;
using System;
using System.IO;
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
        private readonly TdpMonitorService? _tdpMonitor;
        private MainPage? _mainPage;
        private SettingsPage? _settingsPage;
        private TurboService? _turboService;
        private BatteryService? _batteryService;
        private DispatcherTimer? _batteryTimer;
        private bool _batteryAnimating = false;

        private DesktopAcrylicController? _acrylicController;
        private SystemBackdropConfiguration? _backdropConfig;

        //Drag Handling
        private bool _isDragging = false;
        private Windows.Graphics.PointInt32 _lastPointerPosition;
        private Windows.Foundation.Point _lastTouchPosition;
        private bool _touchDragStarted = false;

        private string _currentResolutionDisplayText = "Resolution: Not Set";
        public string CurrentResolutionDisplayText
        {
            get => _currentResolutionDisplayText;
            set { _currentResolutionDisplayText = value; OnPropertyChanged(); }
        }

        private string _currentRefreshRateDisplayText = "Refresh Rate: Not Set";
        public string CurrentRefreshRateDisplayText
        {
            get => _currentRefreshRateDisplayText;
            set { _currentRefreshRateDisplayText = value; OnPropertyChanged(); }
        }

        private string _batteryToolTipText = string.Empty;
        public string BatteryToolTipText
        {
            get => _batteryToolTipText;
            set { _batteryToolTipText = value; OnPropertyChanged(); }
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
            _tdpMonitor = (App.Current as App)?.TdpMonitor;
            _batteryService = new BatteryService();

            InitializeWindow();

            ContentFrame.Loaded += ContentFrame_Loaded;
            SetupEventHandlers();
            SetupDragHandling();
            SetupBatteryService();
            SetupTurboService();

            this.Closed += (s, e) => Cleanup();
        }

        private void InitializeWindow()
        {
            TrySetAcrylicBackdrop();
            _windowManager.Initialize();
        }

        private void InitializeControls()
        {
            if (_mainPage == null) return;

            _mainPage.Initialize(_dpiService, _resolutionService, _audioService, _brightnessService);
            _mainPage.SettingsRequested += (s, e) => SettingsButton_Click(s, new RoutedEventArgs());
            _mainPage.ResolutionPicker.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ResolutionPickerControl.ResolutionStatusText))
                    CurrentResolutionDisplayText = _mainPage.ResolutionPicker.ResolutionStatusText;
                else if (e.PropertyName == nameof(ResolutionPickerControl.RefreshRateStatusText))
                    CurrentRefreshRateDisplayText = _mainPage.ResolutionPicker.RefreshRateStatusText;
            };

            _mainPage.AudioControls.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(AudioControlsControl.AudioStatusText))
                {
                    System.Diagnostics.Debug.WriteLine(_mainPage.AudioControls.AudioStatusText);
                }
            };

            _mainPage.BrightnessControls.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(BrightnessControlControl.BrightnessStatusText))
                {
                    System.Diagnostics.Debug.WriteLine(_mainPage.BrightnessControls.BrightnessStatusText);
                }
            };

            if (_tdpMonitor != null)
            {
                _tdpMonitor.UpdateTargetTdp(_mainPage.TdpPicker.SelectedTdp);
                _mainPage.TdpPicker.TdpChanged += (s, value) => _tdpMonitor.UpdateTargetTdp(value);

                _tdpMonitor.TdpDriftDetected += (s, args) =>
                {
                    System.Diagnostics.Debug.WriteLine($"TDP drift {args.CurrentTdp}W -> {args.TargetTdp}W (corrected: {args.CorrectionApplied})");
                };

                if (SettingsService.GetTdpCorrectionEnabled())
                {
                    _tdpMonitor.Start();
                }
            }
        }

        private void SetupDragHandling()
        {
            MainBorder.PointerPressed += OnMainBorderPointerPressed;
            MainBorder.PointerMoved += OnMainBorderPointerMoved;
            MainBorder.PointerReleased += OnMainBorderPointerReleased;
        }

        private void SetupEventHandlers()
        {
            // Window events
            this.SizeChanged += MainWindow_SizeChanged;
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

        private void SetupBatteryService()
        {
            if (_batteryService == null)
                return;

            _batteryService.PowerStatusChanged += (s, e) => UpdateBatteryStatus();

            _batteryTimer = new DispatcherTimer
            {
                Interval = HudraSettings.BATTERY_UPDATE_INTERVAL
            };
            _batteryTimer.Tick += (s, e) => UpdateBatteryStatus();
            _batteryTimer.Start();

            UpdateBatteryStatus();
        }

        private void ContentFrame_Loaded(object sender, RoutedEventArgs e)
        {
            ContentFrame.Loaded -= ContentFrame_Loaded;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                _navigationService.Navigate(typeof(MainPage));
                _mainPage = ContentFrame.Content as MainPage;
                if (_mainPage != null)
                {
                    InitializeControls();
                }
            });
        }

        // Event handlers
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _windowManager.ToggleVisibility();
        }

        private void AltTabButton_Click(object sender, RoutedEventArgs e)
        {
            _windowManager.ToggleVisibility();
            SimulateAltTab();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            _navigationService.Navigate(typeof(SettingsPage));

            // Delay the setup slightly to ensure navigation completes
            DispatcherQueue.TryEnqueue(() =>
            {
                if (ContentFrame.Content is SettingsPage sp)
                {
                    _settingsPage = sp;
                    sp.BackButton.Click += BackButton_Click;
                }
            });
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            _navigationService.GoBack();
        }
        private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs args)
        {
            var oldScaleFactor = _dpiService.ScaleFactor;
            _dpiService.UpdateScaleFactor();

            if (_dpiService.HasScaleChanged(oldScaleFactor))
            {
                // Handle DPI change
                _windowManager.PositionAboveSystemTray();
            }
        }
        private void SimulateAltTab()
        {
            // Implementation for Alt+Tab simulation
            keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
            keybd_event(VK_TAB, 0, 0, UIntPtr.Zero);
            keybd_event(VK_TAB, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private void UpdateBatteryStatus()
        {
            if (_batteryService == null) return;

            var info = _batteryService.GetStatus();

            BatteryText.Text = $"{info.Percentage}%";
            BatteryIcon.Glyph = GetBatteryGlyph(info.Percentage, info.IsCharging);
            BatteryIcon.Foreground = new Windows.UI.Xaml.Media.SolidColorBrush(
                info.IsCharging ? Windows.UI.Colors.DarkViolet : Windows.UI.Colors.White);

            if (info.IsCharging)
                StartBatteryAnimation();
            else
                StopBatteryAnimation();

            BatteryToolTipText = BuildBatteryToolTip(info);
        }

        private static string GetBatteryGlyph(int percent, bool charging)
        {
            int level = Math.Clamp((int)Math.Round(percent / 10.0), 0, 10);
            if (charging)
            {
                if (level >= 9)
                    return "\uE83E"; // BatteryCharging9
                return char.ConvertFromUtf32(0xE85A + level);
            }
            else
            {
                if (level >= 10)
                    return "\uE83F"; // Battery10
                return char.ConvertFromUtf32(0xE850 + level);
            }
        }

        private string BuildBatteryToolTip(BatteryInfo info)
        {
            string source = info.IsAcPowered ? "AC" : "Battery";
            if (info.RemainingTime.HasValue)
            {
                var t = info.RemainingTime.Value;
                return $"Power source: {source}\nTime remaining: {(int)t.TotalHours}h {t.Minutes}m";
            }
            return $"Power source: {source}";
        }

        private void StartBatteryAnimation()
        {
            if (_batteryAnimating) return;

            var visual = ElementCompositionPreview.GetElementVisual(BatteryIndicator);
            var compositor = visual.Compositor;
            visual.CenterPoint = new Vector3((float)(BatteryIndicator.ActualWidth / 2), (float)(BatteryIndicator.ActualHeight / 2), 0f);

            var anim = compositor.CreateScalarKeyFrameAnimation();
            anim.InsertKeyFrame(0f, 1f);
            anim.InsertKeyFrame(0.5f, 1.1f);
            anim.InsertKeyFrame(1f, 1f);
            anim.Duration = TimeSpan.FromSeconds(1.5);
            anim.IterationBehavior = AnimationIterationBehavior.Forever;

            visual.StartAnimation("Scale.X", anim);
            var animY = compositor.CreateScalarKeyFrameAnimation();
            animY.InsertKeyFrame(0f, 1f);
            animY.InsertKeyFrame(0.5f, 1.1f);
            animY.InsertKeyFrame(1f, 1f);
            animY.Duration = TimeSpan.FromSeconds(1.5);
            animY.IterationBehavior = AnimationIterationBehavior.Forever;
            visual.StartAnimation("Scale.Y", animY);

            _batteryAnimating = true;
        }

        private void StopBatteryAnimation()
        {
            if (!_batteryAnimating) return;

            var visual = ElementCompositionPreview.GetElementVisual(BatteryIndicator);
            visual.StopAnimation("Scale.X");
            visual.StopAnimation("Scale.Y");
            visual.Scale = new Vector3(1f, 1f, 1f);

            _batteryAnimating = false;
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
                _acrylicController.TintColor = Windows.UI.Color.FromArgb(20, 0, 0, 0);
                _acrylicController.TintOpacity = 0.1f;
                _acrylicController.LuminosityOpacity = 0.1f;

                _acrylicController.AddSystemBackdropTarget(this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
                _acrylicController.SetSystemBackdropConfiguration(_backdropConfig);

                return true;
            }
            return false;
        }

        private void Cleanup()
        {
            _mainPage?.TdpPicker?.Dispose();
            _windowManager?.Dispose();
            _turboService?.Dispose();
            _acrylicController?.Dispose();
            _tdpMonitor?.Dispose();
            _batteryTimer?.Stop();
            _batteryService?.Dispose();
        }

        public void ToggleWindowVisibility() => _windowManager.ToggleVisibility();

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // P/Invoke declarations
        [DllImport("user32.dll", SetLastError = true)]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const byte VK_MENU = 0x12;
        private const byte VK_TAB = 0x09;
        private const uint KEYEVENTF_KEYUP = 0x0002;

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

                    // Check if click is over any UserControl - if so, don't drag
                    try
                    {
                        if (_mainPage != null)
                        {
                            var tdpTransform = _mainPage.TdpPicker.TransformToVisual(MainBorder);
                            var tdpBounds = tdpTransform.TransformBounds(new Windows.Foundation.Rect(0, 0, _mainPage.TdpPicker.ActualWidth, _mainPage.TdpPicker.ActualHeight));
                            if (tdpBounds.Contains(position.Position)) return;

                            var resolutionTransform = _mainPage.ResolutionPicker.TransformToVisual(MainBorder);
                            var resolutionBounds = resolutionTransform.TransformBounds(new Windows.Foundation.Rect(0, 0, _mainPage.ResolutionPicker.ActualWidth, _mainPage.ResolutionPicker.ActualHeight));
                            if (resolutionBounds.Contains(position.Position)) return;

                            var audioTransform = _mainPage.AudioControls.TransformToVisual(MainBorder);
                            var audioBounds = audioTransform.TransformBounds(new Windows.Foundation.Rect(0, 0, _mainPage.AudioControls.ActualWidth, _mainPage.AudioControls.ActualHeight));
                            if (audioBounds.Contains(position.Position)) return;

                            var brightnessTransform = _mainPage.BrightnessControls.TransformToVisual(MainBorder);
                            var brightnessBounds = brightnessTransform.TransformBounds(new Windows.Foundation.Rect(0, 0, _mainPage.BrightnessControls.ActualWidth, _mainPage.BrightnessControls.ActualHeight));
                            if (brightnessBounds.Contains(position.Position)) return;
                        }

                        // Check buttons
                        var closeTransform = CloseButton.TransformToVisual(MainBorder);
                        var closeBounds = closeTransform.TransformBounds(new Windows.Foundation.Rect(0, 0, CloseButton.ActualWidth, CloseButton.ActualHeight));
                        if (closeBounds.Contains(position.Position)) return;

                        var altTabTransform = AltTabButton.TransformToVisual(MainBorder);
                        var altTabBounds = altTabTransform.TransformBounds(new Windows.Foundation.Rect(0, 0, AltTabButton.ActualWidth, AltTabButton.ActualHeight));
                        if (altTabBounds.Contains(position.Position)) return;

                        var batteryTransform = BatteryIndicator.TransformToVisual(MainBorder);
                        var batteryBounds = batteryTransform.TransformBounds(new Windows.Foundation.Rect(0, 0, BatteryIndicator.ActualWidth, BatteryIndicator.ActualHeight));
                        if (batteryBounds.Contains(position.Position)) return;
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
                    else // Touch
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
            if (_isDragging)
            {
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
                    else // Touch
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
        }

        private void OnMainBorderPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            _isDragging = false;
            _touchDragStarted = false;
            MainBorder.ReleasePointerCapture(e.Pointer);
        }

        // ADD the P/Invoke if it's missing:
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }
    }
}