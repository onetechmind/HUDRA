using HUDRA.Configuration;
using HUDRA.Helpers;
using HUDRA.Services;
using HUDRA.Interfaces;
using HUDRA.AttachedProperties;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace HUDRA.Controls
{
    public sealed partial class ResolutionPickerControl : UserControl, INotifyPropertyChanged, IGamepadNavigable
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<ResolutionChangedEventArgs>? ResolutionChanged;
        public event EventHandler<RefreshRateChangedEventArgs>? RefreshRateChanged;

        private ResolutionService? _resolutionService;
        private ResolutionAutoSetManager? _resolutionAutoSetManager;
        private RefreshRateAutoSetManager? _refreshRateAutoSetManager;
        private GamepadNavigationService? _gamepadNavigationService;
        private int _currentFocusedControl = 0; // 0 = Resolution, 1 = RefreshRate
        private bool _isFocused = false;

        private List<ResolutionService.Resolution> _availableResolutions = new();
        private List<int> _availableRefreshRates = new();
        private int _selectedResolutionIndex = 0;
        private int _selectedRefreshRateIndex = 0;

        private string _resolutionStatusText = "Resolution: Not Set";
        public string ResolutionStatusText
        {
            get => _resolutionStatusText;
            set
            {
                if (_resolutionStatusText != value)
                {
                    _resolutionStatusText = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _refreshRateStatusText = "Refresh Rate: Not Set";
        public string RefreshRateStatusText
        {
            get => _refreshRateStatusText;
            set
            {
                if (_refreshRateStatusText != value)
                {
                    _refreshRateStatusText = value;
                    OnPropertyChanged();
                }
            }
        }

        // IGamepadNavigable implementation
        public bool CanNavigateUp => false;
        public bool CanNavigateDown => false;
        public bool CanNavigateLeft => _currentFocusedControl == 1; // Can move left from RefreshRate to Resolution
        public bool CanNavigateRight => _currentFocusedControl == 0; // Can move right from Resolution to RefreshRate
        public bool CanActivate => true;
        public FrameworkElement NavigationElement => this;
        
        // Slider interface implementations - ResolutionPicker is not a slider control
        public bool IsSlider => false;
        public bool IsSliderActivated { get; set; } = false;
        public void AdjustSliderValue(int direction) { /* Not applicable */ }
        
        // ComboBox interface implementations - ResolutionPicker has ComboBoxes
        public bool HasComboBoxes => true;
        private bool _isComboBoxOpen = false;
        public bool IsComboBoxOpen 
        { 
            get => _isComboBoxOpen; 
            set => _isComboBoxOpen = value; 
        }
        
        public ComboBox? GetFocusedComboBox()
        {
            return _currentFocusedControl == 0 ? ResolutionComboBox : RefreshRateComboBox;
        }
        
        public int ComboBoxOriginalIndex { get; set; } = -1;
        public bool IsNavigatingComboBox { get; set; } = false;
        
        public void ProcessCurrentSelection()
        {
            // Process the current resolution selection
            if (_currentFocusedControl == 0 && ResolutionComboBox != null)
            {
                OnResolutionSelectionChanged(ResolutionComboBox, new Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs(new List<object>(), new List<object>()));
            }
            // Process the current refresh rate selection
            else if (_currentFocusedControl == 1 && RefreshRateComboBox != null)
            {
                OnRefreshRateSelectionChanged(RefreshRateComboBox, new Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs(new List<object>(), new List<object>()));
            }
        }

        public Brush FocusBorderBrush
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true)
                {
                    return new SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
                }
                return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        public Thickness FocusBorderThickness
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true)
                {
                    return new Thickness(2);
                }
                return new Thickness(0);
            }
        }

        public Brush ResolutionFocusBrush
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true && _currentFocusedControl == 0)
                {
                    return new SolidColorBrush(Microsoft.UI.Colors.MediumOrchid);
                }
                return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        public Brush RefreshRateFocusBrush
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true && _currentFocusedControl == 1)
                {
                    return new SolidColorBrush(Microsoft.UI.Colors.MediumOrchid);
                }
                return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }


        public ResolutionPickerControl()
        {
            this.InitializeComponent();
        }

        public void Initialize()
        {
            _resolutionService = new ResolutionService();

            _resolutionAutoSetManager = new ResolutionAutoSetManager(SetResolutionAsync, status => ResolutionStatusText = status);
            _refreshRateAutoSetManager = new RefreshRateAutoSetManager(SetRefreshRateAsync, status => RefreshRateStatusText = status);

            // Get gamepad service
            if (Application.Current is App app && app.MainWindow is MainWindow mainWindow)
            {
                _gamepadNavigationService = mainWindow.GamepadNavigationService;
            }

            InitializeResolutions();
            SetupEventHandlers();
            LoadCurrentSettings();
        }

        private void InitializeResolutions()
        {
            if (_resolutionService == null) return;

            _availableResolutions = _resolutionService.GetAvailableResolutions();

            if (_availableResolutions.Count == 0)
            {
                ResolutionStatusText = "No resolutions available";
                if (ResolutionComboBox != null)
                    ResolutionComboBox.IsEnabled = false;
                return;
            }

            if (ResolutionComboBox != null)
            {
                ResolutionComboBox.ItemsSource = _availableResolutions.Select(r => r.DisplayText).ToList();
                ResolutionComboBox.IsEnabled = true;
            }

            // Initialize with first resolution
            if (_availableResolutions.Count > 0)
            {
                UpdateRefreshRatesForResolution(0);
            }
        }

        private void SetupEventHandlers()
        {
            if (ResolutionComboBox != null)
            {
                ResolutionComboBox.SelectionChanged += OnResolutionSelectionChanged;
                ResolutionComboBox.DropDownOpened += (s, e) => { IsComboBoxOpen = true; };
                ResolutionComboBox.DropDownClosed += (s, e) => { IsComboBoxOpen = false; };
            }

            if (RefreshRateComboBox != null)
            {
                RefreshRateComboBox.SelectionChanged += OnRefreshRateSelectionChanged;
                RefreshRateComboBox.DropDownOpened += (s, e) => { IsComboBoxOpen = true; };
                RefreshRateComboBox.DropDownClosed += (s, e) => { IsComboBoxOpen = false; };
            }
        }

        private void OnResolutionSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ResolutionComboBox?.SelectedIndex < 0 ||
                ResolutionComboBox.SelectedIndex >= _availableResolutions.Count ||
                _resolutionAutoSetManager == null)
                return;

            // Skip processing if we're just navigating items (not actually selecting)
            if (IsNavigatingComboBox)
            {
                System.Diagnostics.Debug.WriteLine($"🎮 Resolution navigation - skipping update for index: {ResolutionComboBox.SelectedIndex}");
                return;
            }

            _selectedResolutionIndex = ResolutionComboBox.SelectedIndex;
            UpdateRefreshRatesForResolution(_selectedResolutionIndex);

            var selectedResolution = _availableResolutions[_selectedResolutionIndex];
            ResolutionChanged?.Invoke(this, new ResolutionChangedEventArgs(selectedResolution, false));

            _resolutionAutoSetManager.ScheduleUpdate(_selectedResolutionIndex);
            System.Diagnostics.Debug.WriteLine($"🎮 Resolution actual selection - applying update for index: {_selectedResolutionIndex}");
        }

        private void OnRefreshRateSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RefreshRateComboBox?.SelectedIndex < 0 ||
                RefreshRateComboBox.SelectedIndex >= _availableRefreshRates.Count ||
                _refreshRateAutoSetManager == null)
                return;

            // Skip processing if we're just navigating items (not actually selecting)
            if (IsNavigatingComboBox)
            {
                System.Diagnostics.Debug.WriteLine($"🎮 RefreshRate navigation - skipping update for index: {RefreshRateComboBox.SelectedIndex}");
                return;
            }

            _selectedRefreshRateIndex = RefreshRateComboBox.SelectedIndex;

            var selectedRefreshRate = _availableRefreshRates[_selectedRefreshRateIndex];
            RefreshRateChanged?.Invoke(this, new RefreshRateChangedEventArgs(selectedRefreshRate, false));

            _refreshRateAutoSetManager.ScheduleUpdate(_selectedRefreshRateIndex);
            System.Diagnostics.Debug.WriteLine($"🎮 RefreshRate actual selection - applying update for index: {_selectedRefreshRateIndex}");
        }

        private void UpdateRefreshRatesForResolution(int resolutionIndex)
        {
            if (_resolutionService == null || resolutionIndex < 0 || resolutionIndex >= _availableResolutions.Count)
                return;

            // Remember the current refresh rate value (not index)
            int currentRefreshRateValue = 0;
            if (_availableRefreshRates.Count > 0 && _selectedRefreshRateIndex >= 0 && _selectedRefreshRateIndex < _availableRefreshRates.Count)
            {
                currentRefreshRateValue = _availableRefreshRates[_selectedRefreshRateIndex];
            }

            var selectedResolution = _availableResolutions[resolutionIndex];
            _availableRefreshRates = _resolutionService.GetAvailableRefreshRates(selectedResolution);

            if (RefreshRateComboBox != null)
            {
                RefreshRateComboBox.ItemsSource = _availableRefreshRates.Select(rate => $"{rate}Hz").ToList();

                if (_availableRefreshRates.Count > 0)
                {
                    // Try to find the previous refresh rate in the new list
                    int newRefreshRateIndex = -1;
                    if (currentRefreshRateValue > 0)
                    {
                        newRefreshRateIndex = _availableRefreshRates.FindIndex(rate => rate == currentRefreshRateValue);
                    }

                    // If previous refresh rate is available, use it; otherwise use the highest available
                    if (newRefreshRateIndex >= 0)
                    {
                        _selectedRefreshRateIndex = newRefreshRateIndex;
                        RefreshRateComboBox.SelectedIndex = newRefreshRateIndex;
                        RefreshRateStatusText = $"Refresh Rate: {currentRefreshRateValue}Hz (preserved)";
                    }
                    else
                    {
                        // Fall back to the highest refresh rate available
                        var highestRefreshRate = _availableRefreshRates.Max();
                        var highestIndex = _availableRefreshRates.FindIndex(rate => rate == highestRefreshRate);

                        _selectedRefreshRateIndex = highestIndex >= 0 ? highestIndex : 0;
                        RefreshRateComboBox.SelectedIndex = _selectedRefreshRateIndex;

                        if (currentRefreshRateValue > 0)
                        {
                            RefreshRateStatusText = $"Refresh Rate: {_availableRefreshRates[_selectedRefreshRateIndex]}Hz (adjusted from {currentRefreshRateValue}Hz)";
                        }
                        else
                        {
                            RefreshRateStatusText = $"Refresh Rate: {_availableRefreshRates[_selectedRefreshRateIndex]}Hz";
                        }
                    }

                    RefreshRateComboBox.IsEnabled = true;
                }
                else
                {
                    RefreshRateComboBox.IsEnabled = false;
                    RefreshRateStatusText = "No refresh rates available";
                }
            }
        }

        private void LoadCurrentSettings()
        {
            if (_resolutionService == null) return;

            try
            {
                // Load current resolution
                var currentRes = _resolutionService.GetCurrentResolution();
                if (currentRes.Success)
                {
                    var match = _availableResolutions.FindIndex(r =>
                        r.Width == currentRes.CurrentResolution.Width &&
                        r.Height == currentRes.CurrentResolution.Height);

                    if (match >= 0)
                    {
                        _selectedResolutionIndex = match;
                        if (ResolutionComboBox != null)
                            ResolutionComboBox.SelectedIndex = match;
                        UpdateRefreshRatesForResolution(match);
                    }

                    ResolutionStatusText = $"Resolution: {currentRes.CurrentResolution.DisplayText}";
                }
                else
                {
                    ResolutionStatusText = $"Resolution Error: {currentRes.Message}";
                }

                // Load current refresh rate
                var currentRefreshRate = _resolutionService.GetCurrentRefreshRate();
                if (currentRefreshRate.Success)
                {
                    var refreshMatch = _availableRefreshRates.FindIndex(rate => rate == currentRefreshRate.RefreshRate);
                    if (refreshMatch >= 0)
                    {
                        _selectedRefreshRateIndex = refreshMatch;
                        if (RefreshRateComboBox != null)
                            RefreshRateComboBox.SelectedIndex = refreshMatch;
                    }

                    RefreshRateStatusText = $"Refresh Rate: {currentRefreshRate.RefreshRate}Hz";
                }
                else
                {
                    RefreshRateStatusText = $"Refresh Rate Error: {currentRefreshRate.Message}";
                }
            }
            catch (Exception ex)
            {
                ResolutionStatusText = $"Error loading settings: {ex.Message}";
            }
        }

        private async Task<bool> SetResolutionAsync(int resolutionIndex)
        {
            try
            {
                if (_resolutionService == null || resolutionIndex < 0 || resolutionIndex >= _availableResolutions.Count)
                    return false;

                var targetResolution = _availableResolutions[resolutionIndex];

                // Use current refresh rate if available
                int refreshRate = targetResolution.RefreshRate;
                if (_availableRefreshRates != null &&
                    _selectedRefreshRateIndex >= 0 &&
                    _selectedRefreshRateIndex < _availableRefreshRates.Count)
                {
                    refreshRate = _availableRefreshRates[_selectedRefreshRateIndex];
                }

                var result = _resolutionService.SetRefreshRate(targetResolution, refreshRate);

                ResolutionStatusText = result.Success
                    ? $"Resolution: {targetResolution.DisplayText}"
                    : $"Resolution Error: {result.Message}";

                if (result.Success)
                {
                    ResolutionChanged?.Invoke(this, new ResolutionChangedEventArgs(targetResolution, true));
                }

                return result.Success;
            }
            catch (Exception ex)
            {
                ResolutionStatusText = $"Resolution Error: {ex.Message}";
                return false;
            }
        }

        private async Task<bool> SetRefreshRateAsync(int refreshRateIndex)
        {
            try
            {
                if (_resolutionService == null || refreshRateIndex < 0 || refreshRateIndex >= _availableRefreshRates.Count ||
                    _selectedResolutionIndex < 0 || _selectedResolutionIndex >= _availableResolutions.Count)
                    return false;

                var targetRefreshRate = _availableRefreshRates[refreshRateIndex];
                var currentResolution = _availableResolutions[_selectedResolutionIndex];

                var result = _resolutionService.SetRefreshRate(currentResolution, targetRefreshRate);

                RefreshRateStatusText = result.Success
                    ? $"Refresh Rate: {targetRefreshRate}Hz"
                    : $"Refresh Rate Error: {result.Message}";

                if (result.Success)
                {
                    RefreshRateChanged?.Invoke(this, new RefreshRateChangedEventArgs(targetRefreshRate, true));
                }

                return result.Success;
            }
            catch (Exception ex)
            {
                RefreshRateStatusText = $"Refresh Rate Error: {ex.Message}";
                return false;
            }
        }


        public void Dispose()
        {
            _resolutionAutoSetManager?.Dispose();
            _refreshRateAutoSetManager?.Dispose();
        }

        // IGamepadNavigable event handlers
        public void OnGamepadNavigateUp() { }
        public void OnGamepadNavigateDown() { }
        
        public void OnGamepadNavigateLeft()
        {
            if (_currentFocusedControl == 1) // From RefreshRate to Resolution
            {
                _currentFocusedControl = 0;
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 ResolutionPicker: Moved left to Resolution ComboBox");
            }
        }

        public void OnGamepadNavigateRight()
        {
            if (_currentFocusedControl == 0) // From Resolution to RefreshRate
            {
                _currentFocusedControl = 1;
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 ResolutionPicker: Moved right to RefreshRate ComboBox");
            }
        }

        public void OnGamepadActivate()
        {
            // Open the currently focused ComboBox
            if (_currentFocusedControl == 0 && ResolutionComboBox != null)
            {
                ResolutionComboBox.IsDropDownOpen = true;
                System.Diagnostics.Debug.WriteLine($"🎮 ResolutionPicker: Opened Resolution ComboBox");
            }
            else if (_currentFocusedControl == 1 && RefreshRateComboBox != null)
            {
                RefreshRateComboBox.IsDropDownOpen = true;
                System.Diagnostics.Debug.WriteLine($"🎮 ResolutionPicker: Opened RefreshRate ComboBox");
            }
        }

        public void OnGamepadFocusReceived()
        {
            _isFocused = true;
            _currentFocusedControl = 0; // Start with Resolution ComboBox
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 ResolutionPicker: Received gamepad focus");
        }

        public void OnGamepadFocusLost()
        {
            _isFocused = false;
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 ResolutionPicker: Lost gamepad focus");
        }

        public void FocusLastElement()
        {
            // Not used - ResolutionPickerControl is not in a NavigableExpander
        }

        private void UpdateFocusVisuals()
        {
            // Dispatch on UI thread to ensure bindings update reliably with gamepad navigation
            DispatcherQueue.TryEnqueue(() =>
            {
                OnPropertyChanged(nameof(FocusBorderBrush));
                OnPropertyChanged(nameof(FocusBorderThickness));
                OnPropertyChanged(nameof(ResolutionFocusBrush));
                OnPropertyChanged(nameof(RefreshRateFocusBrush));
            });
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // Event argument classes
    public class ResolutionChangedEventArgs : EventArgs
    {
        public ResolutionService.Resolution Resolution { get; }
        public bool IsApplied { get; }

        public ResolutionChangedEventArgs(ResolutionService.Resolution resolution, bool isApplied)
        {
            Resolution = resolution;
            IsApplied = isApplied;
        }
    }

    public class RefreshRateChangedEventArgs : EventArgs
    {
        public int RefreshRate { get; }
        public bool IsApplied { get; }

        public RefreshRateChangedEventArgs(int refreshRate, bool isApplied)
        {
            RefreshRate = refreshRate;
            IsApplied = isApplied;
        }
    }
}