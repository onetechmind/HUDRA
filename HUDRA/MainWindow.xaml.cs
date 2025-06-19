using HUDRA.Services;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using WinRT;
using WinRT.Interop; // Required for WindowNative interop


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
        private bool _dragRegionRegistered = false;

        public MainWindow()
        {
            this.InitializeComponent();
            this.Title = "HUDRA Control Center";

            LayoutRoot.DataContext = this;
            TrySetMicaBackdrop();
            SetInitialSize();
            MakeBorderlessWithRoundedCorners();
        }

       

        private void SetInitialSize()
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            appWindow.Resize(new Windows.Graphics.SizeInt32(300, 400));
        }

        private void MakeBorderlessWithRoundedCorners()
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            // Remove title bar and border
            appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);

            var presenter = appWindow.Presenter as OverlappedPresenter;
            if (presenter != null)
            {
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.SetBorderAndTitleBar(false, false);
            }

            // Optional: enable default OS-level rounded corners (Windows 11 only)
            appWindow.TitleBar.ExtendsContentIntoTitleBar = true;

            DispatcherQueue.TryEnqueue(() =>
            {
                var width = (int)LayoutRoot.ActualWidth;
                var height = (int)LayoutRoot.ActualHeight;
                System.Diagnostics.Debug.WriteLine($"LayoutRoot: {width}x{height}");

                if (width > 0 && height > 0)
                {
                    appWindow.TitleBar.SetDragRectangles(new[]
                    {
            new Windows.Graphics.RectInt32
            {
                X = 0,
                Y = 0,
                Width = width,
                Height = height
            }
        });
                }
            });

        }

        private bool _dragRegionSet = false;

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
            int targetTdp = (int)TdpSlider.Value;
            int tdpInMilliwatts = targetTdp * 1000;

            var result = tdpService.SetTdp(tdpInMilliwatts);

            if (result.Success)
                CurrentTdpDisplayText = $"Current TDP: {targetTdp}W";
            else
                CurrentTdpDisplayText = $"Error: {result.Message}";
        }
    }
}
