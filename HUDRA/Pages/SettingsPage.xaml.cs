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
            if (GameDetectionControl?.RefreshDatabaseButton != null)
            {
                GameDetectionControl.RefreshDatabaseButton.Click += RefreshDatabaseButton_Click;
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

        private async void RefreshDatabaseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as Button;
                if (button != null)
                {
                    button.IsEnabled = false;
                    button.Content = "Scanning...";

                    // Force UI to render immediately before blocking database operations
                    await Task.Yield();
                }

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
                if (GameDetectionControl != null)
                {
                    GameDetectionControl.LastUpdatedText = $"Error refreshing database: {ex.Message}";
                }
            }
            finally
            {
                var button = sender as Button;
                if (button != null)
                {
                    button.IsEnabled = true;
                    button.Content = "Re-Scan";
                }
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
                    XamlRoot = this.XamlRoot
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

        private async Task ShowAddManualGameDialog()
        {
            try
            {
                // Get MainWindow for gamepad service access
                var app = Application.Current as App;
                var mainWindow = app?.MainWindow;

                // Create dialog content
                var dialogContent = new StackPanel { Spacing = 12, MaxWidth = 450 };

                // Game Name input
                var nameLabel = new TextBlock
                {
                    Text = "Game Name:",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    FontSize = 14,
                    Margin = new Thickness(0, 0, 0, 5)
                };
                var nameTextBox = new TextBox
                {
                    PlaceholderText = "Enter game name",
                    Margin = new Thickness(0, 0, 0, 8)
                };

                // Executable location input
                var locationLabel = new TextBlock
                {
                    Text = "Executable Location:",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    FontSize = 14,
                    Margin = new Thickness(0, 0, 0, 5)
                };

                var locationTextBox = new TextBox
                {
                    PlaceholderText = "Path to .exe file",
                    IsReadOnly = true,
                    Margin = new Thickness(0, 0, 0, 8)
                };

                var browseButton = new Button
                {
                    Content = "Browse...",
                    MinWidth = 100,
                    MinHeight = 40,
                    Padding = new Thickness(10),
                    Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 74, 74, 74)), // #4A4A4A
                    BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 102, 102, 102)), // #666666
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    HorizontalAlignment = HorizontalAlignment.Left
                };

                browseButton.Click += async (s, e) =>
                {
                    try
                    {
                        // Get window handle - ensure we have a valid window
                        var currentMainWindow = (Application.Current as App)?.MainWindow;
                        if (currentMainWindow == null)
                        {
                            System.Diagnostics.Debug.WriteLine("MainWindow is null");
                            return;
                        }

                        var picker = new Windows.Storage.Pickers.FileOpenPicker();

                        // Initialize the picker with the window handle BEFORE setting properties
                        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(currentMainWindow);
                        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                        // Configure picker properties AFTER initialization
                        picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
                        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
                        picker.FileTypeFilter.Add(".exe");

                        var file = await picker.PickSingleFileAsync();
                        if (file != null)
                        {
                            locationTextBox.Text = file.Path;

                            // Auto-populate game name from exe filename if name is empty
                            if (string.IsNullOrWhiteSpace(nameTextBox.Text))
                            {
                                nameTextBox.Text = System.IO.Path.GetFileNameWithoutExtension(file.Path);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error opening file picker: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                    }
                };

                dialogContent.Children.Add(nameLabel);
                dialogContent.Children.Add(nameTextBox);
                dialogContent.Children.Add(locationLabel);
                dialogContent.Children.Add(locationTextBox);
                dialogContent.Children.Add(browseButton);

                // Create dialog
                var dialog = new ContentDialog
                {
                    Title = "Add Manual Game",
                    Content = dialogContent,
                    PrimaryButtonText = "Ⓐ Add",
                    CloseButtonText = "Ⓑ Cancel",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = this.XamlRoot
                };

                var result = mainWindow != null
                    ? await dialog.ShowWithGamepadSupportAsync(mainWindow.GamepadNavigationService)
                    : await dialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    // Validate inputs
                    var gameName = nameTextBox.Text?.Trim();
                    var exePath = locationTextBox.Text?.Trim();

                    if (string.IsNullOrWhiteSpace(gameName))
                    {
                        await ShowErrorDialog("Please enter a game name.");
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(exePath))
                    {
                        await ShowErrorDialog("Please select an executable file.");
                        return;
                    }

                    if (!System.IO.File.Exists(exePath))
                    {
                        await ShowErrorDialog("The selected executable file does not exist.");
                        return;
                    }

                    if (!exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        await ShowErrorDialog("Please select a valid .exe file.");
                        return;
                    }

                    // Add the game to the database
                    await AddManualGameToDatabase(gameName, exePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing add manual game dialog: {ex.Message}");
                await ShowErrorDialog($"An error occurred: {ex.Message}");
            }
        }

        private async Task AddManualGameToDatabase(string gameName, string exePath)
        {
            try
            {
                var app = App.Current as App;
                var mainWindow = app?.MainWindow;
                if (mainWindow == null) return;

                var enhancedServiceField = typeof(MainWindow).GetField("_enhancedGameDetectionService",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (enhancedServiceField?.GetValue(mainWindow) is EnhancedGameDetectionService enhancedService)
                {
                    // Create a DetectedGame object for the manual game
                    var manualGame = new DetectedGame
                    {
                        ProcessName = System.IO.Path.GetFileNameWithoutExtension(exePath),
                        DisplayName = gameName,
                        ExecutablePath = exePath,
                        InstallLocation = System.IO.Path.GetDirectoryName(exePath) ?? string.Empty,
                        Source = GameSource.Manual,
                        FirstDetected = DateTime.Now,
                        LastDetected = DateTime.Now
                    };

                    // Access the game database through reflection
                    var databaseField = typeof(EnhancedGameDetectionService).GetField("_gameDatabase",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    if (databaseField?.GetValue(enhancedService) is EnhancedGameDatabase database)
                    {
                        database.SaveGame(manualGame);

                        // Update the cached games dictionary
                        var cachedGamesField = typeof(EnhancedGameDetectionService).GetField("_cachedGames",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                        if (cachedGamesField?.GetValue(enhancedService) is Dictionary<string, DetectedGame> cachedGames)
                        {
                            cachedGames[manualGame.ProcessName] = manualGame;
                        }

                        // Update the display
                        UpdateDatabaseStatusDisplay(enhancedService);

                        System.Diagnostics.Debug.WriteLine($"Manual game added: {gameName} ({exePath})");

                        // Show success message
                        await ShowSuccessDialog($"Game '{gameName}' has been added successfully!");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding manual game to database: {ex.Message}");
                await ShowErrorDialog($"Failed to add game: {ex.Message}");
            }
        }

        private async Task ShowErrorDialog(string message)
        {
            var app = Application.Current as App;
            var mainWindow = app?.MainWindow;

            var errorDialog = new ContentDialog
            {
                Title = "Error",
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };

            if (mainWindow != null)
                await errorDialog.ShowWithGamepadSupportAsync(mainWindow.GamepadNavigationService);
            else
                await errorDialog.ShowAsync();
        }

        private async Task ShowSuccessDialog(string message)
        {
            var app = Application.Current as App;
            var mainWindow = app?.MainWindow;

            var successDialog = new ContentDialog
            {
                Title = "Success",
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };

            if (mainWindow != null)
                await successDialog.ShowWithGamepadSupportAsync(mainWindow.GamepadNavigationService);
            else
                await successDialog.ShowAsync();
        }

    }
}