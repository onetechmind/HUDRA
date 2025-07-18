using HUDRA.Configuration;
using HUDRA.Services;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
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
        private readonly BatteryService _batteryService;
        private TdpMonitorService? _tdpMonitor;
        private TurboService? _turboService;
        private MicaController? _micaController;
        private SystemBackdropConfiguration? _backdropConfig;

        private bool _isDragging;
        private Windows.Graphics.PointInt32 _lastPointerPosition;
        private Windows.Foundation.Point _lastTouchPosition;
        private bool _touchDragStarted;

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
            InitializeComponent();
            Title = "HUDRA";
            LayoutRoot.DataContext = this;

            _dpiService = new DpiScalingService(this);
            _windowManager = new WindowManagementService(this, _dpiService);
            _audioService = new AudioService();
            _brightnessService = new BrightnessService();
            _resolutionService = new ResolutionService();
            _batteryService = new BatteryService(DispatcherQueue);
            _batteryService.BatteryInfoUpdated += OnBatteryInfoUpdated;

            InitializeWindow();

            SetupEventHandlers();
            SetupDragHandling();
            SetupTurboService();

            Loaded += MainWindow_Loaded;
            Closed += (s, e) => Cleanup();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Shell.InitializeServices(_dpiService, _resolutionService, _audioService, _brightnessService);
            if (_tdpMonitor != null)
            {
                Shell.SetTdpMonitor(_tdpMonitor);
            }
        }

        private void InitializeWindow()
        {
            TrySetMicaBackdrop();
            _windowManager.Initialize();
        }

        private void SetupEventHandlers()
        {
            SizeChanged += MainWindow_SizeChanged;
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

        private void Cleanup()
        {
            Shell.MainPage?.TdpPicker?.Dispose();
            _windowManager?.Dispose();
            _turboService?.Dispose();
            _micaController?.Dispose();
            _tdpMonitor?.Dispose();
            _batteryService?.Dispose();
        }

        public void ToggleWindowVisibility() => _windowManager.ToggleVisibility();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        private void SetupDragHandling()
        {
            MainBorder.PointerPressed += OnMainBorderPointerPressed;
            MainBorder.PointerMoved += OnMainBorderPointerMoved;
            MainBorder.PointerReleased += OnMainBorderPointerReleased;
        }

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

                    try
                    {
                        if (Shell.MainPage != null)
                        {
                            var tdpTransform = Shell.MainPage.TdpPicker.TransformToVisual(MainBorder);
                            var tdpBounds = tdpTransform.TransformBounds(new Windows.Foundation.Rect(0, 0, Shell.MainPage.TdpPicker.ActualWidth, Shell.MainPage.TdpPicker.ActualHeight));
                            if (tdpBounds.Contains(position.Position)) return;

                            var resTransform = Shell.MainPage.ResolutionPicker.TransformToVisual(MainBorder);
                            var resBounds = resTransform.TransformBounds(new Windows.Foundation.Rect(0, 0, Shell.MainPage.ResolutionPicker.ActualWidth, Shell.MainPage.ResolutionPicker.ActualHeight));
                            if (resBounds.Contains(position.Position)) return;

                            var audioTransform = Shell.MainPage.AudioControls.TransformToVisual(MainBorder);
                            var audioBounds = audioTransform.TransformBounds(new Windows.Foundation.Rect(0, 0, Shell.MainPage.AudioControls.ActualWidth, Shell.MainPage.AudioControls.ActualHeight));
                            if (audioBounds.Contains(position.Position)) return;

                            var brightTransform = Shell.MainPage.BrightnessControls.TransformToVisual(MainBorder);
                            var brightBounds = brightTransform.TransformBounds(new Windows.Foundation.Rect(0, 0, Shell.MainPage.BrightnessControls.ActualWidth, Shell.MainPage.BrightnessControls.ActualHeight));
                            if (brightBounds.Contains(position.Position)) return;
                        }

                        if (Shell.SettingsPage != null)
                        {
                            var startupTransform = Shell.SettingsPage.StartupTdpPicker.TransformToVisual(MainBorder);
                            var startupBounds = startupTransform.TransformBounds(new Windows.Foundation.Rect(0, 0, Shell.SettingsPage.StartupTdpPicker.ActualWidth, Shell.SettingsPage.StartupTdpPicker.ActualHeight));
                            if (startupBounds.Contains(position.Position)) return;

                            var toggleTransform = Shell.SettingsPage.TdpCorrectionToggle.TransformToVisual(MainBorder);
                            var toggleBounds = toggleTransform.TransformBounds(new Windows.Foundation.Rect(0, 0, Shell.SettingsPage.TdpCorrectionToggle.ActualWidth, Shell.SettingsPage.TdpCorrectionToggle.ActualHeight));
                            if (toggleBounds.Contains(position.Position)) return;
                        }

                        var batteryTransform = BatteryPanel.TransformToVisual(MainBorder);
                        var batteryBounds = batteryTransform.TransformBounds(new Windows.Foundation.Rect(0, 0, BatteryPanel.ActualWidth, BatteryPanel.ActualHeight));
                        if (batteryBounds.Contains(position.Position)) return;
                    }
                    catch
                    {
                        // Ignore hit test failures
                    }

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
        }

        private void OnMainBorderPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            _isDragging = false;
            _touchDragStarted = false;
            MainBorder.ReleasePointerCapture(e.Pointer);
        }

        public void SetTdpMonitor(TdpMonitorService tdpMonitor)
        {
            _tdpMonitor = tdpMonitor;
            Shell.SetTdpMonitor(tdpMonitor);
        }

        private void OnBatteryInfoUpdated(object? sender, BatteryInfo info)
        {
            BatteryPercentageText = $"{info.Percent}%";
            BatteryIcon.Glyph = GetBatteryGlyph(info.Percent, info.IsCharging);
            BatteryIcon.Foreground = new SolidColorBrush(info.IsCharging ? Microsoft.UI.Colors.DarkGreen : Microsoft.UI.Colors.White);
            BatteryTextBrush = new SolidColorBrush(info.IsCharging ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black);

            string timeStr = info.RemainingDischargeTime == TimeSpan.Zero ? "--" : info.RemainingDischargeTime.ToString(@"hh\:mm");
            BatteryToolTip = $"{info.Percent}% - {(info.IsCharging ? "Charging" : info.OnAc ? "Plugged in" : "On battery")}\nTime remaining: {timeStr}";
        }

        private static string GetBatteryGlyph(int percent, bool charging)
        {
            int index = Math.Clamp(percent / 10, 0, 10);
            if (charging)
            {
                if (index >= 9) return "\uE83E";
                return char.ConvertFromUtf32(0xE85A + index);
            }
            else
            {
                if (index >= 10) return "\uE83F";
                return char.ConvertFromUtf32(0xE850 + index);
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

