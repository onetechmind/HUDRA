using HUDRA.AttachedProperties;
using HUDRA.Configuration;
using HUDRA.Helpers;
using HUDRA.Interfaces;
using HUDRA.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace HUDRA.Controls
{
    public sealed partial class TdpPickerControl : UserControl, INotifyPropertyChanged, IGamepadNavigable
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<int>? TdpChanged;

        // Dependencies
        private DpiScalingService? _dpiService;
        private NavigationService? _navigationService;
        private TdpAutoSetManager? _autoSetManager;
        private AudioHelper? _audioHelper;

        // Configuration
        private bool _autoSetEnabled = true;
        private bool _isInitialized = false;
        private bool _showLabel = true;

        // Data and State
        private readonly ObservableCollection<TdpItem> _tdpItems = new();
        private int _selectedTdp = HudraSettings.DEFAULT_STARTUP_TDP;
        private string _statusText = "Current TDP: Not Set";
        private int _lastCenteredTdp = -1;
        private bool _suppressSelectionEvents = false;

        // Scrolling state
        private bool _isScrolling = false;
        private DispatcherTimer? _scrollEndTimer;

        //Mouse drag state
        private bool _isMouseDragging = false;
        private bool _hasPointerCapture = false;
        private double _dragStartX = 0;
        private double _dragStartScrollOffset = 0;
        private const double DRAG_THRESHOLD = 5; // pixels before we consider it a drag

        // Gamepad navigation fields
        private GamepadNavigationService? _gamepadNavigationService;
        private bool _isFocused = false;

        #region Public Properties

        public bool ShowLabel
        {
            get => _showLabel;
            set
            {
                if (_showLabel != value)
                {
                    _showLabel = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LabelVisibility));
                }
            }
        }

        public Visibility LabelVisibility => ShowLabel ? Visibility.Visible : Visibility.Collapsed;

        public string StatusText
        {
            get => _statusText;
            set
            {
                if (_statusText != value)
                {
                    _statusText = value;
                    OnPropertyChanged();
                }
            }
        }

        public int SelectedTdp
        {
            get => _selectedTdp;
            set
            {
                if (_selectedTdp != value && value >= HudraSettings.MIN_TDP && value <= HudraSettings.MAX_TDP)
                {
                    var oldValue = _selectedTdp;
                    _selectedTdp = value;

                    if (_isInitialized && !_suppressSelectionEvents)
                    {
                        UpdateSelection(oldValue, value);
                        ScrollToSelectedItem();
                        TdpChanged?.Invoke(this, value);
                    }

                    OnPropertyChanged();
                }
            }
        }

        public Border PickerBorder => TdpPickerBorder;

        // Focus properties for XAML binding
        public bool IsFocused
        {
            get => _isFocused;
            set
            {
                if (_isFocused != value)
                {
                    _isFocused = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FocusBorderBrush));
                    OnPropertyChanged(nameof(FocusBorderThickness));
                }
            }
        }

        public Brush FocusBorderBrush
        {
            get
            {
                // If not focused or gamepad not active, no border
                if (!IsFocused || _gamepadNavigationService?.IsGamepadActive != true || !_autoSetEnabled)
                    return new SolidColorBrush(Microsoft.UI.Colors.Transparent);

                // If focused, show DarkViolet
                return new SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
            }
        }

        public Thickness FocusBorderThickness => (IsFocused && _gamepadNavigationService?.IsGamepadActive == true && _autoSetEnabled)
            ? new Thickness(3)
            : new Thickness(0);

        #endregion

        public TdpPickerControl()
        {
            this.InitializeComponent();
            this.DataContext = this; // Required for {Binding} to work
            InitializeData();
            this.Loaded += TdpPickerControl_Loaded;
        }

        private void TdpPickerControl_Loaded(object sender, RoutedEventArgs e)
        {
            // When control is loaded (especially after being in a collapsed expander),
            // ensure scroll position is correct
            if (_isInitialized)
            {
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    ScrollToSelectedItem();
                });
            }
        }

        #region Initialization

        private void InitializeData()
        {

            _tdpItems.Clear();

            for (int tdp = HudraSettings.MIN_TDP; tdp <= HudraSettings.MAX_TDP; tdp++)
            {
                var item = new TdpItem(tdp);
                if (tdp == _selectedTdp)
                {
                    item.IsSelected = true;
                }
                _tdpItems.Add(item);
            }

            TdpItemsRepeater.ItemsSource = _tdpItems;
        }

        public void Initialize(DpiScalingService dpiService, bool autoSetEnabled = true, bool preserveCurrentValue = false)
        {
            _dpiService = dpiService ?? throw new ArgumentNullException(nameof(dpiService));
            _autoSetEnabled = autoSetEnabled;
            _autoSetManager = autoSetEnabled ? new TdpAutoSetManager(SetTdpAsync, status => StatusText = status) : null;

            // Get navigation service from app
            if (Application.Current is App app && app.MainWindow is MainWindow mainWindow)
            {
                _navigationService = mainWindow.NavigationService;
            }

            // Initialize audio helper
            try
            {
                _audioHelper = new AudioHelper();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize audio helper: {ex.Message}");
            }

            SetupMouseDragSupport();
            _isInitialized = true;

            if (!preserveCurrentValue)
            {
                LoadCurrentTdp();
            }
            else
            {
                // For preserved value, just update visuals
                UpdateSelection(-1, _selectedTdp);
                StatusText = $"Current TDP: {_selectedTdp}W (preserved)";

                // Ensure proper positioning after layout
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    ScrollToSelectedItem();
                });
            }

            // Initialize gamepad navigation after _autoSetEnabled is properly set
            InitializeGamepadNavigation();

            _lastCenteredTdp = _selectedTdp;
        }

        private async void LoadCurrentTdp()
        {
            if (!_autoSetEnabled)
            {
                // Settings mode - use startup TDP from settings
                int startupTdp = SettingsService.GetStartupTdp();
                SelectedTdp = startupTdp;
                StatusText = $"Default TDP: {startupTdp}W";
                ScrollToSelectedItem();
                return;
            }

            if (Application.Current is App app && app.StartupTdpAlreadyApplied)
            {
                System.Diagnostics.Debug.WriteLine("⚡ Startup TDP already applied during minimized launch - skipping duplicate application");

                // Still need to determine and show the correct TDP value in UI
                int currentTdp;
                string statusReason;

                if (SettingsService.GetUseStartupTdp())
                {
                    currentTdp = SettingsService.GetStartupTdp();
                    statusReason = "startup TDP (already applied)";
                }
                else
                {
                    currentTdp = SettingsService.GetLastUsedTdp();
                    if (currentTdp < HudraSettings.MIN_TDP || currentTdp > HudraSettings.MAX_TDP)
                    {
                        currentTdp = HudraSettings.DEFAULT_STARTUP_TDP;
                        statusReason = "fallback TDP (already applied)";
                    }
                    else
                    {
                        statusReason = "last-used TDP (already applied)";
                    }
                }

                SelectedTdp = currentTdp;
                StatusText = $"TDP: {currentTdp}W ({statusReason})";
                _lastCenteredTdp = _selectedTdp;
                return;
            }

            // Main page mode - implement priority order
            try
            {
                int targetTdp;
                string statusReason;

                // Priority 1: Default TDP if toggle is enabled
                if (SettingsService.GetUseStartupTdp())
                {
                    targetTdp = SettingsService.GetStartupTdp();
                    statusReason = "using default TDP from settings";
                }
                else
                {
                    // Priority 2: Last-Used TDP if default is disabled
                    targetTdp = SettingsService.GetLastUsedTdp();

                    // Priority 3: Fallback to 10W if last-used is invalid
                    if (targetTdp < HudraSettings.MIN_TDP || targetTdp > HudraSettings.MAX_TDP)
                    {
                        targetTdp = HudraSettings.DEFAULT_STARTUP_TDP;
                        statusReason = "using fallback (10W) - invalid last-used TDP";
                    }
                    else
                    {
                        statusReason = "using last-used TDP";
                    }
                }

                // Set the UI value FIRST
                SelectedTdp = targetTdp;

                // Try to read current hardware TDP for status display
                var tdpService = new TDPService();
                StatusText = $"TDP Service: {tdpService.InitializationStatus}";

                var currentHardwareResult = tdpService.GetCurrentTdp();
                if (currentHardwareResult.Success)
                {
                    StatusText = $"Startup TDP: {targetTdp}W ({statusReason}) | Hardware: {currentHardwareResult.TdpWatts}W";
                }
                else
                {
                    StatusText = $"Startup TDP: {targetTdp}W ({statusReason})";
                }

                // Force set the determined TDP to hardware
                DispatcherQueue.TryEnqueue(async () =>
                {
                    await Task.Delay(200);

                    using var setService = new TDPService();
                    var setResult = setService.SetTdp(targetTdp * 1000);

                    if (setResult.Success)
                    {
                        StatusText = $"TDP: {targetTdp}W ({statusReason}) - synchronized";
                        SettingsService.SetLastUsedTdp(targetTdp);
                    }
                    else
                    {
                        StatusText = $"TDP: {targetTdp}W ({statusReason}) - sync failed: {setResult.Message}";
                    }
                });

                tdpService.Dispose();
            }
            catch (Exception ex)
            {
                // Final fallback to 10W on any exception
                int fallbackTdp = HudraSettings.DEFAULT_STARTUP_TDP;
                SelectedTdp = fallbackTdp;
                StatusText = $"TDP: {fallbackTdp}W (fallback due to error): {ex.Message}";
            }

            _lastCenteredTdp = _selectedTdp;
        }

        #endregion

        #region Selection Management

        private void UpdateSelection(int oldValue, int newValue)
        {
            // Update old item
            if (oldValue >= HudraSettings.MIN_TDP && oldValue <= HudraSettings.MAX_TDP)
            {
                var oldIndex = oldValue - HudraSettings.MIN_TDP;
                if (oldIndex >= 0 && oldIndex < _tdpItems.Count)
                {
                    _tdpItems[oldIndex].IsSelected = false;
                }
            }

            // Update new item
            var newIndex = newValue - HudraSettings.MIN_TDP;
            if (newIndex >= 0 && newIndex < _tdpItems.Count)
            {
                _tdpItems[newIndex].IsSelected = true;
            }

            // Play audio feedback if value changed and we have audio
            if (oldValue != newValue && oldValue != -1 && _audioHelper != null)
            {
                _audioHelper.PlayTick();
            }
        }

        private void ScrollToSelectedItem()
        {
            if (!_isInitialized) return;

            var selectedIndex = _selectedTdp - HudraSettings.MIN_TDP;
            if (selectedIndex >= 0 && selectedIndex < _tdpItems.Count)
            {
                // Calculate the scroll position to center the selected item
                var itemWidth = 60.0; // From XAML template width
                var itemMargin = 6.0; // From XAML margin (each side)
                var totalItemWidth = itemWidth + (itemMargin * 2); // Total space per item including margins
                var containerMargin = 110.0; // From ItemsRepeater margin
                var viewportCenter = TdpScrollViewer.ViewportWidth / 2;

                // Position of the selected item's center
                var itemCenter = containerMargin + (selectedIndex * totalItemWidth) + (totalItemWidth / 2);

                // Target scroll position to center the item
                var targetScrollPosition = itemCenter - viewportCenter;

                // Clamp to valid scroll range
                var maxScroll = Math.Max(0, TdpScrollViewer.ExtentWidth - TdpScrollViewer.ViewportWidth);
                targetScrollPosition = Math.Max(0, Math.Min(maxScroll, targetScrollPosition));

                // Perform the scroll
                TdpScrollViewer.ScrollToHorizontalOffset(targetScrollPosition);
            }
        }

        private int GetCenteredTdpFromScroll()
        {
            var scrollOffset = TdpScrollViewer.HorizontalOffset;
            var viewportWidth = TdpScrollViewer.ViewportWidth;
            var centerPosition = scrollOffset + (viewportWidth / 2);

            // Calculate which item is closest to center
            var itemWidth = 60.0; // From XAML template width
            var itemMargin = 6.0; // From XAML margin (each side)
            var totalItemWidth = itemWidth + (itemMargin * 2); // Total space per item including margins
            var containerMargin = 110.0; // From ItemsRepeater margin

            var relativePosition = centerPosition - containerMargin - (totalItemWidth / 2);
            var itemIndex = Math.Round(relativePosition / totalItemWidth);

            itemIndex = Math.Max(0, Math.Min(itemIndex, _tdpItems.Count - 1));

            return (int)itemIndex + HudraSettings.MIN_TDP;
        }

        #endregion

        #region Event Handlers

        private void TdpScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (!_isInitialized) return;

            var centeredTdp = GetCenteredTdpFromScroll();

            // Update selection if centered item changed
            if (centeredTdp != _lastCenteredTdp && centeredTdp != _selectedTdp)
            {
                _suppressSelectionEvents = true;

                var oldSelected = _selectedTdp;
                _selectedTdp = centeredTdp;
                UpdateSelection(oldSelected, centeredTdp);
                OnPropertyChanged(nameof(SelectedTdp));

                // Play audio feedback
                if (_lastCenteredTdp != -1 && _audioHelper != null)
                {
                    _audioHelper.PlayTick();
                }

                _lastCenteredTdp = centeredTdp;
                _suppressSelectionEvents = false;

                // Schedule hardware changes if auto-set is enabled
                if (_autoSetEnabled && _autoSetManager != null)
                {
                    _autoSetManager.ScheduleUpdate(_selectedTdp);
                }

                TdpChanged?.Invoke(this, _selectedTdp);
            }

            // Handle scroll end detection for snapping
            if (!e.IsIntermediate)
            {
                // Scroll ended - ensure proper selection and centering
                EnsureProperSelection();
            }
            else
            {
                // Still scrolling - reset the scroll end timer
                _scrollEndTimer?.Stop();
                _scrollEndTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                _scrollEndTimer.Tick += (s, args) =>
                {
                    _scrollEndTimer.Stop();
                    EnsureProperSelection();
                };
                _scrollEndTimer.Start();
            }
        }

        private void EnsureProperSelection()
        {
            var centeredTdp = GetCenteredTdpFromScroll();
            if (centeredTdp != _selectedTdp)
            {
                SelectedTdp = centeredTdp;
            }

            // Ensure the selected item is properly centered
            ScrollToSelectedItem();
        }

        private void TdpScrollViewer_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var tapPosition = e.GetPosition(TdpScrollViewer);

            // Calculate which TDP item was tapped based on position
            var tappedTdp = GetTdpFromPosition(tapPosition.X);

            if (tappedTdp >= HudraSettings.MIN_TDP && tappedTdp <= HudraSettings.MAX_TDP)
            {
                SelectedTdp = tappedTdp;

                if (_autoSetEnabled && _autoSetManager != null)
                {
                    _autoSetManager.ScheduleUpdate(_selectedTdp);
                }

                e.Handled = true;
            }
        }

        // ADD this helper method:
        private int GetTdpFromPosition(double xPosition)
        {
            // Account for scroll offset
            var scrollOffset = TdpScrollViewer.HorizontalOffset;
            var absolutePosition = xPosition + scrollOffset;

            // Calculate which item was tapped using the same logic as GetCenteredTdpFromScroll
            var itemWidth = 60.0; // From XAML template width
            var itemMargin = 6.0; // From XAML margin (each side)
            var totalItemWidth = itemWidth + (itemMargin * 2); // Total space per item including margins
            var containerMargin = 110.0; // From ItemsRepeater margin

            // Calculate the item index based on absolute position
            var relativePosition = absolutePosition - containerMargin;
            var itemIndex = Math.Floor(relativePosition / totalItemWidth);

            // Clamp to valid range
            itemIndex = Math.Max(0, Math.Min(itemIndex, _tdpItems.Count - 1));

            return (int)itemIndex + HudraSettings.MIN_TDP;
        }

        #endregion

        #region Public Methods

        public void ChangeTdpBy(int delta)
        {
            var newTdp = Math.Max(HudraSettings.MIN_TDP, Math.Min(HudraSettings.MAX_TDP, _selectedTdp + delta));
            if (newTdp != _selectedTdp)
            {
                SelectedTdp = newTdp;

                if (_autoSetEnabled && _autoSetManager != null)
                {
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                    timer.Tick += (s, e) =>
                    {
                        timer.Stop();
                        _autoSetManager.ScheduleUpdate(_selectedTdp);
                    };
                    timer.Start();
                }
            }
        }

        public void EnsureScrollPositionAfterLayout()
        {
            if (!_isInitialized) return;

            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                ScrollToSelectedItem();
            });
        }

        public void ResetScrollPositioning()
        {
            // Not needed with ItemsRepeater approach, but keeping for compatibility
        }

        public void SetSelectedTdpWhenReady(int tdpValue)
        {
            if (_isInitialized)
            {
                SelectedTdp = tdpValue;
                ScrollToSelectedItem();
            }
            else
            {
                // Store the value and set it when initialization completes
                _selectedTdp = tdpValue;
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (_isInitialized)
                    {
                        UpdateSelection(-1, tdpValue);
                        ScrollToSelectedItem();
                    }
                });
            }
        }

        public void SetAudioFeedbackEnabled(bool enabled)
        {
            // Could store this as a flag and check it in UpdateSelection
        }

        public void SetAudioVolume(double volume)
        {
            _audioHelper?.SetVolume(volume);
        }

        #endregion

        #region Hardware Integration

        private async Task<bool> SetTdpAsync(int tdpValue)
        {
            try
            {
                var tdpService = new TDPService();
                int tdpInMilliwatts = tdpValue * 1000;
                var result = tdpService.SetTdp(tdpInMilliwatts);

                StatusText = result.Success
                    ? $"Current TDP: {tdpValue}W"
                    : $"Error: {result.Message}";

                if (result.Success)
                {
                    SettingsService.SetLastUsedTdp(tdpValue);
                }

                return result.Success;
            }
            catch (Exception ex)
            {
                StatusText = $"Error: {ex.Message}";
                return false;
            }
        }

        private void SetupMouseDragSupport()
        {
            TdpScrollViewer.PointerPressed += TdpScrollViewer_PointerPressed;
            TdpScrollViewer.PointerMoved += TdpScrollViewer_PointerMoved;
            TdpScrollViewer.PointerReleased += TdpScrollViewer_PointerReleased;
        }

        private void TdpScrollViewer_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse)
            {
                var point = e.GetCurrentPoint(TdpScrollViewer);
                if (point.Properties.IsLeftButtonPressed)
                {
                    _dragStartX = point.Position.X;
                    _dragStartScrollOffset = TdpScrollViewer.HorizontalOffset;
                    _isMouseDragging = false; // Will be set to true only after threshold
                    _hasPointerCapture = TdpScrollViewer.CapturePointer(e.Pointer);
                }
            }
        }

        private void TdpScrollViewer_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse && _hasPointerCapture)
            {
                var point = e.GetCurrentPoint(TdpScrollViewer);
                var deltaX = _dragStartX - point.Position.X; // Natural scrolling direction

                // Only start dragging if we've moved enough (prevents accidental drags)
                if (!_isMouseDragging && Math.Abs(deltaX) > DRAG_THRESHOLD)
                {
                    _isMouseDragging = true;
                }

                if (_isMouseDragging)
                {
                    var newOffset = _dragStartScrollOffset + deltaX;
                    // Use ChangeView with disableAnimation=true for smooth, immediate response
                    TdpScrollViewer.ChangeView(newOffset, null, null, true);
                    e.Handled = true; // Prevent other handlers from interfering
                }
            }
        }

        private void TdpScrollViewer_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_hasPointerCapture)
            {
                TdpScrollViewer.ReleasePointerCapture(e.Pointer);
                _hasPointerCapture = false;

                // If we were dragging, don't allow the tap to go through
                if (_isMouseDragging)
                {
                    e.Handled = true;
                }

                _isMouseDragging = false;
            }
        }

        #endregion

        #region Cleanup

        #region IGamepadNavigable Implementation

        public bool CanNavigateUp => false; // TDP picker only supports left/right navigation
        public bool CanNavigateDown => false;
        public bool CanNavigateLeft => true; // Always navigable
        public bool CanNavigateRight => true; // Always navigable
        public bool CanActivate => false; // TDP picker doesn't use activation

        public FrameworkElement NavigationElement => this;

        // Slider interface implementations - TDP picker is not a slider
        public bool IsSlider => false;
        public bool IsSliderActivated { get; set; } = false;
        public void AdjustSliderValue(int direction)
        {
            // Not used since IsSlider is false
        }
        
        // ComboBox interface implementations - TdpPicker has no ComboBoxes
        public bool HasComboBoxes => false;
        public bool IsComboBoxOpen { get; set; } = false;
        public ComboBox? GetFocusedComboBox() => null;
        public int ComboBoxOriginalIndex { get; set; } = -1;
        public bool IsNavigatingComboBox { get; set; } = false;
        public void ProcessCurrentSelection() { /* Not applicable - no ComboBoxes */ }

        public void OnGamepadNavigateUp() { } // Not applicable
        public void OnGamepadNavigateDown() { } // Not applicable

        public void OnGamepadNavigateLeft()
        {
            ChangeTdpBy(-1);
        }

        public void OnGamepadNavigateRight()
        {
            ChangeTdpBy(1);
        }

        public void OnGamepadActivate()
        {
            // Not used - TDP picker doesn't support activation
        }

        public void OnGamepadFocusReceived()
        {
            // Lazy initialization of gamepad service if needed
            if (_gamepadNavigationService == null)
            {
                InitializeGamepadNavigationService();
            }
            
            IsFocused = true;
        }

        public void OnGamepadFocusLost()
        {
            IsFocused = false;
        }

        public void FocusLastElement()
        {
            // Not used - TdpPickerControl is not in a NavigableExpander
        }

        public void SetInitialFocus()
        {
            // Set keyboard focus to the ScrollViewer when the page loads
            TdpScrollViewer.Focus(FocusState.Keyboard);
        }

        private void OnGamepadActiveStateChanged(object? sender, bool isActive)
        {
            // Update focus border properties when gamepad active state changes
            // Must run on UI thread since property getters create SolidColorBrush objects
            DispatcherQueue.TryEnqueue(() =>
            {
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
                
                // Subscribe to gamepad active state changes to update focus borders
                if (_gamepadNavigationService != null)
                {
                    _gamepadNavigationService.GamepadActiveStateChanged += OnGamepadActiveStateChanged;
                }
            }
        }

        private void InitializeGamepadNavigation()
        {
            // Only register as a navigable element when in main page mode
            // In settings mode, TdpSettingsControl handles the navigation
            if (_autoSetEnabled)
            {
                GamepadNavigation.SetIsEnabled(this, true);
                GamepadNavigation.SetNavigationGroup(this, "MainControls");
                GamepadNavigation.SetNavigationOrder(this, 1);
            }
        }


        #endregion

        public void Dispose()
        {
            _autoSetManager?.Dispose();
            _audioHelper?.Dispose();
            _scrollEndTimer?.Stop();

            TdpScrollViewer.PointerPressed -= TdpScrollViewer_PointerPressed;
            TdpScrollViewer.PointerMoved -= TdpScrollViewer_PointerMoved;
            TdpScrollViewer.PointerReleased -= TdpScrollViewer_PointerReleased;
        }

        #endregion

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}