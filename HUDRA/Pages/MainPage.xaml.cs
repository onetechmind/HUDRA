using HUDRA.Controls;
using HUDRA.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HUDRA.Pages
{
    public sealed partial class MainPage : Page
    {
        private bool _hasScrollPositioned = false;

        public MainPage()
        {
            this.InitializeComponent();
            this.Loaded += MainPage_Loaded;
        }

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Only trigger scroll positioning once per page instance
            if (!_hasScrollPositioned)
            {
                _hasScrollPositioned = true;

                // Wait for navigation to complete before positioning scroll
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    // Check if navigation service indicates we're still navigating
                    if (Application.Current is App app && app.MainWindow?.NavigationService.IsNavigating == false)
                    {
                        TdpPicker.EnsureScrollPositionAfterLayout();
                    }
                });
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            
            // No RTSS refresh needed - installation status is cached and set in constructor
            // Running status is handled by FpsLimiterControl internally when needed
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
        }

        public void Initialize(DpiScalingService dpiService,
                               ResolutionService resolutionService,
                               AudioService audioService,
                               BrightnessService brightnessService,
                               RtssFpsLimiterService fpsLimiterService,
                               HdrService hdrService)
        {
            TdpPicker.Initialize(dpiService);
            ResolutionPicker.Initialize();
            AudioControls.Initialize();
            BrightnessControls.Initialize();
            FpsLimiter.Initialize(fpsLimiterService, hdrService);

            ResolutionPicker.PropertyChanged += (s, e) =>
            {
                // Property change events can be handled by parent window if needed
            };
        }
    }
}
