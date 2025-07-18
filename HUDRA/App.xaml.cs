using System;
using Microsoft.UI.Xaml;
using HUDRA.Services;

namespace HUDRA
{
    public partial class App : Application
    {
        private TrayIconService? _trayIcon;
        public TdpMonitorService? TdpMonitor { get; private set; }
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
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing services: {ex.Message}");
            }

            Environment.Exit(0);
        }
    }
}