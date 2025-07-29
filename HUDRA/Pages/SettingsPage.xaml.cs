using HUDRA.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;

namespace HUDRA.Pages
{
    public sealed partial class SettingsPage : Page
    {
        private DpiScalingService? _dpiService;
        private bool _isInitialized = false;

        public SettingsPage()
        {
            this.InitializeComponent();
            LoadSettings();
        }

        private void LoadSettings()
        {

            TdpCorrectionToggle.IsOn = SettingsService.GetTdpCorrectionEnabled();
            UseStartupTdpToggle.IsOn = SettingsService.GetUseStartupTdp();
            LaunchAtStartupToggle.IsOn = SettingsService.GetLaunchAtStartup();
            UpdateStartupTdpEnabledState();
        }

        public void Initialize(DpiScalingService dpiService)
        {
            
            _dpiService = dpiService;

            // Initialize the TDP picker with autoSetEnabled=false (settings mode)
            StartupTdpPicker.Initialize(dpiService, autoSetEnabled: false);

            // Set up change handler
            StartupTdpPicker.TdpChanged += StartupTdpPicker_TdpChanged;

            _isInitialized = true;

            // Use the new method to set value when layout is ready
            int startupTdp = SettingsService.GetStartupTdp();
            StartupTdpPicker.SetSelectedTdpWhenReady(startupTdp);

            //Fan Control
            FanCurveControl.Initialize();
            SetupFanCurveEventHandling();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
                    }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            
            // Cleanup
            if (_isInitialized)
            {
                StartupTdpPicker.TdpChanged -= StartupTdpPicker_TdpChanged;
                StartupTdpPicker.Dispose();
                _isInitialized = false;
            }
        }

        private void StartupTdpPicker_TdpChanged(object? sender, int value)
        {
            
            // Always save the startup TDP when it changes, regardless of toggle state
            // The toggle controls whether it's used on startup, not whether changes are saved
            SettingsService.SetStartupTdp(value);

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

        private void LaunchAtStartupToggle_Toggled(object sender, RoutedEventArgs e)
        {
            var isOn = LaunchAtStartupToggle.IsOn;
            SettingsService.SetLaunchAtStartup(isOn);

            if (isOn)
                StartupService.EnableLaunchAtStartup();
            else
                StartupService.DisableLaunchAtStartup();
        }

        private void UpdateStartupTdpEnabledState()
        {
            bool enable = UseStartupTdpToggle.IsOn;
            
            StartupTdpPicker.IsEnabled = enable;
            StartupTdpPicker.Opacity = enable ? 1.0 : 0.5;
        }

        private void SetupFanCurveEventHandling()
        {
            // Handle fan curve control events
            FanCurveControl.FanCurveChanged += (s, e) =>
            {
                if (e.Curve.IsEnabled)
                {
                    System.Diagnostics.Debug.WriteLine("Fan curve control active - custom curve applied");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Fan curve control disabled - hardware mode active");
                }

                // Could add additional logging or status updates here
                // e.g., update main window status, log to file, etc.
            };
        }
    }
}