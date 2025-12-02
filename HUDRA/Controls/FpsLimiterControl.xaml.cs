using HUDRA.Models;
using HUDRA.Services;
using HUDRA.Interfaces;
using HUDRA.AttachedProperties;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace HUDRA.Controls
{
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return (value is bool boolean && boolean) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return value is Visibility visibility && visibility == Visibility.Visible;
        }
    }

    public sealed partial class FpsLimiterControl : UserControl, INotifyPropertyChanged, IGamepadNavigable
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<FpsLimitChangedEventArgs>? FpsLimitChanged;

        private RtssFpsLimiterService? _fpsLimiterService;
        private HdrService? _hdrService;
        private FpsLimitSettings _fpsSettings = new();
        private bool _isRtssSupported = false;
        private bool _isRtssInstalled = false;
        private bool _isGameRunning = false;
        private GamepadNavigationService? _gamepadNavigationService;
        private bool _isFocused = false;
        private int _currentFocusedControl = 0; // 0 = FPS ComboBox, 1 = HDR Toggle

        // HDR fields
        private bool _isHdrSupported = false;
        private bool _isUpdatingHdrToggle = false;
        private bool _cachedHdrState = false;
        private DispatcherTimer? _hdrPollTimer;

        public FpsLimitSettings FpsSettings
        {
            get => _fpsSettings;
            set
            {
                if (_fpsSettings != value)
                {
                    _fpsSettings = value;
                    OnPropertyChanged();
                    UpdateUI();
                }
            }
        }

        public bool IsRtssSupported
        {
            get => _isRtssSupported;
            set
            {
                if (_isRtssSupported != value)
                {
                    _isRtssSupported = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsRtssNotFound));
                }
            }
        }

        public bool IsRtssInstalled
        {
            get => _isRtssInstalled;
            set
            {
                if (_isRtssInstalled != value)
                {
                    _isRtssInstalled = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsRtssNotInstalled));
                }
            }
        }

        public bool IsRtssNotFound
        {
            get => !_isRtssSupported;
        }

        public bool IsRtssNotInstalled
        {
            get => !_isRtssInstalled;
        }

        public bool IsGameRunning
        {
            get => _isGameRunning;
            set
            {
                if (_isGameRunning != value)
                {
                    _isGameRunning = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsHdrSupported
        {
            get => _isHdrSupported;
            private set
            {
                if (_isHdrSupported != value)
                {
                    _isHdrSupported = value;
                    OnPropertyChanged();
                }
            }
        }

        // Track which control was focused for cross-control navigation
        public int CurrentFocusedControl => _currentFocusedControl;

        // IGamepadNavigable implementation
        public bool CanNavigateUp => false; // Handled by hardcoded navigation
        public bool CanNavigateDown => false; // Handled by hardcoded navigation
        public bool CanNavigateLeft => _currentFocusedControl == 1; // Can move left from HDR to FPS
        public bool CanNavigateRight => _currentFocusedControl == 0; // Can move right from FPS to HDR
        public bool CanActivate => (_currentFocusedControl == 0 && IsRtssInstalled) || (_currentFocusedControl == 1 && IsHdrSupported);
        public FrameworkElement NavigationElement => this;

        // Slider interface implementations - FpsLimiter is not a slider control
        public bool IsSlider => false;
        public bool IsSliderActivated { get; set; } = false;
        public void AdjustSliderValue(int direction) { /* Not applicable */ }

        // ComboBox interface implementations - FpsLimiter has ComboBox (only when FPS is focused)
        public bool HasComboBoxes => _currentFocusedControl == 0;
        private bool _isComboBoxOpen = false;
        public bool IsComboBoxOpen
        {
            get => _isComboBoxOpen;
            set => _isComboBoxOpen = value;
        }

        public ComboBox? GetFocusedComboBox()
        {
            return _currentFocusedControl == 0 ? FpsLimitComboBox : null;
        }

        public int ComboBoxOriginalIndex { get; set; } = -1;
        public bool IsNavigatingComboBox { get; set; } = false;

        public void ProcessCurrentSelection()
        {
            // Process the current FPS limit selection
            if (_currentFocusedControl == 0 && FpsLimitComboBox != null)
            {
                OnFpsLimitChanged(FpsLimitComboBox, new Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs(new List<object>(), new List<object>()));
            }
        }

        public Brush FpsFocusBrush
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

        public Brush HdrFocusBrush
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

        // Keep for compatibility but use individual focus brushes now
        public Brush FocusBorderBrush
        {
            get
            {
                return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        public Thickness FocusBorderThickness
        {
            get
            {
                return new Thickness(0);
            }
        }

        public FpsLimiterControl()
        {
            this.InitializeComponent();
            this.DataContext = this;

            // Set initial installation status from cache (preloaded in App.xaml.cs)
            IsRtssInstalled = RtssFpsLimiterService.GetCachedInstallationStatus();
        }

        public async void Initialize(RtssFpsLimiterService fpsLimiterService, HdrService hdrService)
        {
            _fpsLimiterService = fpsLimiterService;
            _hdrService = hdrService;

            // Get gamepad service
            if (Application.Current is App app && app.MainWindow is MainWindow mainWindow)
            {
                _gamepadNavigationService = mainWindow.GamepadNavigationService;
            }

            // Set up ComboBox event handlers for dropdown state tracking
            if (FpsLimitComboBox != null)
            {
                FpsLimitComboBox.DropDownOpened += (s, e) => { IsComboBoxOpen = true; };
                FpsLimitComboBox.DropDownClosed += (s, e) => { IsComboBoxOpen = false; };
            }

            // Only check running status - installation status already set in constructor from cache
            if (_fpsLimiterService != null)
            {
                try
                {
                    var detection = await _fpsLimiterService.DetectRtssInstallationAsync();
                    IsRtssSupported = detection.IsInstalled && detection.IsRunning;

                    if (detection.IsInstalled)
                    {
                        _fpsSettings.IsRtssAvailable = detection.IsRunning;
                        _fpsSettings.RtssInstallPath = detection.InstallPath;
                        _fpsSettings.RtssVersion = detection.Version;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to check RTSS running status: {ex.Message}");
                    IsRtssSupported = false;
                }
            }

            // Initialize HDR
            LoadHdrState();
            StartHdrPolling();
        }

        private void LoadHdrState()
        {
            if (_hdrService == null) return;

            try
            {
                _isUpdatingHdrToggle = true;

                IsHdrSupported = _hdrService.IsHdrSupported();
                _cachedHdrState = _hdrService.IsHdrEnabled();

                if (HdrToggle != null)
                {
                    HdrToggle.IsOn = _cachedHdrState;
                }

                _isUpdatingHdrToggle = false;

                System.Diagnostics.Debug.WriteLine($"FpsLimiterControl: HDR Loaded - Supported={IsHdrSupported}, Enabled={_cachedHdrState}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FpsLimiterControl: Failed to load HDR state: {ex.Message}");
                _isUpdatingHdrToggle = false;
            }
        }

        private void StartHdrPolling()
        {
            _hdrPollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _hdrPollTimer.Tick += OnHdrPollTimerTick;
            _hdrPollTimer.Start();
        }

        private void OnHdrPollTimerTick(object? sender, object e)
        {
            if (_hdrService == null || _isUpdatingHdrToggle) return;

            try
            {
                bool currentHdrState = _hdrService.IsHdrEnabled();

                // If state changed externally, update the toggle
                if (currentHdrState != _cachedHdrState)
                {
                    _isUpdatingHdrToggle = true;
                    _cachedHdrState = currentHdrState;

                    if (HdrToggle != null)
                    {
                        HdrToggle.IsOn = currentHdrState;
                    }

                    _isUpdatingHdrToggle = false;

                    System.Diagnostics.Debug.WriteLine($"FpsLimiterControl: External HDR change detected - now {(currentHdrState ? "enabled" : "disabled")}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FpsLimiterControl: HDR poll error: {ex.Message}");
            }
        }

        private void OnHdrToggled(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingHdrToggle || _hdrService == null || HdrToggle == null)
                return;

            try
            {
                bool desiredState = HdrToggle.IsOn;
                bool success = _hdrService.SetHdrEnabled(desiredState);

                if (success)
                {
                    _cachedHdrState = desiredState;
                    System.Diagnostics.Debug.WriteLine($"FpsLimiterControl: HDR {(desiredState ? "enabled" : "disabled")} successfully");
                }
                else
                {
                    // Revert toggle on failure
                    _isUpdatingHdrToggle = true;
                    HdrToggle.IsOn = !HdrToggle.IsOn;
                    _isUpdatingHdrToggle = false;

                    System.Diagnostics.Debug.WriteLine("FpsLimiterControl: Failed to change HDR state, reverted toggle");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FpsLimiterControl: Failed to toggle HDR: {ex.Message}");

                // Revert toggle on error
                _isUpdatingHdrToggle = true;
                HdrToggle.IsOn = !HdrToggle.IsOn;
                _isUpdatingHdrToggle = false;
            }
        }

        public void Dispose()
        {
            if (_hdrPollTimer != null)
            {
                _hdrPollTimer.Stop();
                _hdrPollTimer.Tick -= OnHdrPollTimerTick;
                _hdrPollTimer = null;
            }
        }

        /// <summary>
        /// Sets which control should be focused when this control receives gamepad focus.
        /// Called by ResolutionPickerControl to indicate whether we came from Resolution (0) or Refresh Rate (1).
        /// </summary>
        public void SetInitialFocusedControl(int controlIndex)
        {
            _currentFocusedControl = Math.Clamp(controlIndex, 0, 1);
        }

        public async Task RefreshRtssStatus()
        {
            if (_fpsLimiterService == null)
                return;

            try
            {
                var detection = await _fpsLimiterService.DetectRtssInstallationAsync(forceRefresh: true);
                IsRtssInstalled = detection.IsInstalled;
                IsRtssSupported = detection.IsInstalled && detection.IsRunning;

                if (detection.IsInstalled)
                {
                    _fpsSettings.IsRtssAvailable = detection.IsRunning;
                    _fpsSettings.RtssInstallPath = detection.InstallPath;
                    _fpsSettings.RtssVersion = detection.Version;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("RTSS not detected");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to refresh RTSS status: {ex.Message}");
                IsRtssInstalled = false;
                IsRtssSupported = false;
            }
        }

        public async Task SmartRefreshRtssStatus()
        {
            if (_fpsLimiterService == null)
                return;

            try
            {
                var detection = await _fpsLimiterService.SmartRefreshRtssStatusAsync();
                var newIsRtssInstalled = detection.IsInstalled;
                var newIsRtssSupported = detection.IsInstalled && detection.IsRunning;

                // Only update installation status if it actually changed
                if (_isRtssInstalled != newIsRtssInstalled)
                {
                    IsRtssInstalled = newIsRtssInstalled;
                    System.Diagnostics.Debug.WriteLine($"RTSS installation status changed to: {newIsRtssInstalled}");
                }

                // Only update running status if it actually changed
                if (_isRtssSupported != newIsRtssSupported)
                {
                    IsRtssSupported = newIsRtssSupported;
                    System.Diagnostics.Debug.WriteLine($"RTSS running status changed to: {newIsRtssSupported}");
                }

                if (detection.IsInstalled)
                {
                    _fpsSettings.IsRtssAvailable = detection.IsRunning;
                    _fpsSettings.RtssInstallPath = detection.InstallPath;
                    _fpsSettings.RtssVersion = detection.Version;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to smart refresh RTSS status: {ex.Message}");
                // Only update if we're currently showing as installed/supported
                if (_isRtssInstalled)
                {
                    IsRtssInstalled = false;
                }
                if (_isRtssSupported)
                {
                    IsRtssSupported = false;
                }
            }
        }

        public void UpdateFpsOptions(List<int> fpsOptions)
        {
            if (_fpsSettings.AvailableFpsOptions != fpsOptions)
            {
                _fpsSettings.AvailableFpsOptions = fpsOptions;
                UpdateUI();
            }
        }

        private void UpdateUI()
        {
            if (FpsLimitComboBox != null && _fpsSettings.AvailableFpsOptions?.Count > 0)
            {
                // Format the FPS options for display - show "Unlimited" for 0, "X FPS" for others
                var formattedOptions = _fpsSettings.AvailableFpsOptions.Select(fps =>
                    fps == 0 ? "Unlimited" : $"{fps} FPS").ToList();
                FpsLimitComboBox.ItemsSource = formattedOptions;

                if (_fpsSettings.AvailableFpsOptions.Contains(_fpsSettings.SelectedFpsLimit))
                {
                    var selectedIndex = _fpsSettings.AvailableFpsOptions.IndexOf(_fpsSettings.SelectedFpsLimit);
                    FpsLimitComboBox.SelectedIndex = selectedIndex;
                }
                else if (_fpsSettings.AvailableFpsOptions.Count > 0)
                {
                    // Default to "Unlimited" (index 0) for new users
                    FpsLimitComboBox.SelectedIndex = 0;
                }
            }
        }


        private async void OnFpsLimitChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FpsLimitComboBox?.SelectedIndex >= 0 && FpsLimitComboBox.SelectedIndex < _fpsSettings.AvailableFpsOptions.Count)
            {
                // Skip processing if we're just navigating items (not actually selecting)
                if (IsNavigatingComboBox)
                {
                    System.Diagnostics.Debug.WriteLine($"🎮 FpsLimit navigation - skipping update for index: {FpsLimitComboBox.SelectedIndex}");
                    return;
                }

                var selectedFps = _fpsSettings.AvailableFpsOptions[FpsLimitComboBox.SelectedIndex];
                if (selectedFps != _fpsSettings.SelectedFpsLimit)
                {
                    _fpsSettings.SelectedFpsLimit = selectedFps;
                    OnPropertyChanged(nameof(FpsSettings));

                    // Always notify of the change - the handler will decide whether to apply
                    FpsLimitChanged?.Invoke(this, new FpsLimitChangedEventArgs(selectedFps));
                    System.Diagnostics.Debug.WriteLine($"🎮 FpsLimit actual selection - applying update for fps: {selectedFps}");
                }
            }
        }

        // IGamepadNavigable event handlers
        public void OnGamepadNavigateUp()
        {
            // Handled by GamepadNavigationService with hardcoded paths
        }

        public void OnGamepadNavigateDown()
        {
            // Handled by GamepadNavigationService with hardcoded paths
        }

        public void OnGamepadNavigateLeft()
        {
            if (_currentFocusedControl == 1)
            {
                _currentFocusedControl = 0;
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 FpsLimiter: Moved left to FPS ComboBox");
            }
        }

        public void OnGamepadNavigateRight()
        {
            if (_currentFocusedControl == 0)
            {
                _currentFocusedControl = 1;
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 FpsLimiter: Moved right to HDR Toggle");
            }
        }

        public void OnGamepadActivate()
        {
            if (_currentFocusedControl == 0)
            {
                // Activate FPS ComboBox
                if (FpsLimitComboBox != null && IsRtssInstalled)
                {
                    FpsLimitComboBox.IsDropDownOpen = true;
                    System.Diagnostics.Debug.WriteLine($"🎮 FpsLimiter: Opened FPS ComboBox dropdown");
                }
            }
            else
            {
                // Activate HDR Toggle
                if (HdrToggle != null && IsHdrSupported)
                {
                    HdrToggle.IsOn = !HdrToggle.IsOn;
                    System.Diagnostics.Debug.WriteLine($"🎮 FpsLimiter: Toggled HDR to {HdrToggle.IsOn}");
                }
            }
        }

        public void OnGamepadBack() { }

        public void OnGamepadFocusReceived()
        {
            // Lazy initialization of gamepad service if needed
            if (_gamepadNavigationService == null)
            {
                InitializeGamepadNavigationService();
            }

            _isFocused = true;
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 FpsLimiter: Received gamepad focus (control={_currentFocusedControl})");
        }

        public void OnGamepadFocusLost()
        {
            _isFocused = false;
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 FpsLimiter: Lost gamepad focus");
        }

        public void FocusLastElement()
        {
            // Focus HDR Toggle (rightmost element)
            _currentFocusedControl = 1;
            UpdateFocusVisuals();
        }

        private void UpdateFocusVisuals()
        {
            // Dispatch on UI thread to ensure bindings update reliably with gamepad navigation
            DispatcherQueue.TryEnqueue(() =>
            {
                OnPropertyChanged(nameof(FpsFocusBrush));
                OnPropertyChanged(nameof(HdrFocusBrush));
                OnPropertyChanged(nameof(FocusBorderBrush));
                OnPropertyChanged(nameof(FocusBorderThickness));
            });
        }

        private void InitializeGamepadNavigationService()
        {
            // Get gamepad navigation service from app
            if (Application.Current is App app && app.MainWindow is MainWindow mainWindow)
            {
                _gamepadNavigationService = mainWindow.GamepadNavigationService;
                System.Diagnostics.Debug.WriteLine($"🎮 FpsLimiter: Lazy-initialized gamepad navigation service");
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private async void OnMsiAfterburnerLinkClick(object sender, RoutedEventArgs e)
        {
            try
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri("https://www.msi.com/Landing/afterburner/graphics-cards"));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to open MSI Afterburner link: {ex.Message}");
            }
        }

        private async void OnRtssLinkClick(object sender, RoutedEventArgs e)
        {
            try
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri("https://www.guru3d.com/files-details/rtss-rivatuner-statistics-server-download.html"));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to open RTSS link: {ex.Message}");
            }
        }
    }

    public class FpsLimitChangedEventArgs : EventArgs
    {
        public int FpsLimit { get; }

        public FpsLimitChangedEventArgs(int fpsLimit)
        {
            FpsLimit = fpsLimit;
        }
    }
}
