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
            this.Loaded += LibraryPage_Loaded;

            // Initialize game launcher service
            _gameLauncherService = new GameLauncherService();
        }

        private async void LibraryPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadGamesAsync();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
        }

        public void Initialize(EnhancedGameDetectionService gameDetectionService)
        {
            _gameDetectionService = gameDetectionService;
        }

        private async Task LoadGamesAsync()
        {
            try
            {
                if (_gameDetectionService == null || Application.Current is not App app)
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
