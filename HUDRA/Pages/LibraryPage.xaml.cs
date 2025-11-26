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
        private ObservableCollection<DetectedGame> _games = new ObservableCollection<DetectedGame>();

        // State preservation
        private double _savedScrollOffset = 0;
        private string? _savedFocusedGameProcessName = null;
        private bool _gamesLoaded = false;

        // Gamepad navigation
        private readonly HashSet<GamepadButtons> _pressedButtons = new();
        private DateTime _lastInputTime = DateTime.MinValue;
        private const double INPUT_REPEAT_DELAY_MS = 150;

        public event PropertyChangedEventHandler? PropertyChanged;

        public LibraryPage()
        {
            this.InitializeComponent();

            // Initialize game launcher service
            _gameLauncherService = new GameLauncherService();

            // Subscribe to Loaded event to set up scroll tracking
            this.Loaded += OnPageLoaded;
        }

        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            // Subscribe to scroll changes to continuously track position
            // Must be done after page is loaded to ensure ScrollViewer is initialized
            LibraryScrollViewer.ViewChanged += OnScrollViewChanged;
            System.Diagnostics.Debug.WriteLine("📜 LibraryPage: ViewChanged event handler attached");
        }

        private void OnScrollViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            // Continuously update saved scroll position as user scrolls
            // This ensures we always have the latest position, regardless of when navigation occurs
            _savedScrollOffset = LibraryScrollViewer.VerticalOffset;
            System.Diagnostics.Debug.WriteLine($"📜 ViewChanged: scroll now at {_savedScrollOffset:F1}");
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

            // Save state when navigating away
            SaveScrollPosition();

            // Unsubscribe from raw gamepad input
            if (_gamepadNavigationService != null)
            {
                _gamepadNavigationService.RawGamepadInput -= OnRawGamepadInput;
            }

            // Unsubscribe from events to prevent memory leaks
            if (_gameDetectionService != null)
            {
                _gameDetectionService.ScanningStateChanged -= OnScanningStateChanged;
                _gameDetectionService.ScanProgressChanged -= OnScanProgressChanged;
            }
        }

        /// <summary>
        /// Saves the focused game state. Called when navigating away from the page.
        /// Note: Scroll position is continuously tracked via ViewChanged event.
        /// </summary>
        public void SaveScrollPosition()
        {
            // Scroll position is already being tracked continuously via ViewChanged event
            // Just save the focused game here

            System.Diagnostics.Debug.WriteLine($"📜 SaveScrollPosition CALLED (leaving Library page)");
            System.Diagnostics.Debug.WriteLine($"📜   Already tracked scroll: {_savedScrollOffset}");
            System.Diagnostics.Debug.WriteLine($"📜   Current scroll: {LibraryScrollViewer.VerticalOffset}");
            System.Diagnostics.Debug.WriteLine($"📜   ExtentHeight: {LibraryScrollViewer.ExtentHeight}");
            System.Diagnostics.Debug.WriteLine($"📜   ViewportHeight: {LibraryScrollViewer.ViewportHeight}");

            // Save focused game for gamepad navigation
            if (FocusManager.GetFocusedElement(this.XamlRoot) is FrameworkElement focusedElement)
            {
                // Walk up the visual tree to find the Button with a DetectedGame tag
                var current = focusedElement;
                while (current != null)
                {
                    if (current is Button button && button.Tag is DetectedGame game)
                    {
                        _savedFocusedGameProcessName = game.ProcessName;
                        System.Diagnostics.Debug.WriteLine($"📜   Focused game: {game.ProcessName}");
                        break;
                    }
                    current = current.Parent as FrameworkElement;
                }
            }
            else
            {
                _savedFocusedGameProcessName = null;
                System.Diagnostics.Debug.WriteLine($"📜   No focused game");
            }
        }

        public async void Initialize(EnhancedGameDetectionService gameDetectionService, GamepadNavigationService gamepadNavigationService)
        {
            System.Diagnostics.Debug.WriteLine($"LibraryPage: Initialize called - _savedScrollOffset={_savedScrollOffset}, _savedFocusedGameProcessName={_savedFocusedGameProcessName}");

            _gameDetectionService = gameDetectionService;
            _gamepadNavigationService = gamepadNavigationService;

            // Subscribe to raw gamepad input immediately upon receiving the service reference
            _gamepadNavigationService.RawGamepadInput += OnRawGamepadInput;

            // Subscribe to scan events for reactive updates
            _gameDetectionService.ScanningStateChanged += OnScanningStateChanged;
            _gameDetectionService.ScanProgressChanged += OnScanProgressChanged;

            // Load games only if not already loaded
            if (!_gamesLoaded)
            {
                await LoadGamesAsync();
            }

            // Restore focused game first (if any) - this may scroll
            await RestoreFocusedGameAsync();

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

                // Sort alphabetically by display name
                gamesList = gamesList.OrderBy(g => g.DisplayName).ToList();

                // Update the ObservableCollection
                _games.Clear();
                foreach (var game in gamesList)
                {
                    _games.Add(game);
                }

                // Set ItemsSource
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

        private async void GameTile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not DetectedGame game)
                return;

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
                LibraryScrollViewer.UpdateLayout();
                await Task.Delay(100);

                // Check if the ScrollViewer has measured its content
                if (LibraryScrollViewer.ExtentHeight > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"📜   ScrollViewer ready on attempt {attempt + 1}");
                    System.Diagnostics.Debug.WriteLine($"📜     ExtentHeight: {LibraryScrollViewer.ExtentHeight}");
                    System.Diagnostics.Debug.WriteLine($"📜     ViewportHeight: {LibraryScrollViewer.ViewportHeight}");
                    System.Diagnostics.Debug.WriteLine($"📜     Current offset: {LibraryScrollViewer.VerticalOffset}");
                    break;
                }
                System.Diagnostics.Debug.WriteLine($"📜   Waiting for layout... attempt {attempt + 1}");
            }

            // Restore the scroll position
            bool success = LibraryScrollViewer.ChangeView(null, _savedScrollOffset, null, disableAnimation: true);
            System.Diagnostics.Debug.WriteLine($"📜   ChangeView() returned: {success}, target: {_savedScrollOffset}");

            // Verify restoration worked
            await Task.Delay(100);
            System.Diagnostics.Debug.WriteLine($"📜   FINAL scroll position: {LibraryScrollViewer.VerticalOffset}");
        }

        private async Task RestoreFocusedGameAsync()
        {
            if (!string.IsNullOrEmpty(_savedFocusedGameProcessName))
            {
                System.Diagnostics.Debug.WriteLine($"LibraryPage: RestoreFocusedGameAsync - Restoring focus to: {_savedFocusedGameProcessName}");

                // Wait for UI to be fully rendered
                await Task.Delay(100);

                // Find the game button that matches the saved process name
                var gameButton = FindGameButton(_savedFocusedGameProcessName);
                if (gameButton != null)
                {
                    System.Diagnostics.Debug.WriteLine($"LibraryPage: Found button, focusing it (this may scroll)");
                    gameButton.Focus(FocusState.Programmatic);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"LibraryPage: Could not find button for {_savedFocusedGameProcessName}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"LibraryPage: No saved focus to restore");
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
            var allButtons = FindAllGameButtonsInVisualTree(GamesItemsControl);
            if (allButtons.Count == 0) return;

            var focusedButton = FindFocusedButton(allButtons);

            // If no button is focused, focus the first button
            if (focusedButton == null)
            {
                allButtons[0].Focus(FocusState.Programmatic);
                EnsureButtonVisible(allButtons[0]);
                return;
            }

            int currentIndex = allButtons.IndexOf(focusedButton);
            // In a 2-column grid, move up 2 positions
            int targetIndex = currentIndex - 2;
            if (targetIndex >= 0)
            {
                allButtons[targetIndex].Focus(FocusState.Programmatic);
                EnsureButtonVisible(allButtons[targetIndex]);
            }
            else
            {
                // Already at top row - scroll to absolute top to show header
                LibraryScrollViewer.UpdateLayout();
                System.Diagnostics.Debug.WriteLine($"⬆️ EDGE SCROLL UP - At top row (index={currentIndex})");
                System.Diagnostics.Debug.WriteLine($"⬆️   Current scroll: {LibraryScrollViewer.VerticalOffset}");
                System.Diagnostics.Debug.WriteLine($"⬆️   Scrolling to: 0");
                bool scrolled = LibraryScrollViewer.ChangeView(null, 0, null, disableAnimation: false);
                System.Diagnostics.Debug.WriteLine($"⬆️   ChangeView returned: {scrolled}");
            }
        }

        private void NavigateDown()
        {
            var allButtons = FindAllGameButtonsInVisualTree(GamesItemsControl);
            if (allButtons.Count == 0) return;

            var focusedButton = FindFocusedButton(allButtons);

            // If no button is focused, focus the first button
            if (focusedButton == null)
            {
                allButtons[0].Focus(FocusState.Programmatic);
                EnsureButtonVisible(allButtons[0]);
                return;
            }

            int currentIndex = allButtons.IndexOf(focusedButton);
            // In a 2-column grid, move down 2 positions
            int targetIndex = currentIndex + 2;
            if (targetIndex < allButtons.Count)
            {
                allButtons[targetIndex].Focus(FocusState.Programmatic);
                EnsureButtonVisible(allButtons[targetIndex]);
            }
            else
            {
                // Already at bottom row - scroll to absolute bottom to show game labels
                LibraryScrollViewer.UpdateLayout();
                double maxScroll = Math.Max(0, LibraryScrollViewer.ExtentHeight - LibraryScrollViewer.ViewportHeight);

                System.Diagnostics.Debug.WriteLine($"⬇️ EDGE SCROLL DOWN - At bottom row (index={currentIndex})");
                System.Diagnostics.Debug.WriteLine($"⬇️   ExtentHeight: {LibraryScrollViewer.ExtentHeight}");
                System.Diagnostics.Debug.WriteLine($"⬇️   ViewportHeight: {LibraryScrollViewer.ViewportHeight}");
                System.Diagnostics.Debug.WriteLine($"⬇️   Current scroll: {LibraryScrollViewer.VerticalOffset}");
                System.Diagnostics.Debug.WriteLine($"⬇️   Max scroll (target): {maxScroll}");

                // Always try to scroll to bottom (ChangeView will clamp to valid range)
                bool scrolled = LibraryScrollViewer.ChangeView(null, maxScroll, null, disableAnimation: false);
                System.Diagnostics.Debug.WriteLine($"⬇️   ChangeView returned: {scrolled}");
            }
        }

        private void NavigateLeft()
        {
            var allButtons = FindAllGameButtonsInVisualTree(GamesItemsControl);
            if (allButtons.Count == 0) return;

            var focusedButton = FindFocusedButton(allButtons);

            // If no button is focused, focus the first button
            if (focusedButton == null)
            {
                allButtons[0].Focus(FocusState.Programmatic);
                EnsureButtonVisible(allButtons[0]);
                return;
            }

            int currentIndex = allButtons.IndexOf(focusedButton);

            // Left: move left in same row, or wrap to right column of previous row
            if (currentIndex % 2 != 0) // If in second column (right)
            {
                // Move left to first column (same row)
                allButtons[currentIndex - 1].Focus(FocusState.Programmatic);
                EnsureButtonVisible(allButtons[currentIndex - 1]);
            }
            else // If in first column (left)
            {
                // Wrap to second column of previous row
                int targetIndex = currentIndex - 1;
                if (targetIndex >= 0)
                {
                    allButtons[targetIndex].Focus(FocusState.Programmatic);
                    EnsureButtonVisible(allButtons[targetIndex]);
                }
            }
        }

        private void NavigateRight()
        {
            var allButtons = FindAllGameButtonsInVisualTree(GamesItemsControl);
            if (allButtons.Count == 0) return;

            var focusedButton = FindFocusedButton(allButtons);

            // If no button is focused, focus the first button
            if (focusedButton == null)
            {
                allButtons[0].Focus(FocusState.Programmatic);
                EnsureButtonVisible(allButtons[0]);
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
            // Invert Y axis (positive thumbstick = scroll up)
            double scrollAmount = -yValue * 15; // Increased for more responsive scrolling
            double currentOffset = LibraryScrollViewer.VerticalOffset;
            double newOffset = currentOffset + scrollAmount;

            LibraryScrollViewer.ChangeView(null, newOffset, null, disableAnimation: true);
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
