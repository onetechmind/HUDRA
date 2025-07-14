using HUDRA.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HUDRA.Pages
{
    public sealed partial class SettingsPage : UserControl
    {
        private DpiScalingService? _dpiService;

        public SettingsPage()
        {
            this.InitializeComponent();
            this.Loaded += SettingsPage_Loaded;
        }

        public void Initialize(DpiScalingService dpiService)
        {
            _dpiService = dpiService;
            StartupTdpPicker.Initialize(dpiService, autoSetEnabled: false);
            StartupTdpPicker.SelectedTdp = SettingsService.GetStartupTdp();
            StartupTdpPicker.TdpChanged += StartupTdpPicker_TdpChanged;
            this.Unloaded += SettingsPage_Unloaded;
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            TdpCorrectionToggle.IsOn = SettingsService.GetTdpCorrectionEnabled();
            UseStartupTdpToggle.IsOn = SettingsService.GetUseStartupTdp();
            UpdateStartupTdpEnabledState();
        }

        private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
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
