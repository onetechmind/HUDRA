using HUDRA.Extensions;
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
        private int _currentFocusedElement = 0; // 0=Back, 1=DisplayName, 2=Delete, 3=Browse, 4=SGDB, 5=Save, 6=Cancel
        private const int MaxFocusIndex = 6;

        // SGDB grid navigation state
        private bool _isSgdbGridActive = false;  // Whether we're navigating in the SGDB grid
        private int _sgdbGridFocusIndex = 0;     // Currently focused tile in the grid (0-9)

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

        public void Initialize(EnhancedGameDatabase gameDatabase, SteamGridDbArtworkService? artworkService)
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
        }

        // IGamepadNavigable implementation
        public bool CanNavigateUp => _isSgdbGridActive || _currentFocusedElement > 0;
        public bool CanNavigateDown => _isSgdbGridActive || _currentFocusedElement < MaxFocusIndex
            || ((_currentFocusedElement == 5 || _currentFocusedElement == 6)
                && SgdbResultsSection?.Visibility == Visibility.Visible
                && SgdbImageGrid?.Items.Count > 0);
        public bool CanNavigateLeft => _isSgdbGridActive || _currentFocusedElement == 6 || _currentFocusedElement == 2;
        public bool CanNavigateRight => _isSgdbGridActive || _currentFocusedElement == 5 || _currentFocusedElement == 1;
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

        public Brush DeleteButtonFocusBrush
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

        public Brush BrowseButtonFocusBrush
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

        public Brush SgdbButtonFocusBrush
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

        public Brush SaveButtonFocusBrush
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true
                    && _currentFocusedElement == 5 && !_isSgdbGridActive)
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
                    && _currentFocusedElement == 6 && !_isSgdbGridActive)
                {
                    return new SolidColorBrush(Colors.DarkViolet);
                }
                return new SolidColorBrush(Colors.Transparent);
            }
        }

        // Gamepad navigation handlers
        public void OnGamepadNavigateUp()
        {
            if (_isSgdbGridActive)
            {
                NavigateSgdbGrid("Up");
                return;
            }

            // Normal navigation - UP always goes to previous element (no grid entry from UP)
            if (_currentFocusedElement > 0)
            {
                _currentFocusedElement--;
                UpdateFocusVisuals();
            }
        }

        public void OnGamepadNavigateDown()
        {
            if (_isSgdbGridActive)
            {
                NavigateSgdbGrid("Down");
                return;
            }

            // If at Save (5) or Cancel (6) and SGDB grid is visible, enter grid at top
            if ((_currentFocusedElement == 5 || _currentFocusedElement == 6)
                && SgdbResultsSection.Visibility == Visibility.Visible
                && SgdbImageGrid.Items.Count > 0)
            {
                EnterSgdbGridNavigation();
                return;
            }

            // Normal navigation - go to next element
            if (_currentFocusedElement < MaxFocusIndex)
            {
                _currentFocusedElement++;
                UpdateFocusVisuals();
            }
        }

        public void OnGamepadNavigateLeft()
        {
            if (_isSgdbGridActive)
            {
                NavigateSgdbGrid("Left");
                return;
            }

            // From Cancel to Save, or from Delete to DisplayName
            if (_currentFocusedElement == 6)
            {
                _currentFocusedElement = 5;
                UpdateFocusVisuals();
            }
            else if (_currentFocusedElement == 2)
            {
                _currentFocusedElement = 1;
                UpdateFocusVisuals();
            }
        }

        public void OnGamepadNavigateRight()
        {
            if (_isSgdbGridActive)
            {
                NavigateSgdbGrid("Right");
                return;
            }

            // From Save to Cancel, or from DisplayName to Delete
            if (_currentFocusedElement == 5)
            {
                _currentFocusedElement = 6;
                UpdateFocusVisuals();
            }
            else if (_currentFocusedElement == 1)
            {
                _currentFocusedElement = 2;
                UpdateFocusVisuals();
            }
        }

        public void OnGamepadActivate()
        {
            if (_isSgdbGridActive)
            {
                ActivateSgdbGridTile();
                return;
            }

            switch (_currentFocusedElement)
            {
                case 0: // Back button
                    BackButton_Click(this, new RoutedEventArgs());
                    break;
                case 1: // Display name TextBox
                    DisplayNameTextBox.Focus(FocusState.Programmatic);
                    DisplayNameTextBox.SelectAll();
                    break;
                case 2: // Delete button
                    DeleteButton_Click(this, new RoutedEventArgs());
                    break;
                case 3: // Browse button
                    BrowseButton_Click(this, new RoutedEventArgs());
                    break;
                case 4: // SteamGridDB button
                    SteamGridDbButton_Click(this, new RoutedEventArgs());
                    break;
                case 5: // Save button
                    SaveButton_Click(this, new RoutedEventArgs());
                    break;
                case 6: // Cancel button
                    CancelButton_Click(this, new RoutedEventArgs());
                    break;
            }
        }

        public void OnGamepadBack()
        {
            // B button anywhere on page navigates back (same as Cancel)
            BackButton_Click(this, new RoutedEventArgs());
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
            // Focus the last element (element 6: Cancel button)
            _currentFocusedElement = MaxFocusIndex;
            UpdateFocusVisuals();
        }

        private void UpdateFocusVisuals()
        {
            // Dispatch on UI thread to ensure bindings update reliably
            DispatcherQueue.TryEnqueue(() =>
            {
                OnPropertyChanged(nameof(BackButtonFocusBrush));
                OnPropertyChanged(nameof(DisplayNameFocusBrush));
                OnPropertyChanged(nameof(DeleteButtonFocusBrush));
                OnPropertyChanged(nameof(BrowseButtonFocusBrush));
                OnPropertyChanged(nameof(SgdbButtonFocusBrush));
                OnPropertyChanged(nameof(SaveButtonFocusBrush));
                OnPropertyChanged(nameof(CancelButtonFocusBrush));

                // Scroll the focused element into view
                ScrollCurrentElementIntoView();
            });
        }

        private void ScrollCurrentElementIntoView()
        {
            FrameworkElement? elementToScroll = _currentFocusedElement switch
            {
                0 => BackButton,
                1 => DisplayNameTextBox,
                2 => DeleteButton,
                3 => BrowseButton,
                4 => SteamGridDbButton,
                5 => SaveButton,
                6 => CancelButton,
                _ => null
            };

            if (elementToScroll != null)
            {
                elementToScroll.StartBringIntoView(new BringIntoViewOptions
                {
                    AnimationDesired = true,
                    VerticalAlignmentRatio = 0.5 // Center the element vertically
                });
            }
        }

        // Button click handlers
        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            // Same as Cancel - discard changes and go back
            CancelButton_Click(sender, e);
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
                PrimaryButtonText = "Ⓐ Yes",
                CloseButtonText = "Ⓑ No",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            var result = mainWindow != null
                ? await dialog.ShowWithGamepadSupportAsync(mainWindow.GamepadNavigationService)
                : await dialog.ShowAsync();

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
                    XamlRoot = this.XamlRoot
                };
                await errorDialog.ShowAsync();
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

        // SGDB Grid Navigation Methods
        private void EnterSgdbGridNavigation()
        {
            _isSgdbGridActive = true;
            _sgdbGridFocusIndex = 0;
            UpdateFocusVisuals(); // Clear button focus visuals
            UpdateSgdbGridFocusVisual();
        }

        private void ExitSgdbGridNavigation()
        {
            _isSgdbGridActive = false;
            ClearSgdbGridFocusVisual();
            UpdateFocusVisuals();
        }

        private void NavigateSgdbGrid(string direction)
        {
            var items = SgdbImageGrid.Items;
            if (items == null || items.Count == 0) return;

            int columns = 2;
            int currentRow = _sgdbGridFocusIndex / columns;
            int currentCol = _sgdbGridFocusIndex % columns;

            switch (direction)
            {
                case "Up":
                    if (currentRow > 0)
                    {
                        _sgdbGridFocusIndex -= columns;
                        UpdateSgdbGridFocusVisual();
                    }
                    else
                    {
                        // Exit to Save button (index 5) - grid is below Save/Cancel visually
                        _currentFocusedElement = 5;
                        ExitSgdbGridNavigation();
                    }
                    break;
                case "Down":
                    // Move down within grid if possible, otherwise dead end (no exit)
                    if (_sgdbGridFocusIndex + columns < items.Count)
                    {
                        _sgdbGridFocusIndex += columns;
                        UpdateSgdbGridFocusVisual();
                    }
                    // Dead end - do nothing at bottom row
                    break;
                case "Left":
                    if (currentCol > 0)
                    {
                        _sgdbGridFocusIndex--;
                        UpdateSgdbGridFocusVisual();
                    }
                    break;
                case "Right":
                    if (currentCol < columns - 1 && _sgdbGridFocusIndex + 1 < items.Count)
                    {
                        _sgdbGridFocusIndex++;
                        UpdateSgdbGridFocusVisual();
                    }
                    break;
            }
        }

        private void UpdateSgdbGridFocusVisual()
        {
            if (SgdbImageGrid.ItemsSource == null) return;

            // Update visual feedback for all tiles
            for (int i = 0; i < SgdbImageGrid.Items.Count; i++)
            {
                var container = SgdbImageGrid.ContainerFromIndex(i) as FrameworkElement;
                if (container == null) continue;

                var border = FindVisualChild<Border>(container);
                if (border == null) continue;

                var button = FindVisualChild<Button>(container);
                if (button?.Tag is SteamGridDbResult result)
                {
                    bool isSelected = result.TempFilePath == _selectedSgdbPath;
                    bool isFocused = _isSgdbGridActive && i == _sgdbGridFocusIndex;

                    if (isFocused)
                    {
                        // Gamepad focus - use DarkViolet with thicker border
                        border.BorderBrush = new SolidColorBrush(Colors.DarkViolet);
                        border.BorderThickness = new Thickness(3);

                        // Scroll the focused tile into view
                        container.StartBringIntoView(new BringIntoViewOptions
                        {
                            AnimationDesired = true,
                            VerticalAlignmentRatio = 0.5
                        });
                    }
                    else if (isSelected)
                    {
                        // Selected but not focused
                        border.BorderBrush = new SolidColorBrush(Colors.DarkViolet);
                        border.BorderThickness = new Thickness(3);
                    }
                    else
                    {
                        // Neither focused nor selected
                        border.BorderBrush = new SolidColorBrush(Colors.Transparent);
                        border.BorderThickness = new Thickness(2);
                    }
                }
            }
        }

        private void ClearSgdbGridFocusVisual()
        {
            // Reset to selection-only state (reuse existing method)
            UpdateSgdbTileSelection();
        }

        private void ActivateSgdbGridTile()
        {
            var items = SgdbImageGrid.Items;
            if (items == null || _sgdbGridFocusIndex >= items.Count) return;

            if (items[_sgdbGridFocusIndex] is SteamGridDbResult result)
            {
                // Reuse existing selection logic
                _pendingArtworkPath = result.TempFilePath;
                _artworkChanged = true;
                _selectedSgdbPath = result.TempFilePath;

                // Update preview
                ArtworkPreview.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
                    new Uri(result.TempFilePath));

                // Update visual feedback
                UpdateSgdbGridFocusVisual();
                HideArtworkError();
            }
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
