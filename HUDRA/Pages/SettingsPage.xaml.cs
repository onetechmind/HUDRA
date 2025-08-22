using HUDRA.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

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
        }

        private void LoadSettings()
        {

            TdpCorrectionToggle.IsOn = SettingsService.GetTdpCorrectionEnabled();
            UseStartupTdpToggle.IsOn = SettingsService.GetUseStartupTdp();
            UpdateStartupTdpEnabledState();
            LoadStartupSettings();
            LoadHotkeySettings();
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

        private void StartRtssWithHudraToggle_Toggled(object sender, RoutedEventArgs e)
        {
            var isOn = StartRtssWithHudraToggle.IsOn;
            SettingsService.SetStartRtssWithHudra(isOn);
        }

        private void LoadRtssSettings()
        {
            StartRtssWithHudraToggle.IsOn = SettingsService.GetStartRtssWithHudra();
        }

        private void UpdateMinimizeOnStartupState()
        {
            // Enable/disable based on admin status only - independent of startup toggle
            bool enable = StartupService.IsRunningAsAdmin();

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
                
                // Set initial value if expander is already expanded
                if (TdpSettingsExpander.IsExpanded)
                {
                    RefreshTdpPickerDisplay();
                }
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
            // Use a small delay to allow the expander content to fully render
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                // Only refresh scroll position without disrupting selection state
                StartupTdpPicker.EnsureScrollPositionAfterLayout();
            });
        }

        private void SetTdpPickerValue()
        {
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

        private void LoadHotkeySettings()
        {
            try
            {
                string modifiers = SettingsService.GetHideShowHotkeyModifiers();
                string key = SettingsService.GetHideShowHotkeyKey();
                
                HideShowHotkeySelector.SetHotkey(modifiers, key);
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
            // Load enhanced library scanning settings
            EnhancedLibraryScanningToggle.IsOn = SettingsService.IsEnhancedLibraryScanningEnabled();
            
            // Load and set scan interval ComboBox
            int currentInterval = SettingsService.GetGameDatabaseRefreshInterval();
            foreach (ComboBoxItem item in ScanIntervalComboBox.Items)
            {
                if (item.Tag is string tagValue && int.TryParse(tagValue, out int intervalValue))
                {
                    if (intervalValue == currentInterval)
                    {
                        ScanIntervalComboBox.SelectedItem = item;
                        break;
                    }
                }
            }
            
            // If no match found, default to 15 minutes
            if (ScanIntervalComboBox.SelectedItem == null)
            {
                ScanIntervalComboBox.SelectedIndex = 1; // 15 minutes option
            }
        }

        private void EnhancedLibraryScanningToggle_Toggled(object sender, RoutedEventArgs e)
        {
            var isOn = EnhancedLibraryScanningToggle.IsOn;
            SettingsService.SetEnhancedLibraryScanningEnabled(isOn);
        }

        private void ScanIntervalComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ScanIntervalComboBox.SelectedItem is ComboBoxItem selectedItem && 
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
                        DatabaseStatusText.Text = "Enhanced game detection service not available";
                    }
                }
                else
                {
                    DatabaseStatusText.Text = "MainWindow not available";
                }
            }
            catch (Exception ex)
            {
                DatabaseStatusText.Text = $"Error loading database status: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Error loading database status: {ex.Message}");
            }
        }

        private void UpdateDatabaseStatusDisplay(EnhancedGameDetectionService enhancedService)
        {
            try
            {
                var stats = enhancedService.DatabaseStats;
                var statusLines = new List<string>
                {
                    $"Total Games: {stats.TotalGames}",
                    $"Database Size: {stats.GetFormattedSize()}"
                };

                if (stats.GamesBySource.Any())
                {
                    statusLines.Add("Games by Source:");
                    foreach (var sourceGroup in stats.GamesBySource.OrderByDescending(kvp => kvp.Value))
                    {
                        statusLines.Add($"  {sourceGroup.Key}: {sourceGroup.Value}");
                    }
                }

                if (stats.LastUpdated != DateTime.MinValue)
                {
                    statusLines.Add($"Last Updated: {stats.LastUpdated:g}");
                }

                DatabaseStatusText.Text = string.Join("\n", statusLines);
            }
            catch (Exception ex)
            {
                DatabaseStatusText.Text = $"Error updating database status: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Error updating database status: {ex.Message}");
            }
        }

        private async void RefreshDatabaseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RefreshDatabaseButton.IsEnabled = false;
                RefreshDatabaseButton.Content = "Scanning...";

                var app = App.Current as App;
                var mainWindow = app?.MainWindow;
                if (mainWindow != null)
                {
                    var enhancedServiceField = typeof(MainWindow).GetField("_enhancedGameDetectionService", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    if (enhancedServiceField?.GetValue(mainWindow) is EnhancedGameDetectionService enhancedService)
                    {
                        // Trigger a manual refresh (this will call RefreshGameDatabaseAsync)
                        var refreshMethod = typeof(EnhancedGameDetectionService).GetMethod("RefreshGameDatabaseAsync", 
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        
                        if (refreshMethod != null)
                        {
                            await (Task)refreshMethod.Invoke(enhancedService, null);
                            
                            // Update the display after refresh
                            UpdateDatabaseStatusDisplay(enhancedService);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error refreshing database: {ex.Message}");
                DatabaseStatusText.Text = $"Error refreshing database: {ex.Message}";
            }
            finally
            {
                RefreshDatabaseButton.IsEnabled = true;
                RefreshDatabaseButton.Content = "Re-Scan";
            }
        }

    }
}