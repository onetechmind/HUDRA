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

namespace HUDRA.Pages
{
    public sealed partial class LibraryPage : Page, INotifyPropertyChanged
    {
        private EnhancedGameDetectionService? _gameDetectionService;
        private GameLauncherService? _gameLauncherService;
        private ObservableCollection<DetectedGame> _games = new ObservableCollection<DetectedGame>();

        // State preservation
        private double _savedScrollOffset = 0;
        private string? _savedFocusedGameProcessName = null;

        public event PropertyChangedEventHandler? PropertyChanged;

        public LibraryPage()
        {
            this.InitializeComponent();

            // Initialize game launcher service
            _gameLauncherService = new GameLauncherService();

            // Don't add Loaded event handler - use OnNavigatedTo instead
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            // Load games when navigating to this page
            // By this time, Initialize() should have been called by MainWindow
            await LoadGamesAsync();

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

        public async void Initialize(EnhancedGameDetectionService gameDetectionService)
        {
            _gameDetectionService = gameDetectionService;
            System.Diagnostics.Debug.WriteLine("LibraryPage: Initialize called with game detection service");

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

                // Auto-focus first game button for gamepad navigation (after a short delay for rendering)
                await Task.Delay(100);
                TryFocusFirstGameButton();

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
            if (_savedScrollOffset > 0)
            {
                System.Diagnostics.Debug.WriteLine($"LibraryPage: Restoring scroll offset: {_savedScrollOffset}");

                // Force layout update
                LibraryScrollViewer.UpdateLayout();

                // Wait for layout to complete before restoring scroll position
                await Task.Delay(200);

                System.Diagnostics.Debug.WriteLine($"LibraryPage: ScrollViewer ExtentHeight: {LibraryScrollViewer.ExtentHeight}, ViewportHeight: {LibraryScrollViewer.ViewportHeight}");

                // Restore the scroll position
                bool success = LibraryScrollViewer.ChangeView(null, _savedScrollOffset, null, disableAnimation: true);
                System.Diagnostics.Debug.WriteLine($"LibraryPage: ChangeView returned: {success}, Current offset after restore: {LibraryScrollViewer.VerticalOffset}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"LibraryPage: No saved scroll offset to restore");
            }
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

            return buttons;
        }

        private void TryFocusFirstGameButton()
        {
            try
            {
                var allButtons = FindAllGameButtonsInVisualTree(GamesItemsControl);
                if (allButtons.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"LibraryPage: Setting focus to first game button");
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
