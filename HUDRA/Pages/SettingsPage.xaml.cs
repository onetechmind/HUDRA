using HUDRA.Services;
using HUDRA.Controls;
using HUDRA.Extensions;
using HUDRA.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace HUDRA.Pages
{
    public sealed partial class SettingsPage : Page
    {
        private DpiScalingService? _dpiService;
        private bool _isInitialized = false;

        // Property change callback token for TDP Settings Expander
        private long _tdpExpanderCallbackToken = 0;
        
        // Session-only state storage for Expanders
        private static bool _gameDetectionExpanderExpanded = false;
        private static bool _tdpSettingsExpanderExpanded = false;
        private static bool _powerProfileExpanderExpanded = false;
        private static bool _startupSettingsExpanderExpanded = false;

        public SettingsPage()
        {
            this.InitializeComponent();
            this.Loaded += SettingsPage_Loaded;
            this.Unloaded += SettingsPage_Unloaded;
            LoadSettings();
            LoadStartupSettings();
            LoadRtssSettings();
            LoadEnhancedScanningSettings();
            LoadDatabaseStatus();
            LoadVersionInfo();
        }

        private void LoadVersionInfo()
        {
            try
            {
                VersionText.Text = DebugLogger.GetAppVersion();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading version info: {ex.Message}");
                VersionText.Text = "HUDRA Beta";
            }
        }

        // Expose root for gamepad page navigation
        public FrameworkElement? RootPanel => SettingsRootPanel;

        private void LoadSettings()
        {
            // Settings are now loaded within the custom controls themselves
            // Wire up event handlers to the custom controls
            LoadTdpSettings();
            LoadGameDetectionSettings();
            LoadStartupSettings();
            LoadHotkeySettings();
            DiagnoseStartupTask();
        }

        private void LoadTdpSettings()
        {
            // TDP settings are handled within TdpSettingsControl
            if (TdpSettingsControl?.TdpCorrectionToggle != null)
            {
                TdpSettingsControl.TdpCorrectionToggle.IsOn = SettingsService.GetTdpCorrectionEnabled();
                TdpSettingsControl.TdpCorrectionToggle.Toggled += TdpCorrectionToggle_Toggled;
            }
            if (TdpSettingsControl?.UseStartupTdpToggle != null)
            {
                TdpSettingsControl.UseStartupTdpToggle.IsOn = SettingsService.GetUseStartupTdp();
                TdpSettingsControl.UseStartupTdpToggle.Toggled += UseStartupTdpToggle_Toggled;
            }
        }

        private void LoadGameDetectionSettings()
        {
            // Game detection settings are handled within GameDetectionControl
            if (GameDetectionControl?.EnhancedLibraryScanningToggle != null)
            {
                GameDetectionControl.EnhancedLibraryScanningToggle.IsOn = SettingsService.IsEnhancedLibraryScanningEnabled();
                GameDetectionControl.EnhancedLibraryScanningToggle.Toggled += EnhancedLibraryScanningToggle_Toggled;
            }
            if (GameDetectionControl?.ScanIntervalComboBox != null)
            {
                LoadScanIntervalSettings();
                GameDetectionControl.ScanIntervalComboBox.SelectionChanged += ScanIntervalComboBox_SelectionChanged;
            }
            if (GameDetectionControl?.ResetDatabaseButton != null)
            {
                GameDetectionControl.ResetDatabaseButton.Click += ResetDatabaseButton_Click;
            }
        }

        private void LoadScanIntervalSettings()
        {
            if (GameDetectionControl?.ScanIntervalComboBox == null) return;

            // Load and set scan interval ComboBox
            int currentInterval = SettingsService.GetGameDatabaseRefreshInterval();
            foreach (ComboBoxItem item in GameDetectionControl.ScanIntervalComboBox.Items)
            {
                if (item.Tag is string tagValue && int.TryParse(tagValue, out int intervalValue))
                {
                    if (intervalValue == currentInterval)
                    {
                        GameDetectionControl.ScanIntervalComboBox.SelectedItem = item;
                        break;
                    }
                }
            }

            // If no match found, default to 15 minutes
            if (GameDetectionControl.ScanIntervalComboBox.SelectedItem == null)
            {
                GameDetectionControl.ScanIntervalComboBox.SelectedIndex = 1; // 15 minutes option
            }
        }

        private void LoadStartupSettings()
        {
            // Startup settings are handled within StartupOptionsControl
            if (StartupOptionsControl?.StartupToggle != null)
            {
                StartupOptionsControl.StartupToggle.IsOn = SettingsService.GetStartupEnabled();
                StartupOptionsControl.StartupToggle.Toggled += StartupToggle_Toggled;
            }
            if (StartupOptionsControl?.MinimizeOnStartupToggle != null)
            {
                StartupOptionsControl.MinimizeOnStartupToggle.IsOn = SettingsService.GetMinimizeToTrayOnStartup();
                StartupOptionsControl.MinimizeOnStartupToggle.Toggled += MinimizeOnStartupToggle_Toggled;
            }
            if (StartupOptionsControl?.StartRtssWithHudraToggle != null)
            {
                StartupOptionsControl.StartRtssWithHudraToggle.IsOn = SettingsService.GetStartRtssWithHudra();
                StartupOptionsControl.StartRtssWithHudraToggle.Toggled += StartRtssWithHudraToggle_Toggled;
            }

            UpdateMinimizeOnStartupState();
        }

        private async void StartupToggle_Toggled(object sender, RoutedEventArgs e)
        {
            var toggle = sender as ToggleSwitch;
            var isOn = toggle?.IsOn ?? false;

            // Don't proceed if not admin
            if (!StartupService.IsRunningAsAdmin())
            {
                if (toggle != null) toggle.IsOn = false;
                return;
            }

            try
            {
                // Show loading state
                if (toggle != null) toggle.IsEnabled = false;

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
                    if (toggle != null) toggle.IsOn = !isOn;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error toggling startup: {ex.Message}");
                if (toggle != null) toggle.IsOn = !isOn;
            }
            finally
            {
                if (toggle != null) toggle.IsEnabled = true;
            }
        }
        private void MinimizeOnStartupToggle_Toggled(object sender, RoutedEventArgs e)
        {
            var toggle = sender as ToggleSwitch;
            var isOn = toggle?.IsOn ?? false;
            SettingsService.SetMinimizeToTrayOnStartup(isOn);
        }

        private void StartRtssWithHudraToggle_Toggled(object sender, RoutedEventArgs e)
        {
            var toggle = sender as ToggleSwitch;
            var isOn = toggle?.IsOn ?? false;
            SettingsService.SetStartRtssWithHudra(isOn);
        }

        private void LoadRtssSettings()
        {
            // RTSS settings are now loaded in LoadStartupSettings()
        }

        private void UpdateMinimizeOnStartupState()
        {
            // Enable/disable based on admin status only - independent of startup toggle
            bool enable = StartupService.IsRunningAsAdmin();

            if (StartupOptionsControl?.MinimizeOnStartupToggle != null)
            {
                StartupOptionsControl.MinimizeOnStartupToggle.IsEnabled = enable;
                StartupOptionsControl.MinimizeOnStartupToggle.Opacity = enable ? 1.0 : 0.5;
            }
        }


        public void Initialize(DpiScalingService dpiService)
        {

            _dpiService = dpiService;

            // Initialize the TDP picker with autoSetEnabled=false (settings mode)
            // This will automatically load and display the startup TDP from settings
            if (TdpSettingsControl?.StartupTdpPicker != null)
            {
                TdpSettingsControl.StartupTdpPicker.Initialize(dpiService, autoSetEnabled: false);

                // Set up change handler
                TdpSettingsControl.StartupTdpPicker.TdpChanged += StartupTdpPicker_TdpChanged;
            }

            _isInitialized = true;
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            // Load Game Detection Expander state (session-only) after page UI is loaded
            if (GameDetectionExpander != null)
            {
                GameDetectionExpander.IsExpanded = _gameDetectionExpanderExpanded;
            }

            // Load TDP Settings Expander state
            if (TdpSettingsExpander != null)
            {
                TdpSettingsExpander.IsExpanded = _tdpSettingsExpanderExpanded;
            }

            // Load Power Profile Expander state
            if (PowerProfileExpander != null)
            {
                PowerProfileExpander.IsExpanded = _powerProfileExpanderExpanded;
            }

            // Load Startup Settings Expander state
            if (StartupSettingsExpander != null)
            {
                StartupSettingsExpander.IsExpanded = _startupSettingsExpanderExpanded;
            }

            // Set up property change monitoring for TDP Settings Expander
            if (TdpSettingsExpander != null)
            {
                _tdpExpanderCallbackToken = TdpSettingsExpander.RegisterPropertyChangedCallback(
                    Microsoft.UI.Xaml.Controls.Expander.IsExpandedProperty,
                    OnTdpExpanderStateChanged);

                // Always refresh TDP picker display to ensure correct value is shown
                RefreshTdpPickerDisplay();
            }
        }

        private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            // Clean up property change callback to prevent memory leaks
            if (TdpSettingsExpander != null && _tdpExpanderCallbackToken != 0)
            {
                TdpSettingsExpander.UnregisterPropertyChangedCallback(
                    Microsoft.UI.Xaml.Controls.Expander.IsExpandedProperty, 
                    _tdpExpanderCallbackToken);
                _tdpExpanderCallbackToken = 0;
            }

            // Save Game Detection expander state when leaving the page (session-only)
            if (GameDetectionExpander != null)
            {
                _gameDetectionExpanderExpanded = GameDetectionExpander.IsExpanded;
            }

            // Save TDP Settings expander state
            if (TdpSettingsExpander != null)
            {
                _tdpSettingsExpanderExpanded = TdpSettingsExpander.IsExpanded;
            }

            // Save Power Profile expander state
            if (PowerProfileExpander != null)
            {
                _powerProfileExpanderExpanded = PowerProfileExpander.IsExpanded;
            }

            // Save Startup Settings expander state
            if (StartupSettingsExpander != null)
            {
                _startupSettingsExpanderExpanded = StartupSettingsExpander.IsExpanded;
            }
        }

        private void OnTdpExpanderStateChanged(Microsoft.UI.Xaml.DependencyObject sender, Microsoft.UI.Xaml.DependencyProperty dp)
        {
            // Only update TDP picker when expander is expanded (not when collapsed)
            if (TdpSettingsExpander != null && TdpSettingsExpander.IsExpanded)
            {
                RefreshTdpPickerDisplay();
            }
        }

        private void RefreshTdpPickerDisplay()
        {
            // Ensure the TDP picker displays correctly after expander expansion
            // This is needed because the control may have been initialized while inside a collapsed expander
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                int startupTdp = SettingsService.GetStartupTdp();
                TdpSettingsControl?.StartupTdpPicker?.SetSelectedTdpWhenReady(startupTdp);
                TdpSettingsControl?.StartupTdpPicker?.EnsureScrollPositionAfterLayout();
            });
        }

        private void SetTdpPickerValue()
        {
            int startupTdp = SettingsService.GetStartupTdp();
            TdpSettingsControl?.StartupTdpPicker?.SetSelectedTdpWhenReady(startupTdp);
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            // Clean up property change callback to prevent memory leaks
            if (TdpSettingsExpander != null && _tdpExpanderCallbackToken != 0)
            {
                TdpSettingsExpander.UnregisterPropertyChangedCallback(
                    Microsoft.UI.Xaml.Controls.Expander.IsExpandedProperty, 
                    _tdpExpanderCallbackToken);
                _tdpExpanderCallbackToken = 0;
            }

            // Save Game Detection expander state when leaving the page (session-only)
            if (GameDetectionExpander != null)
            {
                _gameDetectionExpanderExpanded = GameDetectionExpander.IsExpanded;
            }

            // Save TDP Settings expander state
            if (TdpSettingsExpander != null)
            {
                _tdpSettingsExpanderExpanded = TdpSettingsExpander.IsExpanded;
            }

            // Save Power Profile expander state
            if (PowerProfileExpander != null)
            {
                _powerProfileExpanderExpanded = PowerProfileExpander.IsExpanded;
            }

            // Save Startup Settings expander state
            if (StartupSettingsExpander != null)
            {
                _startupSettingsExpanderExpanded = StartupSettingsExpander.IsExpanded;
            }

            // Cleanup
            if (_isInitialized && TdpSettingsControl?.StartupTdpPicker != null)
            {
                TdpSettingsControl.StartupTdpPicker.TdpChanged -= StartupTdpPicker_TdpChanged;
                TdpSettingsControl.StartupTdpPicker.Dispose();
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
            var toggle = sender as ToggleSwitch;
            var isOn = toggle?.IsOn ?? false;

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
            var toggle = sender as ToggleSwitch;
            var isOn = toggle?.IsOn ?? false;

            SettingsService.SetUseStartupTdp(isOn);
            UpdateStartupTdpEnabledState();
        }

        private void UpdateStartupTdpEnabledState()
        {
            bool enable = TdpSettingsControl?.UseStartupTdpToggle?.IsOn ?? false;

            if (TdpSettingsControl?.StartupTdpPicker != null)
            {
                TdpSettingsControl.StartupTdpPicker.IsEnabled = enable;
                TdpSettingsControl.StartupTdpPicker.Opacity = enable ? 1.0 : 0.5;
            }
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

        private void LoadHotkeySettings()
        {
            try
            {
                string modifiers = SettingsService.GetHideShowHotkeyModifiers();
                string key = SettingsService.GetHideShowHotkeyKey();

                if (StartupOptionsControl?.HideShowHotkeySelector != null)
                {
                    StartupOptionsControl.HideShowHotkeySelector.SetHotkey(modifiers, key);
                    StartupOptionsControl.HideShowHotkeySelector.HotkeyChanged += HideShowHotkeySelector_HotkeyChanged;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading hotkey settings: {ex.Message}");
            }
        }

        private void HideShowHotkeySelector_HotkeyChanged(object sender, HUDRA.Controls.HotkeyChangedEventArgs e)
        {
            try
            {
                // Save the new hotkey settings
                SettingsService.SetHideShowHotkeyModifiers(e.Modifiers);
                SettingsService.SetHideShowHotkeyKey(e.Key);
                
                System.Diagnostics.Debug.WriteLine($"⚙️ Hotkey updated: {e.Modifiers} + {e.Key}");

                // Reload the hotkey configuration in TurboService
                ReloadTurboServiceHotkey();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving hotkey settings: {ex.Message}");
            }
        }

        private void ReloadTurboServiceHotkey()
        {
            try
            {
                // Get the MainWindow through the App instance  
                var app = App.Current as App;
                var mainWindow = app?.MainWindow;
                if (mainWindow != null)
                {
                    // Access the TurboService via reflection since it's private
                    var turboServiceField = typeof(MainWindow).GetField("_turboService", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    if (turboServiceField?.GetValue(mainWindow) is TurboService turboService)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚙️ Reloading TurboService hotkey configuration");
                        turboService.ReloadHotkeyConfiguration();
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"⚙️ TurboService not found or not initialized");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"⚙️ MainWindow not found");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reloading TurboService hotkey: {ex.Message}");
            }
        }

        private void LoadEnhancedScanningSettings()
        {
            // Enhanced library scanning settings are now loaded in LoadGameDetectionSettings()
        }

        private void EnhancedLibraryScanningToggle_Toggled(object sender, RoutedEventArgs e)
        {
            var toggle = sender as ToggleSwitch;
            var isOn = toggle?.IsOn ?? false;
            SettingsService.SetEnhancedLibraryScanningEnabled(isOn);
        }

        private void ScanIntervalComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var comboBox = sender as ComboBox;
            if (comboBox?.SelectedItem is ComboBoxItem selectedItem &&
                selectedItem.Tag is string tagValue &&
                int.TryParse(tagValue, out int intervalValue))
            {
                SettingsService.SetGameDatabaseRefreshInterval(intervalValue);
            }
        }

        private void LoadDatabaseStatus()
        {
            try
            {
                // Access the enhanced game detection service through the main window
                var app = App.Current as App;
                var mainWindow = app?.MainWindow;
                if (mainWindow != null)
                {
                    // Use reflection to access the enhanced game detection service
                    var enhancedServiceField = typeof(MainWindow).GetField("_enhancedGameDetectionService", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    if (enhancedServiceField?.GetValue(mainWindow) is EnhancedGameDetectionService enhancedService)
                    {
                        UpdateDatabaseStatusDisplay(enhancedService);
                    }
                    else
                    {
                        if (GameDetectionControl != null)
                        {
                            GameDetectionControl.LastUpdatedText = "Enhanced game detection service not available";
                        }
                    }
                }
                else
                {
                    if (GameDetectionControl != null)
                    {
                        GameDetectionControl.LastUpdatedText = "MainWindow not available";
                    }
                }
            }
            catch (Exception ex)
            {
                if (GameDetectionControl != null)
                {
                    GameDetectionControl.LastUpdatedText = $"Error loading database status: {ex.Message}";
                }
                System.Diagnostics.Debug.WriteLine($"Error loading database status: {ex.Message}");
            }
        }

        private void UpdateDatabaseStatusDisplay(EnhancedGameDetectionService enhancedService)
        {
            try
            {
                var stats = enhancedService.DatabaseStats;

                if (GameDetectionControl == null) return;

                // Update launcher counts from stats
                GameDetectionControl.BattleNetCount = stats.GamesBySource.TryGetValue(GameSource.BattleNet, out var battleNetCount) ? battleNetCount : 0;
                GameDetectionControl.EpicCount = stats.GamesBySource.TryGetValue(GameSource.Epic, out var epicCount) ? epicCount : 0;
                GameDetectionControl.GOGCount = stats.GamesBySource.TryGetValue(GameSource.GOG, out var gogCount) ? gogCount : 0;
                GameDetectionControl.OriginCount = stats.GamesBySource.TryGetValue(GameSource.Origin, out var originCount) ? originCount : 0;
                GameDetectionControl.RiotCount = stats.GamesBySource.TryGetValue(GameSource.Riot, out var riotCount) ? riotCount : 0;
                GameDetectionControl.RockstarCount = stats.GamesBySource.TryGetValue(GameSource.Rockstar, out var rockstarCount) ? rockstarCount : 0;
                GameDetectionControl.SteamCount = stats.GamesBySource.TryGetValue(GameSource.Steam, out var steamCount) ? steamCount : 0;
                GameDetectionControl.UbisoftCount = stats.GamesBySource.TryGetValue(GameSource.Ubisoft, out var ubisoftCount) ? ubisoftCount : 0;
                GameDetectionControl.XboxCount = stats.GamesBySource.TryGetValue(GameSource.Xbox, out var xboxCount) ? xboxCount : 0;
                GameDetectionControl.ManualCount = stats.GamesBySource.TryGetValue(GameSource.Manual, out var manualCount) ? manualCount : 0;

                // Update last updated text
                if (stats.LastUpdated != DateTime.MinValue)
                {
                    GameDetectionControl.LastUpdatedText = $"Last updated: {stats.LastUpdated:g}";
                }
                else
                {
                    GameDetectionControl.LastUpdatedText = "Last updated: Never";
                }
            }
            catch (Exception ex)
            {
                if (GameDetectionControl != null)
                {
                    GameDetectionControl.LastUpdatedText = $"Error: {ex.Message}";
                }
                System.Diagnostics.Debug.WriteLine($"Error updating database status: {ex.Message}");
            }
        }

        private async void ResetDatabaseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Get MainWindow for gamepad service access
                var app = Application.Current as App;
                var mainWindow = app?.MainWindow;

                // Show confirmation dialog with automatic gamepad support
                var dialog = new ContentDialog
                {
                    Title = "Reset Game Database",
                    Content = "This will clear all detected games and perform a fresh scan. Continue?",
                    PrimaryButtonText = "Ⓐ Yes",
                    CloseButtonText = "Ⓑ No",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot,
                    MaxWidth = 448 // Match standard ContentDialog width
                };

                var result = mainWindow != null
                    ? await dialog.ShowWithGamepadSupportAsync(mainWindow.GamepadNavigationService)
                    : await dialog.ShowAsync();

                if (result != ContentDialogResult.Primary)
                {
                    return; // User cancelled
                }

                var button = sender as Button;
                if (button != null)
                {
                    button.IsEnabled = false;
                    button.Content = "Resetting...";

                    // Force UI to render immediately before blocking database operations
                    await Task.Yield();
                }
                if (mainWindow != null)
                {
                    var enhancedServiceField = typeof(MainWindow).GetField("_enhancedGameDetectionService",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    if (enhancedServiceField?.GetValue(mainWindow) is EnhancedGameDetectionService enhancedService)
                    {
                        // Call ResetDatabaseAsync
                        var resetMethod = typeof(EnhancedGameDetectionService).GetMethod("ResetDatabaseAsync",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                        if (resetMethod != null)
                        {
                            await (Task)resetMethod.Invoke(enhancedService, null);

                            // Update the display after reset
                            UpdateDatabaseStatusDisplay(enhancedService);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error resetting database: {ex.Message}");
                if (GameDetectionControl != null)
                {
                    GameDetectionControl.LastUpdatedText = $"Error resetting database: {ex.Message}";
                }
            }
            finally
            {
                var button = sender as Button;
                if (button != null)
                {
                    button.IsEnabled = true;
                    button.Content = "Reset";
                }
            }
        }

        private async void CopyDebugInfoButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as Button;
                var originalContent = button?.Content;

                var debugInfo = new StringBuilder();
                debugInfo.AppendLine("=== HUDRA Debug Information ===");
                debugInfo.AppendLine();

                // Version
                debugInfo.AppendLine($"Version: {DebugLogger.GetAppVersion()}");

                // Windows version
                debugInfo.AppendLine($"OS: {Environment.OSVersion}");
                debugInfo.AppendLine($"64-bit OS: {Environment.Is64BitOperatingSystem}");

                // Admin status
                bool isAdmin = StartupService.IsRunningAsAdmin();
                debugInfo.AppendLine($"Running as Admin: {isAdmin}");

                // RyzenAdj status
                try
                {
                    using var tdpService = new TDPService();
                    debugInfo.AppendLine($"RyzenAdj Status: {tdpService.InitializationStatus}");
                    debugInfo.AppendLine($"RyzenAdj Mode: {(tdpService.IsDllMode ? "DLL (Fast)" : "EXE (Fallback)")}");

                    // Current TDP if available
                    var tdpResult = tdpService.GetCurrentTdp();
                    if (tdpResult.Success)
                    {
                        debugInfo.AppendLine($"Current TDP: {tdpResult.TdpWatts}W");
                    }
                }
                catch (Exception ex)
                {
                    debugInfo.AppendLine($"RyzenAdj Status: Error - {ex.Message}");
                }

                // Fan control status
                try
                {
                    var app = Application.Current as App;
                    var fanControlService = app?.FanControlService;
                    if (fanControlService != null)
                    {
                        debugInfo.AppendLine($"Fan Control Available: {fanControlService.IsDeviceAvailable}");
                        if (fanControlService.IsDeviceAvailable)
                        {
                            debugInfo.AppendLine($"Fan Control Device: {fanControlService.DeviceInfo}");
                        }
                    }
                    else
                    {
                        debugInfo.AppendLine("Fan Control Available: Not initialized");
                    }
                }
                catch (Exception ex)
                {
                    debugInfo.AppendLine($"Fan Control Status: Error - {ex.Message}");
                }

                // RTSS status
                try
                {
                    var rtssService = new RtssFpsLimiterService();
                    var rtssDetection = await rtssService.DetectRtssInstallationAsync();
                    debugInfo.AppendLine($"RTSS Installed: {rtssDetection.IsInstalled}");
                    if (rtssDetection.IsInstalled)
                    {
                        debugInfo.AppendLine($"RTSS Version: {rtssDetection.Version}");
                        debugInfo.AppendLine($"RTSS Running: {rtssDetection.IsRunning}");
                        debugInfo.AppendLine($"RTSS Path: {rtssDetection.InstallPath}");
                    }
                }
                catch (Exception ex)
                {
                    debugInfo.AppendLine($"RTSS Status: Error - {ex.Message}");
                }

                // Temperature if available
                try
                {
                    var app = Application.Current as App;
                    var tempMonitor = app?.TemperatureMonitor;
                    if (tempMonitor?.CurrentTemperature != null)
                    {
                        var tempData = tempMonitor.CurrentTemperature;
                        debugInfo.AppendLine($"CPU Temperature: {tempData.MaxTemperature:F1}°C");
                    }
                }
                catch
                {
                    // Temperature not available, skip
                }

                debugInfo.AppendLine();
                debugInfo.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                debugInfo.AppendLine("================================");

                // Copy to clipboard
                var dataPackage = new DataPackage();
                dataPackage.SetText(debugInfo.ToString());
                Clipboard.SetContent(dataPackage);

                // Update button text briefly to confirm
                if (button != null)
                {
                    button.Content = "Copied!";
                    await Task.Delay(2000);
                    button.Content = originalContent;
                }

                System.Diagnostics.Debug.WriteLine("Debug info copied to clipboard");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error copying debug info: {ex.Message}");

                // Show error in button
                var button = sender as Button;
                if (button != null)
                {
                    var originalContent = button.Content;
                    button.Content = "Error - try again";
                    await Task.Delay(2000);
                    button.Content = originalContent;
                }
            }
        }

    }
}