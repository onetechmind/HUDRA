using HUDRA.Interfaces;
using HUDRA.Models;
using HUDRA.Services;
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
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace HUDRA.Pages
{
    public sealed partial class GameSettingsPage : Page, IGamepadNavigable, INotifyPropertyChanged
    {
        private EnhancedGameDatabase? _gameDatabase;
        private SteamGridDbArtworkService? _artworkService;
        private GamepadNavigationService? _gamepadNavigationService;
        private DetectedGame? _currentGame;
        private string? _originalDisplayName;
        private string? _originalArtworkPath;
        private string? _pendingArtworkPath;
        private bool _artworkChanged = false;
        private List<string> _tempSgdbPaths = new();
        private string _artworkDirectory = string.Empty;
        private string? _selectedSgdbPath = null;  // Track selected SGDB tile for visual feedback

        // Gamepad navigation state
        private bool _isFocused = false;
        private int _currentFocusedElement = 0; // 0=Back, 1=DisplayName, 2=Browse, 3=SGDB, 4=Save, 5=Cancel

        public event PropertyChangedEventHandler? PropertyChanged;

        public GameSettingsPage()
        {
            this.InitializeComponent();
            InitializeGamepadNavigation();

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

        private void InitializeGamepadNavigation()
        {
            GamepadNavigation.SetIsEnabled(this, true);
            GamepadNavigation.SetNavigationGroup(this, "GameSettings");
            GamepadNavigation.SetNavigationOrder(this, 1);
        }

        public void Initialize(EnhancedGameDatabase gameDatabase, SteamGridDbArtworkService artworkService)
        {
            _gameDatabase = gameDatabase;
            _artworkService = artworkService;

            // Lazy init gamepad service
            if (_gamepadNavigationService == null)
            {
                InitializeGamepadNavigationService();
            }
        }

        private void InitializeGamepadNavigationService()
        {
            if (Application.Current is App app && app.MainWindow is MainWindow mainWindow)
            {
                _gamepadNavigationService = mainWindow.GamepadNavigationService;
            }
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

            if (!string.IsNullOrEmpty(_currentGame.ArtworkPath) && File.Exists(_currentGame.ArtworkPath))
            {
                ArtworkPreview.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
                    new Uri(_currentGame.ArtworkPath));
            }
        }

        // IGamepadNavigable implementation
        public bool CanNavigateUp => _currentFocusedElement > 0;
        public bool CanNavigateDown => _currentFocusedElement < 5;
        public bool CanNavigateLeft => _currentFocusedElement == 5; // From Cancel to Save
        public bool CanNavigateRight => _currentFocusedElement == 4; // From Save to Cancel
        public bool CanActivate => true;
        public FrameworkElement NavigationElement => this;

        // Slider properties (not used)
        public bool IsSlider => false;
        public bool IsSliderActivated { get; set; } = false;
        public void AdjustSliderValue(int direction) { }

        // ComboBox properties (not used)
        public bool HasComboBoxes => false;
        public bool IsComboBoxOpen { get; set; } = false;
        public ComboBox? GetFocusedComboBox() => null;
        public int ComboBoxOriginalIndex { get; set; } = -1;
        public bool IsNavigatingComboBox { get; set; } = false;
        public void ProcessCurrentSelection() { }

        public bool IsFocused
        {
            get => _isFocused;
            set
            {
                if (_isFocused != value)
                {
                    _isFocused = value;
                    OnPropertyChanged();
                    UpdateFocusVisuals();
                }
            }
        }

        // Focus brush properties for each element
        public Brush BackButtonFocusBrush
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true
                    && _currentFocusedElement == 0)
                {
                    return new SolidColorBrush(Colors.DarkViolet);
                }
                return new SolidColorBrush(Colors.Transparent);
            }
        }

        public Brush DisplayNameFocusBrush
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true
                    && _currentFocusedElement == 1)
                {
                    return new SolidColorBrush(Colors.DarkViolet);
                }
                return new SolidColorBrush(Colors.Transparent);
            }
        }

        public Brush BrowseButtonFocusBrush
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true
                    && _currentFocusedElement == 2)
                {
                    return new SolidColorBrush(Colors.DarkViolet);
                }
                return new SolidColorBrush(Colors.Transparent);
            }
        }

        public Brush SgdbButtonFocusBrush
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true
                    && _currentFocusedElement == 3)
                {
                    return new SolidColorBrush(Colors.DarkViolet);
                }
                return new SolidColorBrush(Colors.Transparent);
            }
        }

        public Brush SaveButtonFocusBrush
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true
                    && _currentFocusedElement == 4)
                {
                    return new SolidColorBrush(Colors.DarkViolet);
                }
                return new SolidColorBrush(Colors.Transparent);
            }
        }

        public Brush CancelButtonFocusBrush
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true
                    && _currentFocusedElement == 5)
                {
                    return new SolidColorBrush(Colors.DarkViolet);
                }
                return new SolidColorBrush(Colors.Transparent);
            }
        }

        // Gamepad navigation handlers
        public void OnGamepadNavigateUp()
        {
            if (_currentFocusedElement > 0)
            {
                _currentFocusedElement--;
                UpdateFocusVisuals();
            }
        }

        public void OnGamepadNavigateDown()
        {
            if (_currentFocusedElement < 5)
            {
                _currentFocusedElement++;
                UpdateFocusVisuals();
            }
        }

        public void OnGamepadNavigateLeft()
        {
            if (_currentFocusedElement == 5) // From Cancel to Save
            {
                _currentFocusedElement = 4;
                UpdateFocusVisuals();
            }
        }

        public void OnGamepadNavigateRight()
        {
            if (_currentFocusedElement == 4) // From Save to Cancel
            {
                _currentFocusedElement = 5;
                UpdateFocusVisuals();
            }
        }

        public void OnGamepadActivate()
        {
            switch (_currentFocusedElement)
            {
                case 0: // Back button
                    BackButton_Click(this, new RoutedEventArgs());
                    break;
                case 1: // Display name TextBox
                    DisplayNameTextBox.Focus(FocusState.Programmatic);
                    DisplayNameTextBox.SelectAll();
                    break;
                case 2: // Browse button
                    BrowseButton_Click(this, new RoutedEventArgs());
                    break;
                case 3: // SteamGridDB button
                    SteamGridDbButton_Click(this, new RoutedEventArgs());
                    break;
                case 4: // Save button
                    SaveButton_Click(this, new RoutedEventArgs());
                    break;
                case 5: // Cancel button
                    CancelButton_Click(this, new RoutedEventArgs());
                    break;
            }
        }

        public void OnGamepadFocusReceived()
        {
            if (_gamepadNavigationService == null)
            {
                InitializeGamepadNavigationService();
            }

            _isFocused = true;
            UpdateFocusVisuals();
        }

        public void OnGamepadFocusLost()
        {
            _isFocused = false;
            UpdateFocusVisuals();
        }

        public void FocusLastElement()
        {
            // Focus the last element (element 5: Cancel button)
            _currentFocusedElement = 5;
            UpdateFocusVisuals();
        }

        private void UpdateFocusVisuals()
        {
            // Dispatch on UI thread to ensure bindings update reliably
            DispatcherQueue.TryEnqueue(() =>
            {
                OnPropertyChanged(nameof(BackButtonFocusBrush));
                OnPropertyChanged(nameof(DisplayNameFocusBrush));
                OnPropertyChanged(nameof(BrowseButtonFocusBrush));
                OnPropertyChanged(nameof(SgdbButtonFocusBrush));
                OnPropertyChanged(nameof(SaveButtonFocusBrush));
                OnPropertyChanged(nameof(CancelButtonFocusBrush));
            });
        }

        // Button click handlers
        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            // Same as Cancel - discard changes and go back
            CancelButton_Click(sender, e);
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

                // Fetch artwork options
                var results = await _artworkService.GetArtworkOptionsAsync(_currentGame.DisplayName, 10);

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
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentGame == null || _gameDatabase == null) return;

            try
            {
                // Validate display name
                var newDisplayName = DisplayNameTextBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(newDisplayName))
                {
                    ShowDisplayNameError("Display name cannot be empty");
                    return;
                }

                if (newDisplayName.Length > 100)
                {
                    ShowDisplayNameError("Display name must be 100 characters or less");
                    return;
                }

                HideDisplayNameError();

                // Update display name if changed
                if (newDisplayName != _originalDisplayName)
                {
                    _currentGame.DisplayName = newDisplayName;
                }

                // Handle artwork change
                if (_artworkChanged && !string.IsNullOrEmpty(_pendingArtworkPath))
                {
                    var extension = Path.GetExtension(_pendingArtworkPath);
                    var newFileName = $"{SanitizeFileName(_currentGame.ProcessName)}{extension}";
                    var newPath = Path.Combine(_artworkDirectory, newFileName);

                    // Delete old artwork if different path
                    if (!string.IsNullOrEmpty(_originalArtworkPath) &&
                        File.Exists(_originalArtworkPath) &&
                        _originalArtworkPath != newPath)
                    {
                        File.Delete(_originalArtworkPath);
                    }

                    // Copy new artwork (if not already in artwork folder)
                    if (_pendingArtworkPath != newPath)
                    {
                        File.Copy(_pendingArtworkPath, newPath, overwrite: true);
                    }

                    _currentGame.ArtworkPath = newPath;
                }

                // Save to database
                _gameDatabase.SaveGame(_currentGame);

                // Refresh the game in Library page before navigating back
                if (Application.Current is App app && app.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.RefreshLibraryGameArtwork(_currentGame.ProcessName);
                }

                // Cleanup temp SGDB files
                foreach (var tempPath in _tempSgdbPaths)
                {
                    if (File.Exists(tempPath) && tempPath != _pendingArtworkPath)
                    {
                        try { File.Delete(tempPath); } catch { }
                    }
                }

                // Navigate back to Library
                NavigateToLibrary();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GameSettingsPage: Error saving: {ex.Message}");
                ShowDisplayNameError("Failed to save changes");
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // Cleanup temp files
            foreach (var tempPath in _tempSgdbPaths)
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }

            // Navigate back to Library
            NavigateToLibrary();
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
