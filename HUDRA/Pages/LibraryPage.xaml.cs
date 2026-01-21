using HUDRA.Models;
using HUDRA.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Gaming.Input;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.System;

namespace HUDRA.Pages
{
    public sealed partial class LibraryPage : Page, INotifyPropertyChanged
    {
        private EnhancedGameDetectionService? _gameDetectionService;
        private GameLauncherService? _gameLauncherService;
        private GamepadNavigationService? _gamepadNavigationService;
        private ScrollViewer? _contentScrollViewer; // MainWindow's ContentScrollViewer
        private ObservableCollection<DetectedGame> _games = new ObservableCollection<DetectedGame>();
        private readonly SecureStorageService _secureStorage = new();

        // State preservation - STATIC fields persist across page recreation
        // Despite NavigationCacheMode.Enabled, page is still being recreated (framework issue)
        private static double _savedScrollOffset = 0;
        private static string? _savedFocusedGameProcessName = null;
        private static bool _lastUsedGamepadInput = false; // Track input method for smart focus restoration

        // Static property to pass selected game to GameSettingsPage
        public static string? SelectedGameProcessName { get; set; }
        private bool _gamesLoaded = false;
        private bool _isRestoringScroll = false; // Flag to ignore ViewChanged during programmatic scroll restoration

        // Gamepad navigation
        private readonly HashSet<GamepadButtons> _pressedButtons = new();
        private DateTime _lastInputTime = DateTime.MinValue;
        private const double INPUT_REPEAT_DELAY_MS = 150;

        // Button zone navigation (Add Game / Rescan / Random buttons above game tiles)
        private enum LibraryFocusZone { Tiles, Buttons }
        private LibraryFocusZone _currentZone = LibraryFocusZone.Tiles;
        private int _buttonFocusIndex = 0;  // 0 = Add Game, 1 = Rescan, 2 = Random
        private const int LIBRARY_BUTTON_COUNT = 3;  // Number of buttons in the management row

        // Roulette state tracking
        private bool _isRouletteActive = false;
        private bool _isRouletteCancelled = false;
        private CancellationTokenSource? _rouletteCts;
        private static int _lastRouletteIndex = -1;  // Track last selection to avoid repeats
        private MediaPlayer? _rouletteTickPlayer;
        private MediaPlayer? _rouletteWinnerPlayer;

        public event PropertyChangedEventHandler? PropertyChanged;

        public LibraryPage()
        {
            this.InitializeComponent();

            // Initialize game launcher service
            _gameLauncherService = new GameLauncherService();

            // Subscribe to pointer events to detect mouse clicks (input method switch)
            this.PointerPressed += OnPagePointerPressed;

            // Subscribe to key events to detect keyboard input (input method switch)
            this.KeyDown += OnPageKeyDown;
        }

        private void OnPagePointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // Mouse/touch click detected - switch to mouse input mode
            if (_lastUsedGamepadInput)
            {
                _lastUsedGamepadInput = false;
                // Note: Don't clear _savedFocusedGameProcessName here - let the user click on a tile
                // to naturally update focus, or let it persist if they navigate away and back
            }
        }

        private void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
        {
            // Keyboard input detected - switch to keyboard input mode
            // Ignore gamepad buttons (they're handled separately)
            if (_lastUsedGamepadInput && !IsGamepadKey(e.Key))
            {
                _lastUsedGamepadInput = false;
                // Note: Don't clear _savedFocusedGameProcessName here
            }
        }

        private bool IsGamepadKey(Windows.System.VirtualKey key)
        {
            // GamepadA through GamepadRightThumbstickLeft are gamepad keys
            return key >= Windows.System.VirtualKey.GamepadA && key <= Windows.System.VirtualKey.GamepadRightThumbstickLeft;
        }

        private void OnScrollViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            // Continuously update saved scroll position as user scrolls
            // This ensures we always have the latest position, regardless of when navigation occurs
            if (_contentScrollViewer != null)
            {
                _savedScrollOffset = _contentScrollViewer.VerticalOffset;

                // ONLY track input method if this is a user-initiated scroll, NOT programmatic restoration
                // This prevents RestoreScrollPositionAsync from incorrectly resetting the gamepad flag
                // Also don't clear if we're currently using gamepad input (would break focus restoration)
                if (!_isRestoringScroll && !e.IsIntermediate && !_lastUsedGamepadInput)
                {
                    // Clear saved focus when switching to mouse/keyboard - no tile is selected
                    _savedFocusedGameProcessName = null;
                }
            }
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // Only load games if not already loaded (to preserve scroll position)
            if (!_gamesLoaded)
            {
                await LoadGamesAsync();
            }

            // Check if SGDB hint should be shown
            await CheckSgdbHintVisibilityAsync();

            // Update Rescan button enabled state based on Library Scanning setting
            UpdateRescanButtonState();

            // If we have a saved focused game, scroll to it and focus it
            // Otherwise, restore the scroll position only
            if (!string.IsNullOrEmpty(_savedFocusedGameProcessName))
            {
                // RestoreFocusedGameAsync will scroll to the button and focus it
                await RestoreFocusedGameAsync();
            }
            else
            {
                // No saved focus - restore scroll position to where user was
                await Task.Delay(200);
                await RestoreScrollPositionAsync();
            }

            // If scanning is already in progress, show the indicator
            if (_gameDetectionService != null && _gameDetectionService.IsScanning)
            {
                ShowScanProgress("Scanning...");
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            // SaveScrollPosition() is called from MainWindow before this fires,
            // and it already unsubscribes from ViewChanged

            // Unsubscribe from raw gamepad input (resubscribed on next Initialize)
            if (_gamepadNavigationService != null)
            {
                _gamepadNavigationService.RawGamepadInput -= OnRawGamepadInput;
            }

            // Reset button zone to Tiles so gamepad focus goes to tiles on return
            _currentZone = LibraryFocusZone.Tiles;

            // Note: We keep scan event subscriptions active for reactive updates
        }

        /// <summary>
        /// Saves scroll position and focused game when navigating away.
        /// IMMEDIATELY unsubscribes from events to prevent handler accumulation.
        /// </summary>
        public void SaveScrollPosition()
        {
            if (_contentScrollViewer != null)
            {
                // CRITICAL: Unsubscribe IMMEDIATELY to prevent other pages from overwriting _savedScrollOffset
                // This must happen before the next page loads and scrolls ContentScrollViewer to 0
                _contentScrollViewer.ViewChanged -= OnScrollViewChanged;
            }

            // CRITICAL: Unsubscribe from gamepad input to prevent handler accumulation
            // OnNavigatedFrom may not fire because NavigationService uses direct Content assignment
            // instead of Frame.Navigate(), bypassing the navigation lifecycle
            if (_gamepadNavigationService != null)
            {
                _gamepadNavigationService.RawGamepadInput -= OnRawGamepadInput;
            }

            // NOTE: Focused game is now tracked continuously via SaveCurrentlyFocusedGame()
            // during D-pad navigation, so we don't need to check FocusManager here.
            // The _savedFocusedGameProcessName static field already contains the last focused game.
        }

