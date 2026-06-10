using HUDRA.Extensions;
using HUDRA.Interfaces;
using HUDRA.Models;
using HUDRA.Services;
using HUDRA.Services.GamepadInput;
using HUDRA.AttachedProperties;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace HUDRA.Pages
{
    public sealed partial class GameSettingsPage : Page, INotifyPropertyChanged, IGamepadBackHandler
    {
        private EnhancedGameDatabase? _gameDatabase;
        private SteamGridDbArtworkService? _artworkService;
        private DetectedGame? _currentGame;
        private string? _originalDisplayName;
        private string? _originalArtworkPath;
        private string? _pendingArtworkPath;
        private bool _artworkChanged = false;
        private List<string> _tempSgdbPaths = new();
        private string _artworkDirectory = string.Empty;
        private string? _selectedSgdbPath = null;  // Track selected SGDB tile for visual feedback

        public event PropertyChangedEventHandler? PropertyChanged;

        public GameSettingsPage()
        {
            this.InitializeComponent();

            // Get artwork directory path
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HUDRA");
            _artworkDirectory = Path.Combine(appDataPath, "artwork");

            if (!Directory.Exists(_artworkDirectory))
            {
                Directory.CreateDirectory(_artworkDirectory);
            }
        }

        public void Initialize(EnhancedGameDatabase gameDatabase, SteamGridDbArtworkService? artworkService)
        {
            _gameDatabase = gameDatabase;
            _artworkService = artworkService;
        }

        public void LoadGame(string processName)
        {
            if (_gameDatabase == null) return;

            _currentGame = _gameDatabase.GetGame(processName);
            if (_currentGame == null)
            {
                System.Diagnostics.Debug.WriteLine($"GameSettingsPage: Game with ProcessName '{processName}' not found");
                return;
            }

            // Store original values
            _originalDisplayName = _currentGame.DisplayName;
            _originalArtworkPath = _currentGame.ArtworkPath;

            // Populate UI
            DisplayNameTextBox.Text = _currentGame.DisplayName;

            if (!string.IsNullOrEmpty(_currentGame.ArtworkPath))
            {
                // Strip any existing cache-busting query string to get the clean path for File.Exists check
                var cleanPath = _currentGame.ArtworkPath.Contains('?')
                    ? _currentGame.ArtworkPath.Substring(0, _currentGame.ArtworkPath.IndexOf('?'))
                    : _currentGame.ArtworkPath;

                if (File.Exists(cleanPath))
                {
                    // Add cache-busting query string to force reload (prevents stale cached images)
                    var cacheBustPath = $"{cleanPath}?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                    ArtworkPreview.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
                        new Uri(cacheBustPath));
                }
            }

            // Load game profile if it exists
            LoadGameProfile();

            // Subscribe to auto-save events (unsubscribe first to prevent duplicates if page is cached)
            GameProfileControl.ProfileChanged -= GameProfileControl_ProfileChanged;
            DisplayNameTextBox.TextChanged -= DisplayNameTextBox_TextChanged;
            GameProfileControl.ProfileChanged += GameProfileControl_ProfileChanged;
            DisplayNameTextBox.TextChanged += DisplayNameTextBox_TextChanged;
        }

        private void LoadGameProfile()
        {
            if (_currentGame == null) return;

            try
            {
                if (!string.IsNullOrEmpty(_currentGame.ProfileJson))
                {
                    var profile = JsonSerializer.Deserialize<GameProfile>(_currentGame.ProfileJson);
                    if (profile != null)
                    {
                        GameProfileControl.Profile = profile;
                        System.Diagnostics.Debug.WriteLine($"GameSettingsPage: Loaded profile for {_currentGame.ProcessName}");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GameSettingsPage: Error loading profile: {ex.Message}");
            }

            // No profile or error - use default empty profile
            GameProfileControl.Profile = new GameProfile();
        }

        private void SaveGameProfile()
        {
            if (_currentGame == null) return;

            try
            {
                var profile = GameProfileControl.Profile;

                // Update HasProfile based on whether any settings are enabled
                profile.HasProfile = profile.HasAnySettingsConfigured;

                // If no settings are configured, clear the profile entirely
                // This prevents "ghost" profiles where all settings are at default
                if (!profile.HasAnySettingsConfigured)
                {
                    _currentGame.ProfileJson = null;
                    System.Diagnostics.Debug.WriteLine($"GameSettingsPage: Cleared profile for {_currentGame.ProcessName} (no settings configured)");
                }
                else
                {
                    // Serialize and store
                    _currentGame.ProfileJson = JsonSerializer.Serialize(profile);
                    System.Diagnostics.Debug.WriteLine($"GameSettingsPage: Saved profile for {_currentGame.ProcessName} (HasProfile: {profile.HasProfile})");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GameSettingsPage: Error saving profile: {ex.Message}");
            }
        }

        // ── Gamepad integration ─────────────────────────────────────────────────
        // All interactive elements (including the SGDB result tiles generated
        // from the item template) are individual focus candidates; spatial
        // navigation and the generic adapters handle movement and activation,
        // and the shared FocusIndicatorLayer renders focus. Only the page-wide
        // B-button behavior needs code:

        /// <summary>B anywhere on this page navigates back to the Library.</summary>
        public bool HandleBack()
        {
            BackButton_Click(this, new RoutedEventArgs());
            return true;
        }

        // Button click handlers
        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            // Cleanup temp SGDB files (except the one selected as artwork)
            foreach (var tempPath in _tempSgdbPaths)
            {
                if (File.Exists(tempPath) && tempPath != _pendingArtworkPath)
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
            NavigateToLibrary();
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentGame == null || _gameDatabase == null) return;

            // Get MainWindow for gamepad support
            var mainWindow = Application.Current is App app ? app.MainWindow as MainWindow : null;

            var dialog = new ContentDialog
            {
                Title = "Delete Game",
                Content = "Are you sure you want to delete this game from HUDRA's Library?",
                PrimaryButtonText = "Yes",
                CloseButtonText = "No",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowWithGamepadSupportAsync(mainWindow?.GamepadNavigationService);

            if (result != ContentDialogResult.Primary) return;

            // Delete the artwork file first (if it exists in our artwork folder)
            var artworkPath = _currentGame.ArtworkPath;
            if (!string.IsNullOrEmpty(artworkPath))
            {
                // Strip any cache-busting query string (e.g., "?t=123456")
                var cleanPath = artworkPath.Contains('?')
                    ? artworkPath.Substring(0, artworkPath.IndexOf('?'))
                    : artworkPath;

                // Only delete if it's in our artwork directory (don't delete external files)
                if (cleanPath.StartsWith(_artworkDirectory, StringComparison.OrdinalIgnoreCase) && File.Exists(cleanPath))
                {
                    try
                    {
                        File.Delete(cleanPath);
                        System.Diagnostics.Debug.WriteLine($"GameSettingsPage: Deleted artwork file '{cleanPath}'");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"GameSettingsPage: Failed to delete artwork file: {ex.Message}");
                        // Continue with game deletion even if artwork deletion fails
                    }
                }
            }

            // Delete the game from database
            var processName = _currentGame.ProcessName;
            if (_gameDatabase.DeleteGame(processName))
            {
                System.Diagnostics.Debug.WriteLine($"GameSettingsPage: Deleted game '{processName}'");

                // Refresh library and navigate back
                if (mainWindow != null)
                {
                    mainWindow.RefreshLibrary();
                }

                // Navigate back to Library
                BackButton_Click(sender, e);
            }
            else
            {
                // Show error if deletion failed
                var errorDialog = new ContentDialog
                {
                    Title = "Error",
                    Content = "Failed to delete the game. Please try again.",
                    CloseButtonText = "OK",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = this.XamlRoot
                };
                var mainWindowForDialog = (Application.Current as App)?.MainWindow as MainWindow;
                await errorDialog.ShowWithGamepadSupportAsync(mainWindowForDialog?.GamepadNavigationService);
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Use Windows Forms OpenFileDialog (works in admin mode)
                using var openFileDialog = new System.Windows.Forms.OpenFileDialog
                {
                    Title = "Select Artwork Image",
                    Filter = "Image Files (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|PNG Files (*.png)|*.png|JPG Files (*.jpg;*.jpeg)|*.jpg;*.jpeg|WebP Files (*.webp)|*.webp|All Files (*.*)|*.*",
                    FilterIndex = 1,
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
                };

                var dialogResult = openFileDialog.ShowDialog();
                if (dialogResult != System.Windows.Forms.DialogResult.OK)
                {
                    return;
                }

                string selectedPath = openFileDialog.FileName;

                // Validate file size (max 10MB)
                var fileInfo = new FileInfo(selectedPath);
                if (fileInfo.Length > 10 * 1024 * 1024)
                {
                    ShowArtworkError("File size must be less than 10MB");
                    return;
                }

                // Set as pending artwork
                _pendingArtworkPath = selectedPath;
                _artworkChanged = true;

                // Update preview
                ArtworkPreview.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
                    new Uri(selectedPath));

                HideArtworkError();

                // Save artwork immediately
                SaveArtwork(_pendingArtworkPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GameSettingsPage: Error browsing for artwork: {ex.Message}");
                ShowArtworkError("Failed to load artwork file");
            }
        }

        private async void SteamGridDbButton_Click(object sender, RoutedEventArgs e)
        {
            if (_artworkService == null || _currentGame == null) return;

            try
            {
                // Show section and loading indicator
                SgdbResultsSection.Visibility = Visibility.Visible;
                SgdbLoadingIndicator.Visibility = Visibility.Visible;
                SgdbErrorText.Visibility = Visibility.Collapsed;
                SgdbImageGrid.ItemsSource = null;

                // Fetch artwork options using the current Display Name field value (allows user to correct search term)
                var results = await _artworkService.GetArtworkOptionsAsync(DisplayNameTextBox.Text, 10);

                SgdbLoadingIndicator.Visibility = Visibility.Collapsed;

                if (results == null || results.Count == 0)
                {
                    ShowSgdbError("No artwork found on SteamGridDB for this game.");
                    return;
                }

                // Store temp paths for cleanup
                _tempSgdbPaths = results.Select(r => r.TempFilePath).ToList();

                // Display results
                SgdbImageGrid.ItemsSource = results;

                // Reset selection
                _selectedSgdbPath = null;

                // Delay to allow ItemsControl to render, then update borders
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    UpdateSgdbTileSelection();
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GameSettingsPage: Error fetching SteamGridDB artwork: {ex.Message}");
                SgdbLoadingIndicator.Visibility = Visibility.Collapsed;
                ShowSgdbError("Failed to fetch artwork from SteamGridDB.");
            }
        }

        private void SgdbImage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not SteamGridDbResult result)
                return;

            // Set as pending artwork
            _pendingArtworkPath = result.TempFilePath;
            _artworkChanged = true;
            _selectedSgdbPath = result.TempFilePath;

            // Update preview
            ArtworkPreview.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
                new Uri(result.TempFilePath));

            // Update selection borders
            UpdateSgdbTileSelection();

            HideArtworkError();

            // Save artwork immediately
            SaveArtwork(_pendingArtworkPath);
        }

        private void NavigateToLibrary()
        {
            var app = Application.Current as App;
            var mainWindow = app?.MainWindow;
            var navigationService = mainWindow?.NavigationService;
            navigationService?.NavigateToLibrary();
        }

        // Helper methods
        private string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(fileName.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
            return sanitized;
        }

        private void SaveArtwork(string? sourcePath)
        {
            if (_currentGame == null || _gameDatabase == null || string.IsNullOrEmpty(sourcePath)) return;

            try
            {
                var extension = Path.GetExtension(sourcePath);
                var newFileName = $"{SanitizeFileName(_currentGame.ProcessName)}{extension}";
                var newPath = Path.Combine(_artworkDirectory, newFileName);

                // Delete old artwork if different path
                if (!string.IsNullOrEmpty(_originalArtworkPath))
                {
                    // Strip any cache-busting query string
                    var cleanOriginalPath = _originalArtworkPath.Contains('?')
                        ? _originalArtworkPath.Substring(0, _originalArtworkPath.IndexOf('?'))
                        : _originalArtworkPath;

                    if (File.Exists(cleanOriginalPath) && cleanOriginalPath != newPath)
                    {
                        File.Delete(cleanOriginalPath);
                    }
                }

                // Copy new artwork
                if (sourcePath != newPath)
                {
                    File.Copy(sourcePath, newPath, overwrite: true);
                }

                _currentGame.ArtworkPath = newPath;
                _originalArtworkPath = newPath; // Update so subsequent saves don't re-delete
                _gameDatabase.SaveGame(_currentGame);

                // Refresh library artwork
                if (Application.Current is App app && app.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.RefreshLibraryGameArtwork(_currentGame.ProcessName);
                }

                System.Diagnostics.Debug.WriteLine($"GameSettingsPage: Saved artwork to {newPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GameSettingsPage: SaveArtwork error: {ex.Message}");
            }
        }

        // Auto-save handlers
        private void GameProfileControl_ProfileChanged(object? sender, EventArgs e)
        {
            if (_currentGame == null || _gameDatabase == null) return;
            SaveGameProfile();
            _gameDatabase.SaveGame(_currentGame);
            System.Diagnostics.Debug.WriteLine($"GameSettingsPage: Auto-saved profile for {_currentGame.ProcessName}");
        }

        private void DisplayNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_currentGame == null || _gameDatabase == null) return;

            var newDisplayName = DisplayNameTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(newDisplayName) || newDisplayName.Length > 100) return;

            _currentGame.DisplayName = newDisplayName;
            _gameDatabase.SaveGame(_currentGame);
        }

        private void ShowDisplayNameError(string message)
        {
            DisplayNameErrorText.Text = message;
            DisplayNameErrorText.Visibility = Visibility.Visible;
        }

        private void HideDisplayNameError()
        {
            DisplayNameErrorText.Visibility = Visibility.Collapsed;
        }

        private void ShowArtworkError(string message)
        {
            ArtworkErrorText.Text = message;
            ArtworkErrorText.Visibility = Visibility.Visible;
        }

        private void HideArtworkError()
        {
            ArtworkErrorText.Visibility = Visibility.Collapsed;
        }

        private void ShowSgdbError(string message)
        {
            SgdbErrorText.Text = message;
            SgdbErrorText.Visibility = Visibility.Visible;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void UpdateSgdbTileSelection()
        {
            if (SgdbImageGrid.ItemsSource == null) return;

            // Find all Border elements in the ItemsControl
            for (int i = 0; i < SgdbImageGrid.Items.Count; i++)
            {
                var container = SgdbImageGrid.ContainerFromIndex(i) as FrameworkElement;
                if (container == null) continue;

                // Find the Border in the visual tree
                var border = FindVisualChild<Border>(container);
                if (border == null) continue;

                // Get the Button which has the Tag
                var button = FindVisualChild<Button>(container);
                if (button?.Tag is SteamGridDbResult result)
                {
                    // Update border based on selection
                    if (result.TempFilePath == _selectedSgdbPath)
                    {
                        border.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
                        border.BorderThickness = new Thickness(3);
                    }
                    else
                    {
                        border.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                        border.BorderThickness = new Thickness(2);
                    }
                }
            }
        }

        private T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T typedChild)
                    return typedChild;

                var result = FindVisualChild<T>(child);
                if (result != null)
                    return result;
            }

            return null;
        }
    }
}
