using HUDRA.Models;
using HUDRA.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
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

            // If scanning is already in progress, show the indicator
            if (_gameDetectionService != null && _gameDetectionService.IsScanning)
            {
                ShowScanProgress("Scanning...");
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

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

            try
            {
                // Show launching indicator
                ShowLaunchingIndicator(game.DisplayName);

                // Launch the game
                bool success = _gameLauncherService?.LaunchGame(game) ?? false;

                if (!success)
                {
                    System.Diagnostics.Debug.WriteLine($"LibraryPage: Failed to launch {game.DisplayName}");
                }

                // Hide launching indicator after a delay
                await Task.Delay(3000);
                HideLaunchingIndicator();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LibraryPage: Error launching game: {ex.Message}");
                HideLaunchingIndicator();
            }
        }

        private void ShowLaunchingIndicator(string gameName)
        {
            LaunchingText.Text = $"Launching {gameName}...";
            LaunchingIndicator.Visibility = Visibility.Visible;
        }

        private void HideLaunchingIndicator()
        {
            LaunchingIndicator.Visibility = Visibility.Collapsed;
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
