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

        private MicaController? _micaController;
        private SystemBackdropConfiguration? _backdropConfig;
        private IntPtr _hwnd;
        private bool _isDragging = false;
        private Windows.Graphics.PointInt32 _lastPointerPosition;
        private int _selectedTdp = 15;
        private bool _isScrolling = false;

        public MainWindow()
        {
            this.InitializeComponent();
            this.Title = "HUDRA Control Center";

            LayoutRoot.DataContext = this;
            _hwnd = WindowNative.GetWindowHandle(this);

            TrySetMicaBackdrop();
            SetInitialSize();
            MakeBorderlessWithRoundedCorners();
            SetupDragHandling();
            InitializeTdpPicker();
            LoadCurrentTdp(); // Add this new method call
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
                    Opacity = i == 15 ? 1.0 : 0.4 // Highlight default selection
                };
                NumbersPanel.Children.Add(textBlock);
            }

            var endPadding = new Border { Width = 115 };
            NumbersPanel.Children.Add(endPadding);

            // Set initial scroll position to center on current TDP (will be updated by LoadCurrentTdp)
            LayoutRoot.Loaded += (s, e) =>
            {
                var targetIndex = _selectedTdp - 5;
                var scrollPosition = 115 + (targetIndex * 80);
                TdpScrollViewer.ScrollToHorizontalOffset(scrollPosition);
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

                    // If the window is already loaded, scroll to the current position
                    if (LayoutRoot.IsLoaded)
                    {
                        var targetIndex = _selectedTdp - 5;
                        var scrollPosition = 115 + (targetIndex * 80);
                        TdpScrollViewer.ScrollToHorizontalOffset(scrollPosition);
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

            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer == null) return;

            // Calculate which number is closest to center (account for padding)
            var scrollOffset = scrollViewer.HorizontalOffset;
            var itemWidth = 80.0; // 50 width + 30 spacing
            var adjustedOffset = scrollOffset - 115; // Subtract start padding
            var centerIndex = Math.Round(adjustedOffset / itemWidth);
            var selectedValue = (int)(centerIndex + 5);

            // Clamp to valid range
            selectedValue = Math.Max(5, Math.Min(30, selectedValue));

            if (selectedValue != _selectedTdp)
            {
                _selectedTdp = selectedValue;
                UpdateNumberOpacity();
            }

            // TODO: Add snapping back once basic scrolling works
            // Commenting out snapping for now to debug scrolling
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
            LayoutRoot.PointerPressed += OnPointerPressed;
            LayoutRoot.PointerMoved += OnPointerMoved;
            LayoutRoot.PointerReleased += OnPointerReleased;
        }

        private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse)
            {
                var properties = e.GetCurrentPoint(LayoutRoot).Properties;
                if (properties.IsLeftButtonPressed)
                {
                    _isDragging = true;
                    LayoutRoot.CapturePointer(e.Pointer);

                    GetCursorPos(out POINT cursorPos);
                    _lastPointerPosition = new Windows.Graphics.PointInt32(cursorPos.X, cursorPos.Y);
                }
            }
        }

        private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_isDragging && e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse)
            {
                var properties = e.GetCurrentPoint(LayoutRoot).Properties;
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
            LayoutRoot.ReleasePointerCapture(e.Pointer);
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
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

        private bool TrySetMicaBackdrop()
        {
            if (MicaController.IsSupported())
            {
                _backdropConfig = new SystemBackdropConfiguration
                {
                    IsInputActive = true,
                    Theme = SystemBackdropTheme.Default
                };

                _micaController = new MicaController();
                _micaController.AddSystemBackdropTarget(this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
                _micaController.SetSystemBackdropConfiguration(_backdropConfig);

                return true;
            }

            return false;
        }

        private async void SetTdpButton_Click(object sender, RoutedEventArgs e)
        {
            var tdpService = new TDPService();
            int targetTdp = _selectedTdp;
            int tdpInMilliwatts = targetTdp * 1000;

            var result = tdpService.SetTdp(tdpInMilliwatts);

            if (result.Success)
                CurrentTdpDisplayText = $"Current TDP: {targetTdp}W";
            else
                CurrentTdpDisplayText = $"Error: {result.Message}";
        }
    }
}