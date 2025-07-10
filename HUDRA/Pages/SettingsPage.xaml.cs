using HUDRA.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HUDRA.Pages
{
    public sealed partial class SettingsPage : UserControl
    {
        public SettingsPage()
        {
            this.InitializeComponent();
            this.Loaded += SettingsPage_Loaded;
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            TdpCorrectionToggle.IsOn = SettingsService.GetTdpCorrectionEnabled();
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
    }
}
