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
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            System.Diagnostics.Debug.WriteLine("LibraryPage: OnNavigatedTo called");

            // Only load games if not already loaded (to preserve scroll position)
            if (!_gamesLoaded)
            {
                await LoadGamesAsync();
            }

            // Restore scroll position after layout is complete
            await RestoreScrollPositionAsync();

            // Restore focused game if gamepad was being used
            await RestoreFocusedGameAsync();

            // If scanning is already in progress, show the indicator
            if (_gameDetectionService != null && _gameDetectionService.IsScanning)
            {
                ShowScanProgress("Scanning...");
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            // Unsubscribe from raw gamepad input
            if (_gamepadNavigationService != null)
            {
                _gamepadNavigationService.RawGamepadInput -= OnRawGamepadInput;
                System.Diagnostics.Debug.WriteLine("LibraryPage: Unsubscribed from raw gamepad input");
            }

            // Save scroll position
            _savedScrollOffset = LibraryScrollViewer.VerticalOffset;
            System.Diagnostics.Debug.WriteLine($"LibraryPage: Saving scroll offset: {_savedScrollOffset}");

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
                        System.Diagnostics.Debug.WriteLine($"LibraryPage: Saving focused game: {game.DisplayName}");
                        break;
                    }
                    current = current.Parent as FrameworkElement;
                }
            }

            // Unsubscribe from events to prevent memory leaks
            if (_gameDetectionService != null)
            {
                _gameDetectionService.ScanningStateChanged -= OnScanningStateChanged;
                _gameDetectionService.ScanProgressChanged -= OnScanProgressChanged;
            }
        }

        public async void Initialize(EnhancedGameDetectionService gameDetectionService, GamepadNavigationService gamepadNavigationService)
        {
            _gameDetectionService = gameDetectionService;
            _gamepadNavigationService = gamepadNavigationService;
            System.Diagnostics.Debug.WriteLine("LibraryPage: Initialize called with game detection and gamepad navigation services");

            // Subscribe to raw gamepad input immediately upon receiving the service reference
            _gamepadNavigationService.RawGamepadInput += OnRawGamepadInput;
            System.Diagnostics.Debug.WriteLine("LibraryPage: Subscribed to raw gamepad input");

            // Subscribe to scan events for reactive updates
            _gameDetectionService.ScanningStateChanged += OnScanningStateChanged;
            _gameDetectionService.ScanProgressChanged += OnScanProgressChanged;

            // Load games immediately after initialization
            await LoadGamesAsync();

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
                System.Diagnostics.Debug.WriteLine("LibraryPage: LoadGamesAsync started");

                if (_gameDetectionService == null)
                {
                    System.Diagnostics.Debug.WriteLine("LibraryPage: _gameDetectionService is null");
                    ShowEmptyState();
                    return;
                }

                // Get all games from the database
                var allGames = await _gameDetectionService.GetAllGamesAsync();
                var gamesList = allGames?.ToList() ?? new List<DetectedGame>();

                System.Diagnostics.Debug.WriteLine($"LibraryPage: Retrieved {gamesList.Count} games from database");

                if (!gamesList.Any())
                {
                    System.Diagnostics.Debug.WriteLine("LibraryPage: No games found in database");
                    ShowEmptyState();
                    return;
                }

                // Sort alphabetically by display name
                gamesList = gamesList.OrderBy(g => g.DisplayName).ToList();

                System.Diagnostics.Debug.WriteLine($"LibraryPage: Populating UI with {gamesList.Count} games");

                // Update the ObservableCollection
                _games.Clear();
                foreach (var game in gamesList)
                {
                    _games.Add(game);
                    System.Diagnostics.Debug.WriteLine($"LibraryPage: Added game: {game.DisplayName}, Artwork: {game.ArtworkPath}");
                }

                // Set ItemsSource
                GamesItemsControl.ItemsSource = _games;

                // Hide empty state
                EmptyStatePanel.Visibility = Visibility.Collapsed;

                // Mark games as loaded
                _gamesLoaded = true;

                // Don't auto-focus here - MainWindow will handle it for gamepad navigation
                System.Diagnostics.Debug.WriteLine("LibraryPage: LoadGamesAsync completed successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LibraryPage: Error loading games: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"LibraryPage: Stack trace: {ex.StackTrace}");
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
            System.Diagnostics.Debug.WriteLine($"LibraryPage: RestoreScrollPosition called with offset: {_savedScrollOffset}");

            // Always try to restore, even if offset is 0
            // Force layout update first
            LibraryScrollViewer.UpdateLayout();

            // Wait for layout to complete
            await Task.Delay(300);

            System.Diagnostics.Debug.WriteLine($"LibraryPage: Before restore - ExtentHeight: {LibraryScrollViewer.ExtentHeight}, ViewportHeight: {LibraryScrollViewer.ViewportHeight}, CurrentOffset: {LibraryScrollViewer.VerticalOffset}");

            // Restore the scroll position
            bool success = LibraryScrollViewer.ChangeView(null, _savedScrollOffset, null, disableAnimation: true);

            System.Diagnostics.Debug.WriteLine($"LibraryPage: ChangeView({_savedScrollOffset}) returned: {success}");

            // Verify restoration worked
            await Task.Delay(100);
            System.Diagnostics.Debug.WriteLine($"LibraryPage: After restore - Current offset: {LibraryScrollViewer.VerticalOffset}");
        }

        private async Task RestoreFocusedGameAsync()
        {
            if (!string.IsNullOrEmpty(_savedFocusedGameProcessName))
            {
                System.Diagnostics.Debug.WriteLine($"LibraryPage: Attempting to restore focus to game: {_savedFocusedGameProcessName}");

                // Wait for UI to be fully rendered
                await Task.Delay(100);

                // Find the game button that matches the saved process name
                var gameButton = FindGameButton(_savedFocusedGameProcessName);
                if (gameButton != null)
                {
                    System.Diagnostics.Debug.WriteLine($"LibraryPage: Restoring focus to button");
                    gameButton.Focus(FocusState.Programmatic);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"LibraryPage: Could not find button for game: {_savedFocusedGameProcessName}");
                }
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
                        double sortKey = position.Y * 10000 + position.X;
                        System.Diagnostics.Debug.WriteLine($"LibraryPage: Button at position Y={position.Y:F1}, X={position.X:F1}, sortKey={sortKey:F1}, Tag={((DetectedGame)b.Tag).DisplayName}");
                        return sortKey;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"LibraryPage: Error getting button position: {ex.Message}");
                        return double.MaxValue; // Put errored buttons at the end
                    }
                }).ToList();

                if (buttons.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"LibraryPage: Sorted {buttons.Count} buttons, first button: {((DetectedGame)buttons[0].Tag).DisplayName}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"LibraryPage: No buttons found to sort");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LibraryPage: Error sorting buttons: {ex.Message}");
            }

            return buttons;
        }

        public void FocusFirstGameButton()
        {
            try
            {
                var allButtons = FindAllGameButtonsInVisualTree(GamesItemsControl);
                if (allButtons.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"LibraryPage: Setting focus to first game button (found {allButtons.Count} buttons)");
                    allButtons[0].Focus(FocusState.Programmatic);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"LibraryPage: No game buttons found to focus");
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
                // Log when we receive input (only log when buttons are pressed to avoid spam)
                if (reading.Buttons != GamepadButtons.None)
                {
                    System.Diagnostics.Debug.WriteLine($"LibraryPage: Received gamepad input - Buttons: {reading.Buttons}");
                }

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
            double scrollAmount = -yValue * 10;
            double currentOffset = LibraryScrollViewer.VerticalOffset;
            double newOffset = currentOffset + scrollAmount;

            System.Diagnostics.Debug.WriteLine($"LibraryPage: Analog scroll - yValue: {yValue:F2}, scrollAmount: {scrollAmount:F2}, current: {currentOffset:F1}, new: {newOffset:F1}");

            LibraryScrollViewer.ChangeView(null, newOffset, null, disableAnimation: true);
        }

        private void EnsureButtonVisible(Button button)
        {
            // Get button position relative to ScrollViewer's viewport
            try
            {
                var transform = button.TransformToVisual(LibraryScrollViewer);
                var position = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

                // Use ActualHeight as the visible viewport height (ViewportHeight can be unreliable)
                double viewportHeight = LibraryScrollViewer.ActualHeight;
                double currentOffset = LibraryScrollViewer.VerticalOffset;
                double buttonTop = position.Y;
                double buttonBottom = buttonTop + button.ActualHeight;

                System.Diagnostics.Debug.WriteLine($"LibraryPage: EnsureVisible - ScrollViewer ActualHeight={viewportHeight:F1}, ViewportHeight={LibraryScrollViewer.ViewportHeight:F1}, ExtentHeight={LibraryScrollViewer.ExtentHeight:F1}");
                System.Diagnostics.Debug.WriteLine($"LibraryPage: EnsureVisible - currentOffset: {currentOffset:F1}, buttonTop: {buttonTop:F1}, buttonBottom: {buttonBottom:F1}");

                // Add padding for better UX
                const double PADDING = 20;

                // Button position is relative to ScrollViewer viewport, so:
                // - If buttonTop < 0, button is scrolled above the viewport
                // - If buttonTop > viewportHeight, button is scrolled below the viewport

                // If button is above viewport (scrolled up past the top)
                if (buttonTop < PADDING)
                {
                    double newOffset = currentOffset + buttonTop - PADDING;
                    System.Diagnostics.Debug.WriteLine($"LibraryPage: Button above viewport - scrolling UP to offset {Math.Max(0, newOffset):F1}");
                    LibraryScrollViewer.ChangeView(null, Math.Max(0, newOffset), null, disableAnimation: false);
                }
                // If button is below viewport (needs to scroll down to see it)
                else if (buttonBottom > viewportHeight - PADDING)
                {
                    double newOffset = currentOffset + (buttonBottom - viewportHeight) + PADDING;
                    System.Diagnostics.Debug.WriteLine($"LibraryPage: Button below viewport - scrolling DOWN to offset {newOffset:F1}");
                    LibraryScrollViewer.ChangeView(null, newOffset, null, disableAnimation: false);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"LibraryPage: Button fully visible in viewport - no scroll needed");
                }
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
