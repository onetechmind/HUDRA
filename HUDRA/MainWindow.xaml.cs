using HUDRA.Services;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
            if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse)
            {
                var properties = e.GetCurrentPoint(MainBorder).Properties;
                if (properties.IsLeftButtonPressed)
                {
                    _isDragging = true;
                    MainBorder.CapturePointer(e.Pointer);

                    GetCursorPos(out POINT cursorPos);
                    _lastPointerPosition = new Windows.Graphics.PointInt32(cursorPos.X, cursorPos.Y);
                }
            }
        }

        private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_isDragging && e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse)
            {
                var properties = e.GetCurrentPoint(MainBorder).Properties;
                if (properties.IsLeftButtonPressed)
                {
                    GetCursorPos(out POINT cursorPos);

                    int deltaX = cursorPos.X - _lastPointerPosition.X;
                    int deltaY = cursorPos.Y - _lastPointerPosition.Y;

                    var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
                    var appWindow = AppWindow.GetFromWindowId(windowId);
                    var currentPos = appWindow.Position;

                    appWindow.Move(new Windows.Graphics.PointInt32(
                        currentPos.X + deltaX,
                        currentPos.Y + deltaY));

                    _lastPointerPosition = new Windows.Graphics.PointInt32(cursorPos.X, cursorPos.Y);
                }
                else
                {
                    _isDragging = false;
                }
            }
        }

        private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _isDragging = false;
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