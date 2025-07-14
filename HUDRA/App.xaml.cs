using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using HUDRA.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace HUDRA
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;
        private TrayIconService? _trayIcon;
        public TdpMonitorService? TdpMonitor { get; private set; }

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();

            // Handle unhandled exceptions
            this.UnhandledException += OnUnhandledException;
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();
            _window.Activate();

            TdpMonitor = new TdpMonitorService(_window.DispatcherQueue);

            // Initialize tray icon
            _trayIcon = new TrayIconService();
            _trayIcon.DoubleClicked += (s, e) =>
            {
                if (_window is MainWindow mw)
                {
                    mw.DispatcherQueue.TryEnqueue(() => mw.ToggleWindowVisibility());
                }
            };
            _trayIcon.ExitRequested += (s, e) =>
            {
                CleanupAndExit();
            };

            // Handle window closed event for proper cleanup
            _window.Closed += (s, e) =>
            {
                CleanupAndExit();
            };
        }

        private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            // Log the exception or handle it appropriately
            System.Diagnostics.Debug.WriteLine($"Unhandled exception: {e.Exception}");

            // Mark as handled to prevent crash (optional)
            // e.Handled = true;
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
                System.Diagnostics.Debug.WriteLine($"Error disposing tray icon: {ex.Message}");
            }

            // Exit the application
            Environment.Exit(0);
        }
    }
}