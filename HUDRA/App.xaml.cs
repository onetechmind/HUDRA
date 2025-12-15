using HUDRA.Configuration;
using HUDRA.Services;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace HUDRA
{
    public partial class App : Application
    {
        // Single instance detection
        private static Mutex? _instanceMutex;
        private const string MUTEX_NAME = "Global\\HUDRA_SingleInstance_Mutex";

        // P/Invoke for MessageBox
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
        private const uint MB_OK = 0x00000000;
        private const uint MB_ICONINFORMATION = 0x00000040;
        private const uint MB_ICONERROR = 0x00000010;

        private TrayIconService? _trayIcon;
        private PowerEventService? _powerEventService;
        private readonly object _reinitializationLock = new object();
        private bool _isReinitializing = false;
        
        public TdpMonitorService? TdpMonitor { get; private set; }
        public TemperatureMonitorService? TemperatureMonitor { get; private set; }
        public FanControlService? FanControlService { get; private set; }
        public TurboService? TurboService { get; private set; }
        public MainWindow? MainWindow { get; private set; }

        // Track if we've already applied startup TDP during minimized launch
        public bool StartupTdpAlreadyApplied { get; private set; } = false;

        public App()
        {
            // Check for existing instance BEFORE initializing components
            if (!CheckSingleInstance())
            {
                // Another instance is already running - show message and exit
                MessageBox(IntPtr.Zero,
                    "HUDRA is already running. Please check your system tray.",
                    "HUDRA",
                    MB_OK | MB_ICONINFORMATION);

                // Exit immediately
                Environment.Exit(0);
                return;
            }

            InitializeComponent();
            this.UnhandledException += OnUnhandledException;
        }

        private static bool CheckSingleInstance()
        {
            try
            {
                // Try to create a mutex with a unique name
                _instanceMutex = new Mutex(true, MUTEX_NAME, out bool createdNew);

                // If createdNew is true, we're the first instance
                // If false, another instance already owns this mutex
                return createdNew;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking single instance: {ex.Message}");
                // If there's an error, allow the app to run
                return true;
            }
        }

        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // Check for administrator rights before proceeding
            if (!StartupService.IsRunningAsAdmin())
            {
                MessageBox(IntPtr.Zero,
                    "HUDRA requires administrator privileges to control TDP and fan settings.\n\n" +
                    "Please right-click the HUDRA shortcut and select 'Run as administrator', " +
                    "or check the installer's 'Run with highest privileges' option.",
                    "Administrator Rights Required",
                    MB_OK | MB_ICONERROR);

                Environment.Exit(1);
                return;
            }

            // Apply any preferences set by the installer (first launch only)
            SettingsService.ApplyInstallerPreferences();

            // Check if launched at startup
            var commandLineArgs = Environment.GetCommandLineArgs();
            bool wasLaunchedAtStartup = StartupService.WasLaunchedAtStartup(commandLineArgs);

            // Preload RTSS and Lossless Scaling installation status BEFORE creating any UI to prevent flashing
            await RtssFpsLimiterService.PreloadInstallationStatusAsync();
            await LosslessScalingService.PreloadInstallationStatusAsync();

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

            // Initialize power event service for hibernation resume detection
            InitializePowerEventService();
        }
        private void ApplyStartupTdp()
        {
            try
            {
                int targetTdp;
                string statusReason;

                // Priority 1: Default Profile TDP (if saved)
                var defaultProfile = SettingsService.GetDefaultProfile();
                if (defaultProfile?.TdpWatts > 0)
                {
                    targetTdp = defaultProfile.TdpWatts;
                    statusReason = "using Default Profile TDP";
                }
                // Priority 2: Default TDP if toggle is enabled (legacy setting)
                else if (SettingsService.GetUseStartupTdp())
                {
                    targetTdp = SettingsService.GetStartupTdp();
                    statusReason = "using default TDP from settings";
                }
                else
                {
                    // Priority 3: Last-Used TDP if default is disabled
                    targetTdp = SettingsService.GetLastUsedTdp();

                    // Priority 4: Fallback to 10W if last-used is invalid
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

        private void InitializePowerEventService()
        {
            try
            {
                if (MainWindow == null)
                {
                    System.Diagnostics.Debug.WriteLine("⚠️ Cannot initialize PowerEventService - MainWindow is null");
                    return;
                }

                _powerEventService = new PowerEventService(MainWindow, MainWindow.DispatcherQueue);
                _powerEventService.HibernationResumeDetected += OnHibernationResumeDetected;

                System.Diagnostics.Debug.WriteLine("⚡ PowerEventService initialized successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Failed to initialize PowerEventService: {ex.Message}");
            }
        }

        private async void OnHibernationResumeDetected(object? sender, EventArgs e)
        {
            // Use lock to prevent concurrent reinitialization
            lock (_reinitializationLock)
            {
                if (_isReinitializing)
                {
                    System.Diagnostics.Debug.WriteLine("⚡ Hibernation resume already in progress - ignoring duplicate event");
                    return;
                }
                _isReinitializing = true;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine("⚡ Hibernation resume detected - reinitializing services...");

                // Delay briefly to allow Windows to stabilize after resume
                await Task.Delay(2000);

                var reinitTasks = new List<Task>();

                // Reinitialize hardware-dependent services
                if (TurboService != null)
                {
                    reinitTasks.Add(Task.Run(() =>
                    {
                        var result = TurboService.ReinitializeAfterResume();
                        System.Diagnostics.Debug.WriteLine($"⚡ TurboService reinitialization: {result.Message}");
                    }));
                }

                if (FanControlService != null)
                {
                    reinitTasks.Add(Task.Run(async () =>
                    {
                        var result = await FanControlService.ReinitializeAfterResumeAsync();
                        System.Diagnostics.Debug.WriteLine($"⚡ FanControlService reinitialization: {result.Message}");
                    }));
                }

                // Wait for all reinitializations to complete
                await Task.WhenAll(reinitTasks);

                // Reinitialize TDP and apply last used settings
                await ReinitializeTdpAfterResume();

                // Handle UI refresh in MainWindow
                MainWindow?.HandleHibernationResume();

                System.Diagnostics.Debug.WriteLine("⚡ All services reinitialized after hibernation resume");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Error during hibernation resume handling: {ex.Message}");
            }
            finally
            {
                lock (_reinitializationLock)
                {
                    _isReinitializing = false;
                }
                System.Diagnostics.Debug.WriteLine("⚡ Hibernation resume reinitialization completed");
            }
        }

        private async Task ReinitializeTdpAfterResume()
        {
            try
            {
                // Create a new TDPService instance for reinitialization
                using var tdpService = new TDPService();
                var reinitResult = tdpService.ReinitializeAfterResume();
                
                if (reinitResult.Success)
                {
                    System.Diagnostics.Debug.WriteLine($"⚡ TDPService reinitialization: {reinitResult.Message}");

                    // Get the last used TDP and re-apply it
                    int lastUsedTdp = SettingsService.GetLastUsedTdp();
                    
                    // Validate the TDP value
                    if (lastUsedTdp >= HudraSettings.MIN_TDP && lastUsedTdp <= HudraSettings.MAX_TDP)
                    {
                        var setResult = tdpService.SetTdp(lastUsedTdp * 1000); // Convert to milliwatts
                        
                        if (setResult.Success)
                        {
                            System.Diagnostics.Debug.WriteLine($"⚡ Successfully re-applied last used TDP: {lastUsedTdp}W");
                            
                            // Update TDP monitor target
                            TdpMonitor?.UpdateTargetTdp(lastUsedTdp);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"⚠️ Failed to re-apply TDP: {setResult.Message}");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ Invalid last used TDP value: {lastUsedTdp}W");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ TDPService reinitialization failed: {reinitResult.Message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Error reinitializing TDP after resume: {ex.Message}");
            }
        }

        private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Unhandled exception: {e.Exception}");

                // Write crash report to file
                DebugLogger.WriteCrashReport(e.Exception);
            }
            catch
            {
                // If crash logging fails, don't prevent app termination
            }

            // Let the exception propagate (app will still terminate)
            e.Handled = false;
        }

        private void CleanupAndExit()
        {
            try
            {
                _trayIcon?.Dispose();
                _powerEventService?.Dispose();
                TdpMonitor?.Dispose();
                TemperatureMonitor?.Dispose();
                FanControlService?.Dispose();
                TurboService?.Dispose();

                // Release the single instance mutex
                _instanceMutex?.ReleaseMutex();
                _instanceMutex?.Dispose();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing services: {ex.Message}");
            }

            Environment.Exit(0);
        }
    }
}