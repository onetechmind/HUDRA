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
using System.Linq;
using System.Threading.Tasks;
using Windows.Gaming.Input;

namespace HUDRA.Pages
{
    public sealed partial class LibraryPage : Page, INotifyPropertyChanged
    {
        private EnhancedGameDetectionService? _gameDetectionService;
        private GameLauncherService? _gameLauncherService;
        private GamepadNavigationService? _gamepadNavigationService;
        private ScrollViewer? _contentScrollViewer; // MainWindow's ContentScrollViewer
        private ObservableCollection<DetectedGame> _games = new ObservableCollection<DetectedGame>();

        // State preservation - STATIC fields persist across page recreation
        // Despite NavigationCacheMode.Enabled, page is still being recreated (framework issue)
        private static double _savedScrollOffset = 0;
        private static string? _savedFocusedGameProcessName = null;
        private static bool _lastUsedGamepadInput = false; // Track input method for smart focus restoration
        private bool _gamesLoaded = false;
        private bool _eventsSubscribed = false; // Track if we've subscribed to prevent duplicates
        private bool _isRestoringScroll = false; // Flag to ignore ViewChanged during programmatic scroll restoration

        // Gamepad navigation
        private readonly HashSet<GamepadButtons> _pressedButtons = new();
        private DateTime _lastInputTime = DateTime.MinValue;
        private const double INPUT_REPEAT_DELAY_MS = 150;

        public event PropertyChangedEventHandler? PropertyChanged;

        public LibraryPage()
        {
            this.InitializeComponent();

            System.Diagnostics.Debug.WriteLine($"📜 LibraryPage CONSTRUCTOR - page created (caching enabled, should only happen once)");

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
                _savedFocusedGameProcessName = null;
                System.Diagnostics.Debug.WriteLine($"🖱️ Mouse click detected - switched to mouse input, cleared saved focus");
            }
        }

        private void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
        {
            // Keyboard input detected - switch to keyboard input mode
            // Ignore gamepad buttons (they're handled separately)
            if (_lastUsedGamepadInput && !IsGamepadKey(e.Key))
            {
                _lastUsedGamepadInput = false;
                _savedFocusedGameProcessName = null;
                System.Diagnostics.Debug.WriteLine($"⌨️ Keyboard input detected - switched to keyboard input, cleared saved focus");
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
                System.Diagnostics.Debug.WriteLine($"📜 ViewChanged: scroll now at {_savedScrollOffset:F1}");

                // ONLY track input method if this is a user-initiated scroll, NOT programmatic restoration
                // This prevents RestoreScrollPositionAsync from incorrectly resetting the gamepad flag
                if (!_isRestoringScroll && !e.IsIntermediate)
                {
                    _lastUsedGamepadInput = false;
                    // Clear saved focus when switching to mouse/keyboard - no tile is selected
                    _savedFocusedGameProcessName = null;
                    System.Diagnostics.Debug.WriteLine($"📜   Input method: Mouse/Touch scroll (user-initiated) - cleared saved focus");
                }
            }
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            System.Diagnostics.Debug.WriteLine($"LibraryPage: OnNavigatedTo - _savedScrollOffset={_savedScrollOffset}, _savedFocusedGameProcessName={_savedFocusedGameProcessName}");

            // Only load games if not already loaded (to preserve scroll position)
            if (!_gamesLoaded)
            {
                await LoadGamesAsync();
            }

            // Restore focused game first (if any) - this may scroll
            await RestoreFocusedGameAsync();

            // THEN restore scroll position to override any focus-induced scrolling
            // Wait a bit for focus to settle before restoring scroll
            await Task.Delay(200);
            await RestoreScrollPositionAsync();

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

            // Note: We keep scan event subscriptions active for reactive updates
        }

