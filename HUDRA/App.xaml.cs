using System;
using Microsoft.UI.Xaml;
using HUDRA.Services;

namespace HUDRA
{
    public partial class App : Application
    {
        private TrayIconService? _trayIcon;
        public TdpMonitorService? TdpMonitor { get; private set; }
        public TemperatureMonitorService? TemperatureMonitor { get; private set; }
        public FanControlService? FanControlService { get; private set; }
        public MainWindow? MainWindow { get; private set; }

        public App()
        {
            InitializeComponent();
            this.UnhandledException += OnUnhandledException;
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            MainWindow = new MainWindow();

            // Create TdpMonitor IMMEDIATELY after MainWindow creation
            TdpMonitor = new TdpMonitorService(MainWindow.DispatcherQueue);

            TemperatureMonitor = new TemperatureMonitorService(MainWindow.DispatcherQueue);
            FanControlService = new FanControlService(MainWindow.DispatcherQueue);

            // Enable fan control if fan curve is enabled in settings
            try
            {
                var fanCurve = SettingsService.GetFanCurve();
                if (fanCurve.IsEnabled)
                {
                    FanControlService.EnableTemperatureControl(TemperatureMonitor);
                    System.Diagnostics.Debug.WriteLine("🌡️ Global fan control service enabled at startup");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("🌡️ Fan curve disabled - no automatic fan control");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Fan control initialization warning: {ex.Message}");
                // Continue app startup even if fan control fails
            }

            // Set the TdpMonitor in MainWindow so it can use it
            MainWindow.SetTdpMonitor(TdpMonitor);

            MainWindow.Activate();

            // Initialize tray icon
            _trayIcon = new TrayIconService();
            _trayIcon.DoubleClicked += (s, e) =>
            {
                MainWindow.DispatcherQueue.TryEnqueue(() => MainWindow.ToggleWindowVisibility());
            };
            _trayIcon.ExitRequested += (s, e) =>
            {
                CleanupAndExit();
            };

            // Handle window closed event for proper cleanup
            MainWindow.Closed += (s, e) =>
            {
                CleanupAndExit();
            };
        }

        private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"Unhandled exception: {e.Exception}");
        }

        private void CleanupAndExit()
        {
            try
            {
                _trayIcon?.Dispose();
                TdpMonitor?.Dispose();
                TemperatureMonitor?.Dispose();
                FanControlService?.Dispose();
                System.Diagnostics.Debug.WriteLine("All services disposed cleanly");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing services: {ex.Message}");
            }

            Environment.Exit(0);
        }
    }
}