        public async Task Initialize(EnhancedGameDetectionService gameDetectionService, GamepadNavigationService gamepadNavigationService, ScrollViewer contentScrollViewer, bool isGamepadNavigation = false)
        {
            // Defensive null checks
            if (gameDetectionService == null)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Initialize: gameDetectionService is NULL!");
                return;
            }
            if (gamepadNavigationService == null)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Initialize: gamepadNavigationService is NULL!");
                return;
            }
            if (contentScrollViewer == null)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ Initialize: contentScrollViewer is NULL!");
                return;
            }

            _gameDetectionService = gameDetectionService;
            _gamepadNavigationService = gamepadNavigationService;
            _contentScrollViewer = contentScrollViewer;

            // Unsubscribe first to prevent handler accumulation (Initialize called on every navigation)
            // The -= on a non-existent subscription is a safe no-op
            _gamepadNavigationService.RawGamepadInput -= OnRawGamepadInput;
            _gamepadNavigationService.RawGamepadInput += OnRawGamepadInput;

            // Same pattern for scroll viewer to prevent accumulation
            _contentScrollViewer.ViewChanged -= OnScrollViewChanged;
            _contentScrollViewer.ViewChanged += OnScrollViewChanged;

            // Same pattern for scan events to prevent accumulation
            // Unsubscribe first (safe no-op if not subscribed), then subscribe
            _gameDetectionService.ScanningStateChanged -= OnScanningStateChanged;
            _gameDetectionService.ScanningStateChanged += OnScanningStateChanged;
            _gameDetectionService.ScanProgressChanged -= OnScanProgressChanged;
            _gameDetectionService.ScanProgressChanged += OnScanProgressChanged;

            // Load games only if not already loaded
            if (!_gamesLoaded)
            {
                await LoadGamesAsync();
            }

            // Check if SGDB hint should be shown
            await CheckSgdbHintVisibilityAsync();

            // Update Rescan button enabled state based on Library Scanning setting
            UpdateRescanButtonState();

            // Focus logic per UX requirements:
            // 1. If saved focus exists: restore it (gamepad navigation only)
            // 2. Else if navigated via gamepad: focus first tile
            // 3. Else (mouse/keyboard): no auto-focus
            //
            // Use isGamepadNavigation (from L1/R1) OR _lastUsedGamepadInput (tracks if user was using gamepad)
            // This ensures focus is restored when returning from GameSettings via gamepad Back button
            bool shouldRestoreFocus = isGamepadNavigation || _lastUsedGamepadInput;

            if (isGamepadNavigation)
            {
                _lastUsedGamepadInput = true; // Explicitly mark as gamepad input for L1/R1 navigation
            }

            if (shouldRestoreFocus)
            {
                if (!string.IsNullOrEmpty(_savedFocusedGameProcessName))
                {
                    // Restore previously focused game
                    await RestoreFocusedGameAsync();
                }
                else
                {
                    // Auto-select first tile
                    await FocusFirstGameTileAsync();
                }
            }
            // else: No auto-focus for mouse/keyboard navigation

            // THEN restore scroll position to override any focus-induced scrolling
            await Task.Delay(200);
            await RestoreScrollPositionAsync();

            // If scanning is already in progress, show the indicator
            if (_gameDetectionService.IsScanning)
            {
                ShowScanProgress("Scanning...");
            }
        }

        private async Task LoadGamesAsync()
        {
            try
            {
                if (_gameDetectionService == null)
                {
                    ShowEmptyState();
                    return;
                }

                // Get all games from the database
                var allGames = await _gameDetectionService.GetAllGamesAsync();
                var gamesList = allGames?.ToList() ?? new List<DetectedGame>();

                if (!gamesList.Any())
                {
                    ShowEmptyState();
                    return;
                }

                // Sort alphabetically by display name (null-safe)
                gamesList = gamesList.OrderBy(g => g?.DisplayName ?? "").ToList();

                // Add cache-busting timestamp to force WinUI Image controls to reload artwork
                // Without this, Image controls serve cached bitmaps even when files change
                var cacheBustTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                // Update the ObservableCollection
                _games.Clear();
                foreach (var game in gamesList)
                {
                    if (game == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ LoadGamesAsync: Found NULL game object in database results - SKIPPING!");
                        continue; // Skip null entries - don't add them to the collection
                    }

                    // Add timestamp query parameter to artwork path for cache busting
                    // This prevents Image control from serving stale cached images when artwork changes
                    if (!string.IsNullOrEmpty(game.ArtworkPath) && File.Exists(game.ArtworkPath))
                    {
                        game.ArtworkPath = $"{game.ArtworkPath}?t={cacheBustTimestamp}";
                    }

                    _games.Add(game);
                }

                // Set ItemsSource (clear and reset to force rebinding)
                GamesItemsControl.ItemsSource = null;
                GamesItemsControl.ItemsSource = _games;

                // Hide empty state
                EmptyStatePanel.Visibility = Visibility.Collapsed;

                // Mark games as loaded
                _gamesLoaded = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LibraryPage: Error loading games: {ex.Message}");
                ShowEmptyState();
            }
        }

        private void ShowEmptyState()
        {
            EmptyStatePanel.Visibility = Visibility.Visible;
            GamesItemsControl.ItemsSource = null;
        }

        /// <summary>
        /// Refreshes artwork for a specific game in the library.
        /// Called after artwork is updated in GameSettingsPage.
        /// Sets flag to force reload when page is navigated to.
        /// </summary>
        /// <param name="processName">ProcessName of the game to refresh</param>
        public void RefreshGameArtwork(string processName)
        {
            RefreshLibrary();
        }

        /// <summary>
        /// Forces a full library reload on next navigation.
        /// Called after adding manual games or updating artwork.
        /// </summary>
        public void RefreshLibrary()
        {
            System.Diagnostics.Debug.WriteLine($"LibraryPage: Setting reload flag and clearing scroll for library refresh");

            // Set flag to force reload when page is navigated to
            // InitializeLibraryPage will call LoadGamesAsync when it sees _gamesLoaded = false
            _gamesLoaded = false;

            // Clear saved scroll position - fresh load should start at top
            _savedScrollOffset = 0;
        }

        private async void GameTile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not DetectedGame game)
            {
                return;
            }

            Border? overlay = null;

            try
            {
                // Find the launching overlay in this button's visual tree
                overlay = FindTileLaunchingOverlay(button);
                if (overlay != null)
                {
                    overlay.Visibility = Visibility.Visible;
                }

                // Apply per-game profile IMMEDIATELY before launching
                // This ensures settings are applied right away instead of waiting for detection
                var app = Application.Current as App;
                var mainWindow = app?.MainWindow;
                if (mainWindow != null)
                {
                    var profileApplied = await mainWindow.ApplyGameProfileAsync(game.ProcessName, game.DisplayName);
                    if (profileApplied)
                    {
                        System.Diagnostics.Debug.WriteLine($"LibraryPage: Profile applied for {game.DisplayName} before launch");
                    }
                }

                // Launch the game
                bool success = _gameLauncherService?.LaunchGame(game) ?? false;

                if (!success)
                {
                    System.Diagnostics.Debug.WriteLine($"LibraryPage: Failed to launch {game.DisplayName}");
                }

                // Hide launching indicator after a delay
                await Task.Delay(3000);
                if (overlay != null)
                {
                    overlay.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LibraryPage: Error launching game: {ex.Message}");
                if (overlay != null)
                {
                    overlay.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void GameTile_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            // Find and show the settings overlay
            var settingsOverlay = FindTileSettingsOverlay(button);
            if (settingsOverlay != null)
            {
                settingsOverlay.Visibility = Visibility.Visible;
            }
        }

        private void GameTile_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not Button button)
                return;

            // Find and hide the settings overlay
            var settingsOverlay = FindTileSettingsOverlay(button);
            if (settingsOverlay != null)
            {
                settingsOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void SettingsIcon_Click(object sender, RoutedEventArgs e)
        {
            // Prevent the click from bubbling to the tile button
            if (sender is not Button settingsButton)
                return;

            // Find the parent game tile button to get the game data
            var parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(settingsButton);
            while (parent != null)
            {
                if (parent is Button tileButton && tileButton.Tag is DetectedGame game)
                {
                    // Store the game ProcessName and navigate to settings
                    SelectedGameProcessName = game.ProcessName;

                    // Navigate to GameSettingsPage
                    var app = Application.Current as App;
                    var mainWindow = app?.MainWindow;
                    var navigationService = mainWindow?.NavigationService;
                    navigationService?.NavigateToGameSettings();
                    return;
                }
                parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent);
            }
        }

        private Border? FindTileSettingsOverlay(DependencyObject parent)
        {
            int childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);

                if (child is Border border && border.Name == "TileSettingsOverlay")
                {
                    return border;
                }

                var result = FindTileSettingsOverlay(child);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }

        private Border? FindTileLaunchingOverlay(DependencyObject parent)
        {
            int childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);

                if (child is Border border && border.Name == "TileLaunchingOverlay")
                {
                    return border;
                }

                var result = FindTileLaunchingOverlay(child);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }

        private void OnScanningStateChanged(object? sender, bool isScanning)
        {
            // Update UI on the UI thread
            DispatcherQueue.TryEnqueue(() =>
            {
                if (isScanning)
                {
                    ShowScanProgress("Scanning...");
                }
                else
                {
                    HideScanProgress();
                    // Scan completed - refresh the game list
                    // Reset _gamesLoaded BEFORE loading to ensure reload on next navigation
                    // even if this LoadGamesAsync call fails (page not visible, exception, etc.)
                    _gamesLoaded = false;
                    _ = LoadGamesAsync();
                }
            });
        }

        private void OnScanProgressChanged(object? sender, string progress)
        {
            // Update progress text on the UI thread
            DispatcherQueue.TryEnqueue(() =>
            {
                ShowScanProgress(progress);
            });
        }

        private void ShowScanProgress(string message)
        {
            ScanProgressText.Text = message;
            ScanProgressIndicator.Visibility = Visibility.Visible;
        }

        private void HideScanProgress()
        {
            ScanProgressIndicator.Visibility = Visibility.Collapsed;
        }

        private async Task RestoreScrollPositionAsync()
        {
            if (_contentScrollViewer == null || _savedScrollOffset == 0)
            {
                return; // No need to restore if at top
            }

            // Wait for content to be fully loaded and measured
            // Retry a few times until the extent height is calculated
            for (int attempt = 0; attempt < 5; attempt++)
            {
                _contentScrollViewer.UpdateLayout();
                await Task.Delay(100);

                // Check if the ScrollViewer has measured its content
                if (_contentScrollViewer.ExtentHeight > 0)
                {
                    break;
                }
            }

            // Set flag to prevent ViewChanged from resetting _lastUsedGamepadInput
            _isRestoringScroll = true;

            // Restore the scroll position
            _contentScrollViewer.ChangeView(null, _savedScrollOffset, null, disableAnimation: true);

            // Verify restoration worked
            await Task.Delay(100);

            // Clear the flag after restoration is complete
            _isRestoringScroll = false;
        }

        private async Task RestoreFocusedGameAsync()
        {
            if (string.IsNullOrEmpty(_savedFocusedGameProcessName)) return;

            // Wait for UI to be fully rendered
            await Task.Delay(100);

            // Null check for GamesItemsControl
            if (GamesItemsControl == null)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ RestoreFocusedGameAsync: GamesItemsControl is NULL!");
                return;
            }

            // Find the game in our data source first
            var game = _games.FirstOrDefault(g => g.ProcessName == _savedFocusedGameProcessName);
            if (game == null)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ RestoreFocusedGameAsync: Game '{_savedFocusedGameProcessName}' not found in collection");
                _savedFocusedGameProcessName = null;
                return;
            }

            // Try to find and focus the button, with retries for virtualization
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var gameButton = FindGameButton(_savedFocusedGameProcessName);
                if (gameButton != null)
                {
                    // Use BringIntoViewOptions for better scroll positioning
                    var options = new BringIntoViewOptions
                    {
                        AnimationDesired = false,  // Instant scroll
                        VerticalAlignmentRatio = 0.3  // Position near top of viewport
                    };
                    gameButton.StartBringIntoView(options);

                    // Wait for scroll to settle
                    await Task.Delay(150);

                    // Focus the button
                    gameButton.Focus(FocusState.Programmatic);
                    SaveCurrentlyFocusedGame(gameButton);
                    System.Diagnostics.Debug.WriteLine($"✅ RestoreFocusedGameAsync: Focused '{_savedFocusedGameProcessName}' on attempt {attempt + 1}");
                    return;
                }

                // Button not found - might be virtualized (off-screen)
                // Scroll toward the item to force rendering
                System.Diagnostics.Debug.WriteLine($"🔄 RestoreFocusedGameAsync: Button not found, scrolling to item (attempt {attempt + 1})");
                await ScrollToGameIndex(game);
                await Task.Delay(100);
            }

            // If still not found after retries, clear saved focus
            System.Diagnostics.Debug.WriteLine($"⚠️ RestoreFocusedGameAsync: Failed to find button after 3 attempts, clearing saved focus");
            _savedFocusedGameProcessName = null;
        }

        /// <summary>
        /// Scrolls the view to make the specified game visible, based on its index in the collection.
        /// Used to force virtualized items to be rendered before focusing.
        /// </summary>
        private async Task ScrollToGameIndex(DetectedGame game)
        {
            int index = _games.IndexOf(game);
            if (index < 0 || _contentScrollViewer == null) return;

            // Estimate scroll position based on index
            // Layout: 2 columns, each row is approximately 130px (tile height + spacing)
            int row = index / 2;
            double estimatedOffset = row * 130;

            _contentScrollViewer.ChangeView(null, estimatedOffset, null, disableAnimation: true);
            await Task.Yield(); // Allow UI to process the scroll
        }

        /// <summary>
        /// Focuses the first tile in the grid (top-left, index 0).
        /// Used when navigating via gamepad with no saved focus.
        /// </summary>
        private async Task FocusFirstGameTileAsync()
        {
            // Wait for UI to be fully rendered
            await Task.Delay(100);

            // Null check for GamesItemsControl
            if (GamesItemsControl == null)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ FocusFirstGameTile: GamesItemsControl is NULL!");
                return;
            }

            // Get all game buttons
            var allButtons = FindAllGameButtonsInVisualTree(GamesItemsControl);
            if (allButtons.Count == 0)
            {
                return;
            }

            // Focus the very first button (index 0)
            allButtons[0].Focus(FocusState.Programmatic);
            SaveCurrentlyFocusedGame(allButtons[0]);
        }

        /// <summary>
        /// Saves the currently focused game for restoration on next navigation.
        /// Called whenever focus changes via D-pad navigation.
        /// </summary>
        private void SaveCurrentlyFocusedGame(Button button)
        {
            if (button?.Tag is DetectedGame game)
            {
                _savedFocusedGameProcessName = game.ProcessName;
            }
        }

        /// <summary>
        /// Focuses the top-left visible tile in the current viewport.
        /// Used when user was scrolling with mouse and then navigates back.
        /// </summary>
        private async Task FocusFirstVisibleTileAsync()
        {
            if (_contentScrollViewer == null)
            {
                return;
            }

            // Wait for UI to be fully rendered
            await Task.Delay(100);

            // Get all game buttons
            var allButtons = FindAllGameButtonsInVisualTree(GamesItemsControl);
            if (allButtons.Count == 0)
            {
                return;
            }

            // Get current scroll position
            double currentScrollOffset = _contentScrollViewer.VerticalOffset;
            double viewportHeight = _contentScrollViewer.ViewportHeight;

            // Find the first button that is fully or partially visible in the viewport
            Button? firstVisibleButton = null;
            foreach (var button in allButtons)
            {
                try
                {
                    // Get button's position relative to the content
                    var transform = button.TransformToVisual(GamesItemsControl);
                    var position = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                    double buttonTop = position.Y;
                    double buttonBottom = position.Y + button.ActualHeight;

                    // Check if button is visible in viewport
                    // A button is visible if its bottom edge is below the scroll position
                    // and its top edge is above the scroll position + viewport height
                    bool isVisible = buttonBottom > currentScrollOffset &&
                                    buttonTop < (currentScrollOffset + viewportHeight);

                    if (isVisible)
                    {
                        firstVisibleButton = button;
                        break; // Buttons are already sorted top-to-bottom, so first visible is top-left
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"LibraryPage: Error checking button visibility: {ex.Message}");
                }
            }

            // Focus the first visible button
            if (firstVisibleButton != null)
            {
                firstVisibleButton.Focus(FocusState.Programmatic);
            }
            else
            {
                // Fallback: focus the very first button if no visible button found
                allButtons[0].Focus(FocusState.Programmatic);
            }
        }

        private Button? FindGameButton(string processName)
        {
            // Search through the visual tree to find all game buttons
            var allButtons = FindAllGameButtonsInVisualTree(GamesItemsControl);

            // Find the button with matching ProcessName
            foreach (var button in allButtons)
            {
                if (button.Tag is DetectedGame game &&
                    game.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
                {
                    return button;
                }
            }

            return null;
        }

        private List<Button> FindAllGameButtonsInVisualTree(DependencyObject parent)
        {
            var buttons = new List<Button>();
            int childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);

            for (int i = 0; i < childCount; i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);

                // If this is a game button, add it to the list
                if (child is Button button && button.Tag is DetectedGame)
                {
                    buttons.Add(button);
                }

                // Recursively search children
                buttons.AddRange(FindAllGameButtonsInVisualTree(child));
            }

            // Sort buttons by position (top-to-bottom, left-to-right) to ensure correct navigation order
            try
            {
                buttons = buttons.OrderBy(b =>
                {
                    try
                    {
                        var transform = b.TransformToVisual(GamesItemsControl);
                        var position = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                        // Sort by Y position first (row), then X position (column)
                        return position.Y * 10000 + position.X;
                    }
                    catch
                    {
                        return double.MaxValue; // Put errored buttons at the end
                    }
                }).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LibraryPage: Error sorting buttons: {ex.Message}");
            }

            return buttons;
        }

        public async void FocusFirstGameButton()
        {
            try
            {
                // Don't override if we already have a saved focus position
                if (!string.IsNullOrEmpty(_savedFocusedGameProcessName))
                {
                    return;
                }

                // Wait for buttons to be rendered and positioned
                await Task.Delay(300);

                var allButtons = FindAllGameButtonsInVisualTree(GamesItemsControl);

                if (allButtons.Count > 0)
                {
                    // Focus the first button WITHOUT scrolling to preserve scroll position
                    allButtons[0].Focus(FocusState.Programmatic);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LibraryPage: Error focusing first button: {ex.Message}");
            }
        }

        private void OnRawGamepadInput(object? sender, GamepadReading reading)
        {
            try
            {
                ProcessGamepadInput(reading);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LibraryPage: Error processing gamepad input: {ex.Message}");
            }
        }

        private void ProcessGamepadInput(GamepadReading reading)
        {
            // Check for new button presses
            var newButtons = new List<GamepadButtons>();
            foreach (GamepadButtons button in Enum.GetValues(typeof(GamepadButtons)))
            {
                if (reading.Buttons.HasFlag(button) && !_pressedButtons.Contains(button))
                {
                    newButtons.Add(button);
                    _pressedButtons.Add(button);
                }
                else if (!reading.Buttons.HasFlag(button) && _pressedButtons.Contains(button))
                {
                    _pressedButtons.Remove(button);
                }
            }

            // Check for repeat navigation
            bool shouldProcessRepeats = (DateTime.Now - _lastInputTime).TotalMilliseconds >= INPUT_REPEAT_DELAY_MS;

            // Track if we navigated (to prevent scroll conflict)
            bool navigated = false;

            // Handle D-pad AND left analog stick navigation (both new presses and repeats)
            if (newButtons.Contains(GamepadButtons.DPadUp) || (shouldProcessRepeats && reading.Buttons.HasFlag(GamepadButtons.DPadUp)) ||
                (shouldProcessRepeats && reading.LeftThumbstickY > 0.7))
            {
                NavigateUp();
                _lastInputTime = DateTime.Now;
                navigated = true;
            }
            else if (newButtons.Contains(GamepadButtons.DPadDown) || (shouldProcessRepeats && reading.Buttons.HasFlag(GamepadButtons.DPadDown)) ||
                     (shouldProcessRepeats && reading.LeftThumbstickY < -0.7))
            {
                NavigateDown();
                _lastInputTime = DateTime.Now;
                navigated = true;
            }

            if (newButtons.Contains(GamepadButtons.DPadLeft) || (shouldProcessRepeats && reading.Buttons.HasFlag(GamepadButtons.DPadLeft)) ||
                (shouldProcessRepeats && reading.LeftThumbstickX < -0.7))
            {
                NavigateLeft();
                _lastInputTime = DateTime.Now;
                navigated = true;
            }
            else if (newButtons.Contains(GamepadButtons.DPadRight) || (shouldProcessRepeats && reading.Buttons.HasFlag(GamepadButtons.DPadRight)) ||
                     (shouldProcessRepeats && reading.LeftThumbstickX > 0.7))
            {
                NavigateRight();
                _lastInputTime = DateTime.Now;
                navigated = true;
            }

            // Handle A button to launch game
            if (newButtons.Contains(GamepadButtons.A))
            {
                InvokeFocusedButton();
            }

            // Handle B button - cancel roulette if active
            if (newButtons.Contains(GamepadButtons.B))
            {
                if (_isRouletteActive)
                {
                    CancelRoulette();
                }
            }

            // Handle X button to open game settings
            if (newButtons.Contains(GamepadButtons.X))
            {
                OpenGameSettingsForFocusedTile();
            }

            // Handle right analog stick for scrolling (only when not navigating with left stick)
            if (!navigated && Math.Abs(reading.RightThumbstickY) > 0.2)
            {
                ScrollWithAnalogStick(reading.RightThumbstickY);
            }
        }

        private void NavigateUp()
        {
            // Track gamepad navigation input
            _lastUsedGamepadInput = true;

            // If currently in buttons zone, can't go higher
            if (_currentZone == LibraryFocusZone.Buttons)
            {
                // Scroll to top to show buttons clearly
                if (_contentScrollViewer != null)
                {
                    _contentScrollViewer.UpdateLayout();
                    _contentScrollViewer.ChangeView(null, 0, null, disableAnimation: false);
                }
                return;
            }

            var allButtons = FindAllGameButtonsInVisualTree(GamesItemsControl);
            if (allButtons.Count == 0) return;

            var focusedButton = FindFocusedButton(allButtons);

            // If no button is focused, focus the first button
            if (focusedButton == null)
            {
                allButtons[0].Focus(FocusState.Programmatic);
                EnsureButtonVisible(allButtons[0]);
                SaveCurrentlyFocusedGame(allButtons[0]);
                return;
            }

            int currentIndex = allButtons.IndexOf(focusedButton);
            // In a 2-column grid, move up 2 positions
            int targetIndex = currentIndex - 2;
            if (targetIndex >= 0)
            {
                allButtons[targetIndex].Focus(FocusState.Programmatic);
                EnsureButtonVisible(allButtons[targetIndex]);
                SaveCurrentlyFocusedGame(allButtons[targetIndex]);
            }
            else
            {
                // At first row of tiles - transition to buttons zone
                _currentZone = LibraryFocusZone.Buttons;
                // Map column position: left column (0) → Add Game, right column (1) → Rescan
                _buttonFocusIndex = currentIndex % 2;
                FocusLibraryButton(_buttonFocusIndex);

                // Scroll to top to show buttons
                if (_contentScrollViewer != null)
                {
                    _contentScrollViewer.UpdateLayout();
                    _contentScrollViewer.ChangeView(null, 0, null, disableAnimation: false);
                }
            }
        }

        private void NavigateDown()
        {
            // Track gamepad navigation input
            _lastUsedGamepadInput = true;

            // If currently in buttons zone, transition to tiles
            if (_currentZone == LibraryFocusZone.Buttons)
            {
                _currentZone = LibraryFocusZone.Tiles;
                var allTileButtons = FindAllGameButtonsInVisualTree(GamesItemsControl);
                if (allTileButtons.Count > 0)
                {
                    // Focus tile in same column position as button
                    int tileIndex = Math.Min(_buttonFocusIndex, allTileButtons.Count - 1);
                    allTileButtons[tileIndex].Focus(FocusState.Programmatic);
                    EnsureButtonVisible(allTileButtons[tileIndex]);
                    SaveCurrentlyFocusedGame(allTileButtons[tileIndex]);
                }
                return;
            }

            var gameTiles = FindAllGameButtonsInVisualTree(GamesItemsControl);
            if (gameTiles.Count == 0) return;

            var focusedButton = FindFocusedButton(gameTiles);

            // If no button is focused, focus the first button
            if (focusedButton == null)
            {
                gameTiles[0].Focus(FocusState.Programmatic);
                EnsureButtonVisible(gameTiles[0]);
                SaveCurrentlyFocusedGame(gameTiles[0]);
                return;
            }

            int currentIndex = gameTiles.IndexOf(focusedButton);
            // In a 2-column grid, move down 2 positions
            int targetIndex = currentIndex + 2;
            if (targetIndex < gameTiles.Count)
            {
                gameTiles[targetIndex].Focus(FocusState.Programmatic);
                EnsureButtonVisible(gameTiles[targetIndex]);
                SaveCurrentlyFocusedGame(gameTiles[targetIndex]);
            }
            else if (_contentScrollViewer != null)
            {
                // Already at bottom row - scroll to absolute bottom to show game labels
                _contentScrollViewer.UpdateLayout();
                double maxScroll = Math.Max(0, _contentScrollViewer.ExtentHeight - _contentScrollViewer.ViewportHeight);

                // Always try to scroll to bottom (ChangeView will clamp to valid range)
                _contentScrollViewer.ChangeView(null, maxScroll, null, disableAnimation: false);
            }
        }

        private void NavigateLeft()
        {
            // Track gamepad navigation input
            _lastUsedGamepadInput = true;

            // If in buttons zone, move between Add Game, Rescan, and Random
            if (_currentZone == LibraryFocusZone.Buttons)
            {
                if (_buttonFocusIndex > 0)
                {
                    FocusLibraryButton(_buttonFocusIndex - 1);
                }
                return;
            }

            var allButtons = FindAllGameButtonsInVisualTree(GamesItemsControl);
            if (allButtons.Count == 0) return;

            var focusedButton = FindFocusedButton(allButtons);

            // If no button is focused, focus the first button
            if (focusedButton == null)
            {
                allButtons[0].Focus(FocusState.Programmatic);
                EnsureButtonVisible(allButtons[0]);
                SaveCurrentlyFocusedGame(allButtons[0]);
                return;
            }

            int currentIndex = allButtons.IndexOf(focusedButton);

            // Left: move left in same row, or wrap to right column of previous row
            if (currentIndex % 2 != 0) // If in second column (right)
            {
                // Move left to first column (same row)
                allButtons[currentIndex - 1].Focus(FocusState.Programmatic);
                EnsureButtonVisible(allButtons[currentIndex - 1]);
                SaveCurrentlyFocusedGame(allButtons[currentIndex - 1]);
            }
            else // If in first column (left)
            {
                // Wrap to second column of previous row
                int targetIndex = currentIndex - 1;
                if (targetIndex >= 0)
                {
                    allButtons[targetIndex].Focus(FocusState.Programmatic);
                    EnsureButtonVisible(allButtons[targetIndex]);
                    SaveCurrentlyFocusedGame(allButtons[targetIndex]);
                }
            }
        }

        private void NavigateRight()
        {
            // Track gamepad navigation input
            _lastUsedGamepadInput = true;

            // If in buttons zone, move between Add Game, Rescan, and Random
            if (_currentZone == LibraryFocusZone.Buttons)
            {
                if (_buttonFocusIndex < LIBRARY_BUTTON_COUNT - 1)
                {
                    FocusLibraryButton(_buttonFocusIndex + 1);
                }
                return;
            }

            var allButtons = FindAllGameButtonsInVisualTree(GamesItemsControl);
            if (allButtons.Count == 0) return;

            var focusedButton = FindFocusedButton(allButtons);

            // If no button is focused, focus the first button
            if (focusedButton == null)
            {
                allButtons[0].Focus(FocusState.Programmatic);
                EnsureButtonVisible(allButtons[0]);
                SaveCurrentlyFocusedGame(allButtons[0]);
                return;
            }

            int currentIndex = allButtons.IndexOf(focusedButton);

            // Right: move right in same row, or wrap to left column of next row
            if (currentIndex % 2 == 0) // If in first column (left)
            {
                // Move right to second column if it exists (same row)
                if (currentIndex + 1 < allButtons.Count)
                {
                    allButtons[currentIndex + 1].Focus(FocusState.Programmatic);
                    EnsureButtonVisible(allButtons[currentIndex + 1]);
                    SaveCurrentlyFocusedGame(allButtons[currentIndex + 1]);
                }
            }
            else // If in second column (right)
            {
                // Wrap to first column of next row
                int targetIndex = currentIndex + 1;
                if (targetIndex < allButtons.Count)
                {
                    allButtons[targetIndex].Focus(FocusState.Programmatic);
                    EnsureButtonVisible(allButtons[targetIndex]);
                    SaveCurrentlyFocusedGame(allButtons[targetIndex]);
                }
            }
        }

        private Button? FindFocusedButton(List<Button> buttons)
        {
            var focusedElement = FocusManager.GetFocusedElement(this.XamlRoot);
            foreach (var button in buttons)
            {
                if (button == focusedElement)
                    return button;
            }
            return null;
        }

        private void FocusLibraryButton(int index)
        {
            _buttonFocusIndex = index;
            switch (index)
            {
                case 0:
                    AddGameButton?.Focus(FocusState.Programmatic);
                    break;
                case 1:
                    RescanButton?.Focus(FocusState.Programmatic);
                    break;
                case 2:
                    RandomButton?.Focus(FocusState.Programmatic);
                    break;
            }
        }

        private void InvokeFocusedButton()
        {
            // If in buttons zone, invoke the focused button (Add Game, Rescan, or Random)
            if (_currentZone == LibraryFocusZone.Buttons)
            {
                switch (_buttonFocusIndex)
                {
                    case 0:
                        AddGameButton_Click(AddGameButton, new RoutedEventArgs());
                        break;
                    case 1:
                        RescanButton_Click(RescanButton, new RoutedEventArgs());
                        break;
                    case 2:
                        RandomButton_Click(RandomButton, new RoutedEventArgs());
                        break;
                }
                return;
            }

            // Otherwise, invoke the focused game tile
            var allButtons = FindAllGameButtonsInVisualTree(GamesItemsControl);
            var focusedButton = FindFocusedButton(allButtons);
            if (focusedButton != null && focusedButton.Tag is DetectedGame)
            {
                GameTile_Click(focusedButton, new RoutedEventArgs());
            }
        }

        private void OpenGameSettingsForFocusedTile()
        {
            var allButtons = FindAllGameButtonsInVisualTree(GamesItemsControl);
            var focusedButton = FindFocusedButton(allButtons);
            if (focusedButton != null && focusedButton.Tag is DetectedGame game)
            {
                // Store the game ProcessName and navigate to settings
                SelectedGameProcessName = game.ProcessName;

                // Navigate to GameSettingsPage
                var app = Application.Current as App;
                var mainWindow = app?.MainWindow;
                var navigationService = mainWindow?.NavigationService;
                navigationService?.NavigateToGameSettings();
            }
        }

        private void ScrollWithAnalogStick(double yValue)
        {
            if (_contentScrollViewer == null) return;

            // Invert Y axis (positive thumbstick = scroll up)
            double scrollAmount = -yValue * 15; // Increased for more responsive scrolling
            double currentOffset = _contentScrollViewer.VerticalOffset;
            double newOffset = currentOffset + scrollAmount;

            _contentScrollViewer.ChangeView(null, newOffset, null, disableAnimation: true);
        }

        private void EnsureButtonVisible(Button button)
        {
            // Use WinUI's built-in bring into view - same approach as GamepadNavigationService
            try
            {
                button.StartBringIntoView();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LibraryPage: Error ensuring button visible: {ex.Message}");
            }
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #region Game Management (Add Game & Rescan)

        private async void AddGameButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Use Windows Forms OpenFileDialog because WinUI FileOpenPicker doesn't work in Admin mode
                string? selectedPath = null;
                string? suggestedName = null;

                try
                {
                    using var openFileDialog = new System.Windows.Forms.OpenFileDialog
                    {
                        Title = "Select Game Executable",
                        Filter = "Executable Files (*.exe)|*.exe",
                        FilterIndex = 1,
                        InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
                    };

                    var dialogResult = openFileDialog.ShowDialog();
                    if (dialogResult != System.Windows.Forms.DialogResult.OK)
                    {
                        // User canceled file picker
                        return;
                    }

                    selectedPath = openFileDialog.FileName;
                    suggestedName = System.IO.Path.GetFileNameWithoutExtension(openFileDialog.FileName);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error opening file picker: {ex.Message}");
                    await ShowErrorDialog($"Failed to open file picker: {ex.Message}");
                    return;
                }

                if (!string.IsNullOrEmpty(selectedPath) && !string.IsNullOrEmpty(suggestedName))
                {
                    // Prompt for game name
                    var gameName = await ShowGameNameDialog(suggestedName);

                    if (!string.IsNullOrEmpty(gameName))
                    {
                        await AddManualGameToDatabase(gameName, selectedPath);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in AddGameButton_Click: {ex.Message}");
                await ShowErrorDialog($"Failed to add game: {ex.Message}");
            }
        }

        private async Task<string?> ShowGameNameDialog(string suggestedName)
        {
            var dialog = new ContentDialog
            {
                Title = "Enter Game Name",
                PrimaryButtonText = "Add",
                CloseButtonText = "Cancel",
                XamlRoot = this.XamlRoot
            };

            var textBox = new TextBox
            {
                Text = suggestedName,
                PlaceholderText = "Game name",
                Width = 300
            };

            dialog.Content = textBox;

            // Ensure gamepad navigation works in dialog
            dialog.Loaded += (s, e) =>
            {
                textBox.Focus(FocusState.Programmatic);
            };

            var result = await dialog.ShowAsync();

            return result == ContentDialogResult.Primary ? textBox.Text : null;
        }

        private async Task AddManualGameToDatabase(string gameName, string exePath)
        {
            try
            {
                if (_gameDetectionService == null) return;

                // Create a DetectedGame object for the manual game
                var manualGame = new DetectedGame
                {
                    ProcessName = System.IO.Path.GetFileNameWithoutExtension(exePath),
                    DisplayName = gameName,
                    ExecutablePath = exePath,
                    InstallLocation = System.IO.Path.GetDirectoryName(exePath) ?? string.Empty,
                    Source = GameSource.Manual,
                    FirstDetected = DateTime.Now,
                    LastDetected = DateTime.Now,
                    ArtworkPath = string.Empty,
                    LauncherInfo = string.Empty,
                    PackageInfo = string.Empty
                };

                // Access the game database through reflection
                var databaseField = typeof(EnhancedGameDetectionService).GetField("_gameDatabase",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (databaseField?.GetValue(_gameDetectionService) is EnhancedGameDatabase database)
                {
                    // Try to fetch artwork from SteamGridDB before saving
                    var artworkServiceField = typeof(EnhancedGameDetectionService).GetField("_artworkService",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    if (artworkServiceField?.GetValue(_gameDetectionService) is SteamGridDbArtworkService artworkService)
                    {
                        System.Diagnostics.Debug.WriteLine($"Fetching artwork for manual game: {gameName}");
                        var artworkPath = await artworkService.DownloadArtworkAsync(manualGame);

                        if (!string.IsNullOrEmpty(artworkPath))
                        {
                            manualGame.ArtworkPath = artworkPath;
                            System.Diagnostics.Debug.WriteLine($"Artwork downloaded: {artworkPath}");
                        }
                        else
                        {
                            // Copy fallback image to artwork directory
                            System.Diagnostics.Debug.WriteLine($"No artwork found for {gameName}, using fallback image");
                            var appDataPath = Path.Combine(
                                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                "HUDRA");
                            var artworkDirectory = Path.Combine(appDataPath, "artwork");

                            if (!Directory.Exists(artworkDirectory))
                            {
                                Directory.CreateDirectory(artworkDirectory);
                            }

                            var fallbackSourcePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "no-artwork-grid.png");
                            var fallbackDestPath = Path.Combine(artworkDirectory, $"{manualGame.ProcessName}_fallback.png");

                            if (File.Exists(fallbackSourcePath))
                            {
                                File.Copy(fallbackSourcePath, fallbackDestPath, overwrite: true);
                                manualGame.ArtworkPath = fallbackDestPath;
                                System.Diagnostics.Debug.WriteLine($"Fallback image copied to: {fallbackDestPath}");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"Warning: Fallback image not found at {fallbackSourcePath}");
                            }
                        }
                    }

                    // Save the game with artwork path (or empty if not found)
                    database.SaveGame(manualGame);

                    // Update the cached games dictionary
                    var cachedGamesField = typeof(EnhancedGameDetectionService).GetField("_cachedGames",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    if (cachedGamesField?.GetValue(_gameDetectionService) is Dictionary<string, DetectedGame> cachedGames)
                    {
                        cachedGames[manualGame.ProcessName] = manualGame;
                    }

                    System.Diagnostics.Debug.WriteLine($"Manual game added: {gameName} ({exePath})");

                    // Immediately reload the library to show the new game with artwork
                    _gamesLoaded = false;
                    await LoadGamesAsync();

                    // Show success message
                    await ShowSuccessDialog($"Game '{gameName}' has been added successfully!");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding manual game to database: {ex.Message}");
                await ShowErrorDialog($"Failed to add game: {ex.Message}");
            }
        }

        private async void RescanButton_Click(object sender, RoutedEventArgs e)
        {
            if (_gameDetectionService == null) return;

            // Check if Library Scanning is enabled
            if (!SettingsService.IsEnhancedLibraryScanningEnabled())
            {
                System.Diagnostics.Debug.WriteLine("LibraryPage: Rescan blocked - Library Scanning is disabled");
                await ShowErrorDialog("Library Scanning is disabled. Enable it in Settings to rescan your game library.");
                return;
            }

            try
            {
                ShowScanProgress("Scanning game libraries...");

                // Trigger a full library rescan with cache clearing to detect newly installed games
                await _gameDetectionService.RescanLibraryAsync();

                // Reload the library to show any new games
                _gamesLoaded = false;
                await LoadGamesAsync();

                HideScanProgress();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in RescanButton_Click: {ex.Message}");
                HideScanProgress();
                await ShowErrorDialog($"Scan failed: {ex.Message}");
            }
        }

        private async Task ShowSuccessDialog(string message)
        {
            var dialog = new ContentDialog
            {
                Title = "Success",
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };

            await dialog.ShowAsync();
        }

        private async Task ShowErrorDialog(string message)
        {
            var dialog = new ContentDialog
            {
                Title = "Error",
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };

            await dialog.ShowAsync();
        }

        #endregion

        #region UI State Updates

        /// <summary>
        /// Updates the Rescan button enabled state based on Library Scanning setting.
        /// When Library Scanning is disabled, the Rescan button is disabled and text is greyed out.
        /// </summary>
        private void UpdateRescanButtonState()
        {
            var isLibraryScanningEnabled = SettingsService.IsEnhancedLibraryScanningEnabled();

            DispatcherQueue.TryEnqueue(() =>
            {
                RescanButton.IsEnabled = isLibraryScanningEnabled;

                // Update text opacity to visually indicate disabled state
                RescanButton.Opacity = isLibraryScanningEnabled ? 1.0 : 0.4;

                // Update tooltip to explain why it's disabled
                if (!isLibraryScanningEnabled)
                {
                    ToolTipService.SetToolTip(RescanButton, "Enable Library Scanning in Settings to use this feature");
                }
                else
                {
                    ToolTipService.SetToolTip(RescanButton, null);
                }
            });

            System.Diagnostics.Debug.WriteLine($"LibraryPage: Rescan button enabled = {isLibraryScanningEnabled}");
        }

        #endregion

        #region SteamGridDB Hint

        /// <summary>
        /// Checks if the SGDB hint bar should be shown.
        /// Hides if user dismissed it OR if API key is already configured.
        /// </summary>
        private async Task CheckSgdbHintVisibilityAsync()
        {
            try
            {
                var dismissed = SettingsService.GetSgdbHintDismissed();
                var hasKey = await _secureStorage.HasApiKeyAsync();

                System.Diagnostics.Debug.WriteLine($"LibraryPage: SGDB hint check - dismissed={dismissed}, hasKey={hasKey}, shouldShow={!dismissed && !hasKey}");

                DispatcherQueue.TryEnqueue(() =>
                {
                    SgdbHintBar.IsOpen = !dismissed && !hasKey;
                    System.Diagnostics.Debug.WriteLine($"LibraryPage: SGDB hint IsOpen set to {SgdbHintBar.IsOpen}");
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LibraryPage: Error checking SGDB hint visibility: {ex.Message}");
            }
        }

        private void SgdbHintBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
        {
            // Persist dismissal state
            SettingsService.SetSgdbHintDismissed(true);
            System.Diagnostics.Debug.WriteLine("LibraryPage: SGDB hint bar dismissed");
        }

        private void SgdbHintOpenSettings_Click(object sender, RoutedEventArgs e)
        {
            // Navigate to Settings page
            var app = Application.Current as App;
            var mainWindow = app?.MainWindow;
            var navigationService = mainWindow?.NavigationService;
            navigationService?.NavigateToSettings();
        }

        #endregion

        #region Random Game Roulette

        private async void RandomButton_Click(object sender, RoutedEventArgs e)
        {
            // Check if we have games to choose from
            if (_games == null || _games.Count == 0)
            {
                await ShowErrorDialog("No games in library. Add games first!");
                return;
            }

            // Don't start another roulette if one is already active
            if (_isRouletteActive)
            {
                return;
            }

            // Start the roulette
            await StartRouletteAsync();
        }

        private void RouletteCancelButton_Click(object sender, RoutedEventArgs e)
        {
            CancelRoulette();
        }

        private async Task StartRouletteAsync()
        {
            _isRouletteActive = true;
            _isRouletteCancelled = false;
            _rouletteCts = new CancellationTokenSource();

            DetectedGame? selectedGame = null;

            try
            {
                // Get list of games first
                var gamesList = _games.ToList();
                if (gamesList.Count == 0)
                {
                    _isRouletteActive = false;
                    return;
                }

                // Initialize sound players
                var tickSoundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "random-tick.mp3");
                var winnerSoundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "random-winner.mp3");

                if (File.Exists(tickSoundPath))
                {
                    _rouletteTickPlayer = new MediaPlayer();
                    _rouletteTickPlayer.Source = MediaSource.CreateFromUri(new Uri(tickSoundPath));
                    _rouletteTickPlayer.Volume = 0.5;
                }

                if (File.Exists(winnerSoundPath))
                {
                    _rouletteWinnerPlayer = new MediaPlayer();
                    _rouletteWinnerPlayer.Source = MediaSource.CreateFromUri(new Uri(winnerSoundPath));
                    _rouletteWinnerPlayer.Volume = 0.7;
                }

                // Select a random target game index (avoid repeating the same game)
                var random = new Random();
                int targetIndex;
                if (gamesList.Count > 1 && _lastRouletteIndex >= 0 && _lastRouletteIndex < gamesList.Count)
                {
                    // Pick from all indices except the last one
                    targetIndex = random.Next(gamesList.Count - 1);
                    if (targetIndex >= _lastRouletteIndex)
                    {
                        targetIndex++; // Skip over the last selected index
                    }
                }
                else
                {
                    targetIndex = random.Next(gamesList.Count);
                }
                _lastRouletteIndex = targetIndex;
                selectedGame = gamesList[targetIndex];

                // Show the modal immediately with first game
                var firstGame = gamesList[0];
                DispatcherQueue.TryEnqueue(() =>
                {
                    RouletteOverlay.Visibility = Visibility.Visible;
                    RouletteCountdownOverlay.Visibility = Visibility.Collapsed;
                    RouletteGameNameText.Text = firstGame.DisplayName;

                    var artworkPath = firstGame.ArtworkPath;
                    if (!string.IsNullOrEmpty(artworkPath))
                    {
                        var queryIndex = artworkPath.IndexOf('?');
                        if (queryIndex > 0)
                        {
                            artworkPath = artworkPath.Substring(0, queryIndex);
                        }
                        if (File.Exists(artworkPath))
                        {
                            RouletteGameImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(artworkPath));
                        }
                    }
                });

                // Wait for audio to be ready (first tick plays immediately after this)
                await Task.Delay(150);

                // Roulette animation - cycle through games in the modal with deceleration
                const int minIntervalMs = 80;   // Fast speed at start
                const int maxIntervalMs = 400;  // Slow speed at end
                int durationMs = 5000 + random.Next(10000); // 5-15 seconds
                var startTime = DateTime.Now;
                int currentPosition = 1; // Start at 1 since we already showed first game

                while ((DateTime.Now - startTime).TotalMilliseconds < durationMs && !_isRouletteCancelled)
                {
                    // Calculate progress (0 to 1)
                    double progress = (DateTime.Now - startTime).TotalMilliseconds / durationMs;

                    // Ease-out cubic for natural deceleration: progress^2
                    double easeProgress = progress * progress;

                    // Calculate interval based on progress (faster at start, slower at end)
                    int intervalMs = (int)(minIntervalMs + (maxIntervalMs - minIntervalMs) * easeProgress);

                    int gameIndex = currentPosition % gamesList.Count;
                    var currentGame = gamesList[gameIndex];

                    // Play tick sound (reset position to replay)
                    if (_rouletteTickPlayer != null)
                    {
                        _rouletteTickPlayer.PlaybackSession.Position = TimeSpan.Zero;
                        _rouletteTickPlayer.Play();
                    }

                    // Update the modal display
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        RouletteGameNameText.Text = currentGame.DisplayName;

                        // Update artwork - strip query params if present
                        var artworkPath = currentGame.ArtworkPath;
                        if (!string.IsNullOrEmpty(artworkPath))
                        {
                            var queryIndex = artworkPath.IndexOf('?');
                            if (queryIndex > 0)
                            {
                                artworkPath = artworkPath.Substring(0, queryIndex);
                            }

                            if (File.Exists(artworkPath))
                            {
                                RouletteGameImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(artworkPath));
                            }
                            else
                            {
                                RouletteGameImage.Source = null;
                            }
                        }
                        else
                        {
                            RouletteGameImage.Source = null;
                        }
                    });

                    try
                    {
                        await Task.Delay(intervalMs, _rouletteCts.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }

                    currentPosition++;
                }

                // Check if cancelled
                if (_isRouletteCancelled)
                {
                    System.Diagnostics.Debug.WriteLine("LibraryPage: Roulette cancelled by user");
                    return;
                }

                // Play winner sound
                _rouletteWinnerPlayer?.Play();

                // Show the final selected game
                DispatcherQueue.TryEnqueue(() =>
                {
                    RouletteGameNameText.Text = selectedGame.DisplayName;

                    var artworkPath = selectedGame.ArtworkPath;
                    if (!string.IsNullOrEmpty(artworkPath))
                    {
                        var queryIndex = artworkPath.IndexOf('?');
                        if (queryIndex > 0)
                        {
                            artworkPath = artworkPath.Substring(0, queryIndex);
                        }

                        if (File.Exists(artworkPath))
                        {
                            RouletteGameImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(artworkPath));
                        }
                    }
                });

                // Start countdown
                await StartRouletteCountdownAsync(selectedGame);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LibraryPage: Roulette error: {ex.Message}");
            }
            finally
            {
                _isRouletteActive = false;
                _rouletteCts?.Dispose();
                _rouletteCts = null;

                // Dispose sound players
                _rouletteTickPlayer?.Dispose();
                _rouletteTickPlayer = null;
                _rouletteWinnerPlayer?.Dispose();
                _rouletteWinnerPlayer = null;

                // Hide modal
                DispatcherQueue.TryEnqueue(() =>
                {
                    RouletteOverlay.Visibility = Visibility.Collapsed;
                    RouletteCountdownOverlay.Visibility = Visibility.Collapsed;
                });
            }
        }

        private async Task StartRouletteCountdownAsync(DetectedGame game)
        {
            // Show countdown overlay on top of the game tile
            DispatcherQueue.TryEnqueue(() =>
            {
                RouletteCountdownOverlay.Visibility = Visibility.Visible;
            });

            // Countdown from 3
            for (int count = 3; count >= 1; count--)
            {
                if (_isRouletteCancelled)
                {
                    return;
                }

                DispatcherQueue.TryEnqueue(() =>
                {
                    RouletteCountdownText.Text = count.ToString();
                });

                try
                {
                    await Task.Delay(1000, _rouletteCts?.Token ?? CancellationToken.None);
                }
                catch (TaskCanceledException)
                {
                    return;
                }
            }

            // Check one more time for cancellation
            if (_isRouletteCancelled)
            {
                return;
            }

            // Hide modal before launching
            DispatcherQueue.TryEnqueue(() =>
            {
                RouletteOverlay.Visibility = Visibility.Collapsed;
            });

            // Launch the game
            try
            {
                // Apply per-game profile IMMEDIATELY before launching
                var app = Application.Current as App;
                var mainWindow = app?.MainWindow;
                if (mainWindow != null)
                {
                    var profileApplied = await mainWindow.ApplyGameProfileAsync(game.ProcessName, game.DisplayName);
                    if (profileApplied)
                    {
                        System.Diagnostics.Debug.WriteLine($"LibraryPage: Profile applied for {game.DisplayName} before roulette launch");
                    }
                }

                // Launch the game
                bool success = _gameLauncherService?.LaunchGame(game) ?? false;

                if (!success)
                {
                    System.Diagnostics.Debug.WriteLine($"LibraryPage: Failed to launch {game.DisplayName} via roulette");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LibraryPage: Error launching game via roulette: {ex.Message}");
            }
        }

        private void CancelRoulette()
        {
            if (_isRouletteActive && !_isRouletteCancelled)
            {
                _isRouletteCancelled = true;
                _rouletteCts?.Cancel();
                System.Diagnostics.Debug.WriteLine("LibraryPage: Roulette cancellation requested");
            }
        }

        #endregion
    }

    // Value converters for XAML bindings
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return !string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }

    public class InvertedNullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