        /// <summary>
        /// Saves scroll position and focused game when navigating away.
        /// IMMEDIATELY unsubscribes from ViewChanged to prevent other pages from overwriting saved scroll.
        /// </summary>
        public void SaveScrollPosition()
        {
            System.Diagnostics.Debug.WriteLine($"📜 SaveScrollPosition CALLED (leaving Library page)");
            System.Diagnostics.Debug.WriteLine($"📜   Scroll position in static field: {_savedScrollOffset}");
            System.Diagnostics.Debug.WriteLine($"📜   Saved focused game in static field: {_savedFocusedGameProcessName ?? "(none)"}");

            if (_contentScrollViewer != null)
            {
                System.Diagnostics.Debug.WriteLine($"📜   Current ContentScrollViewer offset: {_contentScrollViewer.VerticalOffset}");

                // CRITICAL: Unsubscribe IMMEDIATELY to prevent other pages from overwriting _savedScrollOffset
                // This must happen before the next page loads and scrolls ContentScrollViewer to 0
                _contentScrollViewer.ViewChanged -= OnScrollViewChanged;
                System.Diagnostics.Debug.WriteLine($"📜   ✅ UNSUBSCRIBED from ViewChanged - scroll locked at {_savedScrollOffset}");
            }

            // NOTE: Focused game is now tracked continuously via SaveCurrentlyFocusedGame()
            // during D-pad navigation, so we don't need to check FocusManager here.
            // The _savedFocusedGameProcessName static field already contains the last focused game.
            System.Diagnostics.Debug.WriteLine($"📜   Final saved focused game: {_savedFocusedGameProcessName ?? "(none)"}");
        }

        public async void Initialize(EnhancedGameDetectionService gameDetectionService, GamepadNavigationService gamepadNavigationService, ScrollViewer contentScrollViewer, bool isGamepadNavigation = false)
        {
            System.Diagnostics.Debug.WriteLine($"LibraryPage: Initialize called - _savedScrollOffset={_savedScrollOffset}, _savedFocusedGameProcessName={_savedFocusedGameProcessName}, isGamepadNavigation={isGamepadNavigation}");

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

            // Subscribe to gamepad input on each navigation (unsubscribed when leaving page)
            _gamepadNavigationService.RawGamepadInput += OnRawGamepadInput;

            // Subscribe to ContentScrollViewer on each navigation (unsubscribed when leaving)
            // This is necessary because we need to stop tracking when other pages use the same ScrollViewer
            _contentScrollViewer.ViewChanged += OnScrollViewChanged;
            System.Diagnostics.Debug.WriteLine($"📜 Subscribed to ContentScrollViewer.ViewChanged");
            System.Diagnostics.Debug.WriteLine($"📜   ContentScrollViewer ExtentHeight: {_contentScrollViewer.ExtentHeight}");
            System.Diagnostics.Debug.WriteLine($"📜   ContentScrollViewer ViewportHeight: {_contentScrollViewer.ViewportHeight}");

            // Subscribe to scan events only once (for reactive game list updates)
            if (!_eventsSubscribed)
            {
                System.Diagnostics.Debug.WriteLine("LibraryPage: Subscribing to scan events (first time only)");

                _gameDetectionService.ScanningStateChanged += OnScanningStateChanged;
                _gameDetectionService.ScanProgressChanged += OnScanProgressChanged;

                _eventsSubscribed = true;
            }

            // Load games only if not already loaded
            if (!_gamesLoaded)
            {
                await LoadGamesAsync();
            }

            // Focus logic per UX requirements:
            // 1. If saved focus exists: restore it (gamepad navigation only)
            // 2. Else if navigated via gamepad: focus first tile
            // 3. Else (mouse/keyboard): no auto-focus
            if (isGamepadNavigation)
            {
                _lastUsedGamepadInput = true; // Update input method tracking for future navigations

                if (!string.IsNullOrEmpty(_savedFocusedGameProcessName))
                {
                    // Restore previously focused game
                    System.Diagnostics.Debug.WriteLine($"🎮 Gamepad nav + saved focus: Restoring focus to {_savedFocusedGameProcessName}");
                    await RestoreFocusedGameAsync();
                }
                else
                {
                    // Auto-select first tile
                    System.Diagnostics.Debug.WriteLine($"🎮 Gamepad nav + no saved focus: Focusing first tile");
                    await FocusFirstGameTileAsync();
                }
            }
            else
            {
                _lastUsedGamepadInput = false; // Update input method tracking
                System.Diagnostics.Debug.WriteLine($"🖱️ Mouse/Keyboard nav: No auto-focus");
                // No auto-focus for mouse/keyboard navigation
            }

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

                System.Diagnostics.Debug.WriteLine($"📚 LoadGamesAsync: Retrieved {gamesList.Count} games from database");

                if (!gamesList.Any())
                {
                    ShowEmptyState();
                    return;
                }

                // Sort alphabetically by display name (null-safe)
                gamesList = gamesList.OrderBy(g => g?.DisplayName ?? "").ToList();

                // Update the ObservableCollection
                _games.Clear();
                int nullCount = 0;
                int addedCount = 0;
                foreach (var game in gamesList)
                {
                    if (game == null)
                    {
                        nullCount++;
                        System.Diagnostics.Debug.WriteLine($"⚠️ LoadGamesAsync: Found NULL game object in database results - SKIPPING!");
                        continue; // Skip null entries - don't add them to the collection
                    }

                    string artworkInfo = string.IsNullOrEmpty(game.ArtworkPath) ? "NO artwork" : "has artwork";
                    System.Diagnostics.Debug.WriteLine($"📚 Adding game: {game.DisplayName} ({game.ProcessName}) - {artworkInfo}");
                    _games.Add(game);
                    addedCount++;
                }

                if (nullCount > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ LoadGamesAsync: WARNING - Skipped {nullCount} null game objects from database!");
                }

                // Set ItemsSource
                GamesItemsControl.ItemsSource = _games;

                // Hide empty state
                EmptyStatePanel.Visibility = Visibility.Collapsed;

                // Mark games as loaded
                _gamesLoaded = true;

                System.Diagnostics.Debug.WriteLine($"📚 LoadGamesAsync: Added {addedCount} valid games to ObservableCollection (skipped {nullCount} null entries)");
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

        private async void GameTile_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"🖱️ GameTile_Click called - sender type: {sender?.GetType().Name}");

