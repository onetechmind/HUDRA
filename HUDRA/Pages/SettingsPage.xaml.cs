using HUDRA.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.IO;
using System.Threading.Tasks;

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
            LoadStartupSettings();
        }

        private void LoadSettings()
        {

            TdpCorrectionToggle.IsOn = SettingsService.GetTdpCorrectionEnabled();
            UseStartupTdpToggle.IsOn = SettingsService.GetUseStartupTdp();
            UpdateStartupTdpEnabledState();
            LoadStartupSettings();
            DiagnoseStartupTask();
        }
        private void LoadStartupSettings()
        {
            // Load startup settings
            StartupToggle.IsOn = SettingsService.GetStartupEnabled();
            MinimizeOnStartupToggle.IsOn = SettingsService.GetMinimizeToTrayOnStartup();

            UpdateMinimizeOnStartupState();
        }

        private async void StartupToggle_Toggled(object sender, RoutedEventArgs e)
        {
            var isOn = StartupToggle.IsOn;

            // Don't proceed if not admin
            if (!StartupService.IsRunningAsAdmin())
            {
                StartupToggle.IsOn = false;
                return;
            }

            try
            {
                // Show loading state
                StartupToggle.IsEnabled = false;

                // Run the startup configuration in background thread
                // This might show UAC prompt when creating the task
                bool success = await Task.Run(() => SettingsService.SetStartupEnabled(isOn));

                if (success)
                {
                    UpdateMinimizeOnStartupState();
                }
                else
                {
                    // Revert toggle
                    StartupToggle.IsOn = !isOn;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error toggling startup: {ex.Message}");
                StartupToggle.IsOn = !isOn;
            }
            finally
            {
                StartupToggle.IsEnabled = true;
            }
        }
        private void MinimizeOnStartupToggle_Toggled(object sender, RoutedEventArgs e)
        {
            var isOn = MinimizeOnStartupToggle.IsOn;
            SettingsService.SetMinimizeToTrayOnStartup(isOn);
        }

        private void UpdateMinimizeOnStartupState()
        {
            // Enable/disable based on startup toggle and admin status
            bool enable = StartupToggle.IsOn && StartupService.IsRunningAsAdmin();

            MinimizeOnStartupToggle.IsEnabled = enable;
            MinimizeOnStartupToggle.Opacity = enable ? 1.0 : 0.5;
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

        private void UpdateStartupTdpEnabledState()
        {
            bool enable = UseStartupTdpToggle.IsOn;

            StartupTdpPicker.IsEnabled = enable;
            StartupTdpPicker.Opacity = enable ? 1.0 : 0.5;
        }


        // Add this temporary method to your SettingsPage.xaml.cs for debugging
        // Make sure you have: using System.IO; at the top of your file

        private void DiagnoseStartupTask()
        {

            // Check if task exists
            bool taskExists = StartupService.IsStartupEnabled();

            // Get executable path
            string exePath = StartupService.GetExecutablePath();

            // Check if executable exists
            bool exeExists = File.Exists(exePath);

            // Get task details (without running it!)
            if (taskExists)
            {
                string taskDetails = StartupService.GetTaskRunInfo(); // This just gets info, doesn't run
            }

            // Check admin status
            bool isAdmin = StartupService.IsRunningAsAdmin();

        }
    }
}