using HUDRA.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HUDRA.Pages
{
    public sealed partial class SettingsPage : Page
    {
        private DpiScalingService? _dpiService;
        private bool _hasScrollPositioned = false;

        public SettingsPage()
        {
            this.InitializeComponent();
            this.Loaded += SettingsPage_Loaded;
            LoadSettings();
        }

        private void LoadSettings()
        {
            TdpCorrectionToggle.IsOn = SettingsService.GetTdpCorrectionEnabled();
            UseStartupTdpToggle.IsOn = SettingsService.GetUseStartupTdp();
            UpdateStartupTdpEnabledState();
        }

        public void Initialize(DpiScalingService dpiService)
        {
            _dpiService = dpiService;
            StartupTdpPicker.Initialize(dpiService, autoSetEnabled: false);
            StartupTdpPicker.SelectedTdp = SettingsService.GetStartupTdp();
            StartupTdpPicker.TdpChanged += StartupTdpPicker_TdpChanged;
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Handle scroll positioning when page becomes visible
            if (!_hasScrollPositioned)
            {
                _hasScrollPositioned = true;

                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    // Check if navigation service indicates we're still navigating
                    if (Application.Current is App app && app.MainWindow?.NavigationService.IsNavigating == false)
                    {
                        StartupTdpPicker.EnsureScrollPositionAfterLayout();
                    }
                });
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            System.Diagnostics.Debug.WriteLine("SettingsPage: OnNavigatedTo");
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            System.Diagnostics.Debug.WriteLine("SettingsPage: OnNavigatedFrom");

            // Cleanup
            StartupTdpPicker.TdpChanged -= StartupTdpPicker_TdpChanged;
            StartupTdpPicker.Dispose();
        }

        private void StartupTdpPicker_TdpChanged(object? sender, int value)
        {
            if (UseStartupTdpToggle.IsOn)
            {
                SettingsService.SetStartupTdp(value);
            }
        }

        private void TdpCorrectionToggle_Toggled(object sender, RoutedEventArgs e)
        {
            var isOn = TdpCorrectionToggle.IsOn;
            SettingsService.SetTdpCorrectionEnabled(isOn);

            var monitor = (App.Current as App)?.TdpMonitor;
            if (monitor != null)
            {
                if (isOn)
                    monitor.Start();
                else
                    monitor.Stop();
            }
        }

        private void UseStartupTdpToggle_Toggled(object sender, RoutedEventArgs e)
        {
            var isOn = UseStartupTdpToggle.IsOn;
            SettingsService.SetUseStartupTdp(isOn);
            UpdateStartupTdpEnabledState();
        }

        private void UpdateStartupTdpEnabledState()
        {
            bool enable = UseStartupTdpToggle.IsOn;
            StartupTdpPicker.IsEnabled = enable;
            StartupTdpPicker.Opacity = enable ? 1.0 : 0.5;
        }
    }
}