            if (sender is not Button button)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ GameTile_Click: sender is not Button");
                return;
            }

            if (button.Tag is not DetectedGame game)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ GameTile_Click: Button.Tag is not DetectedGame, type: {button.Tag?.GetType().Name ?? "null"}");
                return;
            }

            string artworkInfo = string.IsNullOrEmpty(game.ArtworkPath) ? "NO artwork" : "has artwork";
            System.Diagnostics.Debug.WriteLine($"🖱️ GameTile_Click: Launching {game.DisplayName} ({artworkInfo})");

            Border? overlay = null;

            try
            {
                // Find the launching overlay in this button's visual tree
                overlay = FindTileLaunchingOverlay(button);
                if (overlay != null)
                {
                    overlay.Visibility = Visibility.Visible;
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
            System.Diagnostics.Debug.WriteLine($"📜 RestoreScrollPositionAsync - _savedScrollOffset={_savedScrollOffset}");

            if (_contentScrollViewer == null)
            {
                System.Diagnostics.Debug.WriteLine($"📜   ERROR: ContentScrollViewer is null");
                return;
            }

            if (_savedScrollOffset == 0)
            {
                System.Diagnostics.Debug.WriteLine($"📜   Skipping restore - offset is 0");
                return; // No need to restore if at top
            }

            System.Diagnostics.Debug.WriteLine($"📜   Will restore scroll to: {_savedScrollOffset}");

            // Wait for content to be fully loaded and measured
            // Retry a few times until the extent height is calculated
            for (int attempt = 0; attempt < 5; attempt++)
            {
                _contentScrollViewer.UpdateLayout();
                await Task.Delay(100);

                // Check if the ScrollViewer has measured its content
                if (_contentScrollViewer.ExtentHeight > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"📜   ScrollViewer ready on attempt {attempt + 1}");
                    System.Diagnostics.Debug.WriteLine($"📜     ExtentHeight: {_contentScrollViewer.ExtentHeight}");
                    System.Diagnostics.Debug.WriteLine($"📜     ViewportHeight: {_contentScrollViewer.ViewportHeight}");
                    System.Diagnostics.Debug.WriteLine($"📜     Current offset: {_contentScrollViewer.VerticalOffset}");
                    break;
                }
                System.Diagnostics.Debug.WriteLine($"📜   Waiting for layout... attempt {attempt + 1}");
            }

            // Set flag to prevent ViewChanged from resetting _lastUsedGamepadInput
            _isRestoringScroll = true;

            // Restore the scroll position
            bool success = _contentScrollViewer.ChangeView(null, _savedScrollOffset, null, disableAnimation: true);
            System.Diagnostics.Debug.WriteLine($"📜   ChangeView() returned: {success}, target: {_savedScrollOffset}");

            // Verify restoration worked
            await Task.Delay(100);
            System.Diagnostics.Debug.WriteLine($"📜   FINAL scroll position: {_contentScrollViewer.VerticalOffset}");

            // Clear the flag after restoration is complete
            _isRestoringScroll = false;
        }

        private async Task RestoreFocusedGameAsync()
        {
            if (!string.IsNullOrEmpty(_savedFocusedGameProcessName))
            {
                System.Diagnostics.Debug.WriteLine($"LibraryPage: RestoreFocusedGameAsync - Restoring focus to: {_savedFocusedGameProcessName}");

                // Wait for UI to be fully rendered
                await Task.Delay(100);

                // Null check for GamesItemsControl
                if (GamesItemsControl == null)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ RestoreFocusedGameAsync: GamesItemsControl is NULL!");
                    return;
                }

                // Find the game button that matches the saved process name
                var gameButton = FindGameButton(_savedFocusedGameProcessName);
                if (gameButton != null)
                {
                    System.Diagnostics.Debug.WriteLine($"LibraryPage: Found button, focusing it (this may scroll)");
                    gameButton.Focus(FocusState.Programmatic);
                    // Keep the saved focus valid (already saved, but this ensures it)
                    SaveCurrentlyFocusedGame(gameButton);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"LibraryPage: Could not find button for {_savedFocusedGameProcessName}");
                    // Game no longer exists, clear saved focus
                    _savedFocusedGameProcessName = null;
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"LibraryPage: No saved focus to restore");
            }
        }

        /// <summary>
        /// Focuses the first tile in the grid (top-left, index 0).
        /// Used when navigating via gamepad with no saved focus.
        /// </summary>
        private async Task FocusFirstGameTileAsync()
        {
            System.Diagnostics.Debug.WriteLine($"🎮 FocusFirstGameTile: Focusing first tile in list");

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
                System.Diagnostics.Debug.WriteLine($"🎮 FocusFirstGameTile: No buttons found");
                return;
            }

            // Focus the very first button (index 0)
            allButtons[0].Focus(FocusState.Programmatic);
            SaveCurrentlyFocusedGame(allButtons[0]);
            System.Diagnostics.Debug.WriteLine($"🎮 FocusFirstGameTile: Focused first tile (index 0)");
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
                string artworkStatus = string.IsNullOrEmpty(game.ArtworkPath) ? "NO artwork" : "has artwork";
                System.Diagnostics.Debug.WriteLine($"💾 Saved focused game: {game.DisplayName} ({game.ProcessName}) - {artworkStatus}");
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
                System.Diagnostics.Debug.WriteLine($"🖱️ FocusFirstVisibleTile: ContentScrollViewer is null");
                return;
            }

            // Wait for UI to be fully rendered
            await Task.Delay(100);

            // Get all game buttons
            var allButtons = FindAllGameButtonsInVisualTree(GamesItemsControl);
            if (allButtons.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"🖱️ FocusFirstVisibleTile: No buttons found");
                return;
            }

            // Get current scroll position
            double currentScrollOffset = _contentScrollViewer.VerticalOffset;
            double viewportHeight = _contentScrollViewer.ViewportHeight;

            System.Diagnostics.Debug.WriteLine($"🖱️ FocusFirstVisibleTile: Scroll={currentScrollOffset:F0}, Viewport={viewportHeight:F0}");

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
                        System.Diagnostics.Debug.WriteLine($"🖱️ FocusFirstVisibleTile: Found visible button at Y={buttonTop:F0}");
                        break; // Buttons are already sorted top-to-bottom, so first visible is top-left
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"🖱️ FocusFirstVisibleTile: Error checking button visibility: {ex.Message}");
                }
            }

            // Focus the first visible button
            if (firstVisibleButton != null)
            {
                firstVisibleButton.Focus(FocusState.Programmatic);
                System.Diagnostics.Debug.WriteLine($"🖱️ FocusFirstVisibleTile: Focused first visible tile");
            }
            else
            {
                // Fallback: focus the very first button if no visible button found
                allButtons[0].Focus(FocusState.Programmatic);
                System.Diagnostics.Debug.WriteLine($"🖱️ FocusFirstVisibleTile: No visible button found, focused first button as fallback");
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
                if (child is Button button)
                {
                    if (button.Tag is DetectedGame game)
                    {
                        buttons.Add(button);
                        // Log details about this button for debugging
                        string artworkInfo = string.IsNullOrEmpty(game.ArtworkPath)
                            ? $"NO artwork (ArtworkPath='{game.ArtworkPath ?? "null"}')"
                            : $"has artwork ({game.ArtworkPath})";
                        System.Diagnostics.Debug.WriteLine($"🔍 Found button: {game.DisplayName} - {artworkInfo}, IsEnabled={button.IsEnabled}, IsTabStop={button.IsTabStop}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️ Found button with Tag type: {button.Tag?.GetType().Name ?? "null"}");
                    }
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

            System.Diagnostics.Debug.WriteLine($"🔍 Total buttons found: {buttons.Count}");
            return buttons;
        }

        public async void FocusFirstGameButton()
        {
            try
            {
                // Don't override if we already have a saved focus position
                if (!string.IsNullOrEmpty(_savedFocusedGameProcessName))
                {
                    System.Diagnostics.Debug.WriteLine("LibraryPage: Skipping FocusFirstGameButton - saved focus exists");
                    return;
                }

                // Wait for buttons to be rendered and positioned
                await Task.Delay(300);

                var allButtons = FindAllGameButtonsInVisualTree(GamesItemsControl);

                if (allButtons.Count > 0)
                {
                    // Focus the first button WITHOUT scrolling to preserve scroll position
                    allButtons[0].Focus(FocusState.Programmatic);
                    System.Diagnostics.Debug.WriteLine("LibraryPage: Focused first game button");
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
            else if (_contentScrollViewer != null)
            {
                // Already at top row - scroll to absolute top to show header
                _contentScrollViewer.UpdateLayout();
                System.Diagnostics.Debug.WriteLine($"⬆️ EDGE SCROLL UP - At top row (index={currentIndex})");
                System.Diagnostics.Debug.WriteLine($"⬆️   Current scroll: {_contentScrollViewer.VerticalOffset}");
                System.Diagnostics.Debug.WriteLine($"⬆️   Scrolling to: 0");
                bool scrolled = _contentScrollViewer.ChangeView(null, 0, null, disableAnimation: false);
                System.Diagnostics.Debug.WriteLine($"⬆️   ChangeView returned: {scrolled}");
            }
        }

        private void NavigateDown()
        {
            // Track gamepad navigation input
            _lastUsedGamepadInput = true;

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
            // In a 2-column grid, move down 2 positions
            int targetIndex = currentIndex + 2;
            if (targetIndex < allButtons.Count)
            {
                allButtons[targetIndex].Focus(FocusState.Programmatic);
                EnsureButtonVisible(allButtons[targetIndex]);
                SaveCurrentlyFocusedGame(allButtons[targetIndex]);
            }
            else if (_contentScrollViewer != null)
            {
                // Already at bottom row - scroll to absolute bottom to show game labels
                _contentScrollViewer.UpdateLayout();
                double maxScroll = Math.Max(0, _contentScrollViewer.ExtentHeight - _contentScrollViewer.ViewportHeight);

                System.Diagnostics.Debug.WriteLine($"⬇️ EDGE SCROLL DOWN - At bottom row (index={currentIndex})");
                System.Diagnostics.Debug.WriteLine($"⬇️   ExtentHeight: {_contentScrollViewer.ExtentHeight}");
                System.Diagnostics.Debug.WriteLine($"⬇️   ViewportHeight: {_contentScrollViewer.ViewportHeight}");
                System.Diagnostics.Debug.WriteLine($"⬇️   Current scroll: {_contentScrollViewer.VerticalOffset}");
                System.Diagnostics.Debug.WriteLine($"⬇️   Max scroll (target): {maxScroll}");

                // Always try to scroll to bottom (ChangeView will clamp to valid range)
                bool scrolled = _contentScrollViewer.ChangeView(null, maxScroll, null, disableAnimation: false);
                System.Diagnostics.Debug.WriteLine($"⬇️   ChangeView returned: {scrolled}");
            }
        }

        private void NavigateLeft()
        {
            // Track gamepad navigation input
            _lastUsedGamepadInput = true;

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

        private void InvokeFocusedButton()
        {
            var allButtons = FindAllGameButtonsInVisualTree(GamesItemsControl);
            var focusedButton = FindFocusedButton(allButtons);
            if (focusedButton != null && focusedButton.Tag is DetectedGame game)
            {
                System.Diagnostics.Debug.WriteLine($"LibraryPage: A button pressed - launching {game.DisplayName}");
                GameTile_Click(focusedButton, new RoutedEventArgs());
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
