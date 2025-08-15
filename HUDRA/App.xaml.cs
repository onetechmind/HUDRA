using HUDRA.Configuration;
using HUDRA.Services;
using Microsoft.UI.Xaml;
using System;
using System.Threading.Tasks;

namespace HUDRA
{
    public partial class App : Application
    {
        private TrayIconService? _trayIcon;
        public TdpMonitorService? TdpMonitor { get; private set; }
        public TemperatureMonitorService? TemperatureMonitor { get; private set; }
        public FanControlService? FanControlService { get; private set; }
        public TurboService? TurboService { get; private set; }
        public MainWindow? MainWindow { get; private set; }

        // Track if we've already applied startup TDP during minimized launch
        public bool StartupTdpAlreadyApplied { get; private set; } = false;

        public App()
        {
            InitializeComponent();
            this.UnhandledException += OnUnhandledException;
        }

        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // Check if launched at startup
            var commandLineArgs = Environment.GetCommandLineArgs();
            bool wasLaunchedAtStartup = StartupService.WasLaunchedAtStartup(commandLineArgs);

            // Preload RTSS installation status BEFORE creating any UI to prevent flashing
            await RtssFpsLimiterService.PreloadInstallationStatusAsync();

            MainWindow = new MainWindow();

            // Create TdpMonitor IMMEDIATELY after MainWindow creation
            TdpMonitor = new TdpMonitorService(MainWindow.DispatcherQueue);

            TemperatureMonitor = new TemperatureMonitorService(MainWindow.DispatcherQueue);
            FanControlService = new FanControlService(MainWindow.DispatcherQueue);

            // CRITICAL: Initialize the FanControlService
            try
            {
                var initResult = await FanControlService.InitializeAsync();
                System.Diagnostics.Debug.WriteLine($"Fan control initialization: {initResult.Message}");

                // Enable fan control if fan curve is enabled in settings AND service initialized successfully
                if (initResult.Success)
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
                    
                    // Initialize TurboService with detected device after FanControlService completes
                    try
                    {
                        TurboService = new TurboService(FanControlService.DetectedDevice);
                        System.Diagnostics.Debug.WriteLine("🎮 TurboService initialized successfully");
                        
                        // Connect MainWindow to TurboService now that it's ready
                        MainWindow?.ConnectTurboService();
                    }
                    catch (Exception turboEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ TurboService initialization failed: {turboEx.Message}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Fan control not available: {initResult.Message}");
                    
                    // Still try to initialize TurboService without device for software hotkeys
                    try
                    {
                        TurboService = new TurboService(null);
                        System.Diagnostics.Debug.WriteLine("🎮 TurboService initialized in fallback mode (software hotkeys only)");
                        
                        // Connect MainWindow to TurboService for software hotkeys
                        MainWindow?.ConnectTurboService();
                    }
                    catch (Exception turboEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ TurboService fallback initialization failed: {turboEx.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Fan control initialization error: {ex.Message}");
                // Continue app startup even if fan control fails
            }

            // Set the TdpMonitor in MainWindow so it can use it
            MainWindow.SetTdpMonitor(TdpMonitor);

            // Initialize tray icon BEFORE deciding whether to show window
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

            // Check "Start Minimized" setting for ALL launches
            bool shouldStartMinimized = SettingsService.GetMinimizeToTrayOnStartup();

            if (shouldStartMinimized)
            {
                // IMPORTANT: Tell WindowManagementService the window is hidden initially
                MainWindow.WindowManager.SetInitialVisibilityState(false);

                // For startup launches, apply TDP since MainPage won't load
                if (wasLaunchedAtStartup)
                {
                    ApplyStartupTdp();
                    StartupTdpAlreadyApplied = true;
                }
                else
                {
                    // Manual launch but minimized - let TdpPickerControl handle TDP when window opens later
                    StartupTdpAlreadyApplied = false;
                }
            }
            else
            {
                // Show window normally
                MainWindow.Activate();

                // TdpPickerControl will handle TDP when MainPage loads
                StartupTdpAlreadyApplied = false;

                // Window is visible, so set the state accordingly
                MainWindow.WindowManager.SetInitialVisibilityState(true);
            }
        }
        private void ApplyStartupTdp()
        {
            try
            {
                int targetTdp;
                string statusReason;

                // Priority 1: Default TDP if toggle is enabled
                if (SettingsService.GetUseStartupTdp())
                {
                    targetTdp = SettingsService.GetStartupTdp();
                    statusReason = "using default TDP from settings";
                }
                else
                {
                    // Priority 2: Last-Used TDP if default is disabled
                    targetTdp = SettingsService.GetLastUsedTdp();

                    // Priority 3: Fallback to 10W if last-used is invalid
                    if (targetTdp < HudraSettings.MIN_TDP || targetTdp > HudraSettings.MAX_TDP)
                    {
                        targetTdp = HudraSettings.DEFAULT_STARTUP_TDP;
                        statusReason = "using fallback (10W) - invalid last-used TDP";
                    }
                    else
                    {
                        statusReason = "using last-used TDP";
                    }
                }

                System.Diagnostics.Debug.WriteLine($"⚡ Applying startup TDP: {targetTdp}W ({statusReason})");

                // Small delay to ensure services are ready, then apply startup TDP
                Task.Delay(1000).ContinueWith(_ =>
                {
                    MainWindow?.DispatcherQueue.TryEnqueue(() =>
                    {
                        try
                        {
                            // Use TDPService directly to set the TDP
                            using var tdpService = new TDPService();
                            var result = tdpService.SetTdp(targetTdp * 1000); // Convert to milliwatts

                            if (result.Success)
                            {
                                System.Diagnostics.Debug.WriteLine($"✅ Startup TDP applied successfully: {result.Message}");

                                // Update the monitor target so it knows the current TDP
                                TdpMonitor?.UpdateTargetTdp(targetTdp);

                                // Save as last-used TDP for future sessions
                                SettingsService.SetLastUsedTdp(targetTdp);
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"❌ Failed to apply startup TDP: {result.Message}");
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"⚠️ Exception applying startup TDP: {ex.Message}");
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Error in startup TDP application: {ex.Message}");
            }
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
                TurboService?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing services: {ex.Message}");
            }

            Environment.Exit(0);
        }
    }
}