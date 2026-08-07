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
        private bool _includeNoneOption = false;

        // Data and State
        private readonly ObservableCollection<TdpItem> _tdpItems = new();
        private int _selectedTdp = HudraSettings.DEFAULT_STARTUP_TDP;
        private string _statusText = "Current TDP: Not Set";
        private int _lastCenteredTdp = -1;
        private bool _suppressSelectionEvents = false;

        // Device-specific TDP range (populated from HardwareDetectionService at Initialize() time)
        private int _deviceMinTdp = HudraSettings.MIN_TDP;
        private int _deviceMaxTdp = HudraSettings.MAX_TDP;

        // Scrolling state
        private bool _isScrolling = false;
        private DispatcherTimer? _scrollEndTimer;
        private bool _isProgrammaticScroll = false; // True when scrolling programmatically (not user-initiated)
        private int _programmaticRetries = 0; // Re-center attempts after a programmatic scroll landed off-target

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

        /// <summary>
        /// When true, includes a "0" (None/Not Set) option at the beginning of the picker.
        /// This is used for game profiles where TDP can be unset.
        /// </summary>
        public bool IncludeNoneOption
        {
            get => _includeNoneOption;
            set
            {
                if (_includeNoneOption != value)
                {
                    _includeNoneOption = value;
                    OnPropertyChanged();
                    // Reinitialize data if already loaded
                    if (_tdpItems.Count > 0)
                    {
                        InitializeData();
                    }
                }
            }
        }

        /// <summary>
        /// The minimum valid TDP value for this picker instance.
        /// Returns 0 if IncludeNoneOption is true, otherwise MIN_TDP.
        /// </summary>
        private int EffectiveMinTdp => _includeNoneOption ? 0 : _deviceMinTdp;

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
                // Validate: value must be 0 (if IncludeNoneOption) or within device TDP range
                bool isValid = (_includeNoneOption && value == 0) ||
                               (value >= _deviceMinTdp && value <= _deviceMaxTdp);

                if (!isValid && _selectedTdp != value)
                {
                    // Never silent: a rejected set with stale limits was the
                    // invisible half of the "picker shows 30, hardware says 45"
                    // field bug.
                    DebugLogger.Log($"SelectedTdp={value}W rejected (limits {_deviceMinTdp}-{_deviceMaxTdp}W, current {_selectedTdp}W)", "TDP");
                }

                if (_selectedTdp != value && isValid)
                {
                    var oldValue = _selectedTdp;
                    _selectedTdp = value;

                    // Every accepted change is logged: the field 45W->10W state
                    // corruption had no visible writer. Low-frequency (wheel
                    // scrolling bypasses this setter), so no throttle needed.
                    DebugLogger.Log($"SelectedTdp {oldValue}W -> {value}W (via setter, init={_isInitialized})", "TDP");

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

            if (_includeNoneOption)
            {
                // Add 0 (Default/None) first, then skip 1-4 and add MIN_TDP through MAX_TDP
                var noneItem = new TdpItem(0);
                if (_selectedTdp == 0)
                {
                    noneItem.IsSelected = true;
                }
                _tdpItems.Add(noneItem);

                // Add valid TDP values starting from _deviceMinTdp
                for (int tdp = _deviceMinTdp; tdp <= _deviceMaxTdp; tdp++)
                {
                    var item = new TdpItem(tdp);
                    if (tdp == _selectedTdp)
                    {
                        item.IsSelected = true;
                    }
                    _tdpItems.Add(item);
                }
            }
            else
            {
                // No "None" option - start from _deviceMinTdp
                for (int tdp = _deviceMinTdp; tdp <= _deviceMaxTdp; tdp++)
                {
                    var item = new TdpItem(tdp);
                    if (tdp == _selectedTdp)
                    {
                        item.IsSelected = true;
                    }
                    _tdpItems.Add(item);
                }
            }

            TdpItemsRepeater.ItemsSource = _tdpItems;
        }

        /// <summary>
        /// Converts a TDP value to its index in the _tdpItems list.
        /// </summary>
        private int GetIndexFromTdp(int tdp)
        {
            if (_includeNoneOption)
            {
                if (tdp == 0) return 0;
                // TDP _deviceMinTdp is at index 1, next is at index 2, etc.
                return tdp - _deviceMinTdp + 1;
            }
            else
            {
                return tdp - _deviceMinTdp;
            }
        }

        /// <summary>
        /// Converts an index in the _tdpItems list to its TDP value.
        /// </summary>
        private int GetTdpFromIndex(int index)
        {
            if (_includeNoneOption)
            {
                if (index == 0) return 0;
                // Index 1 is TDP _deviceMinTdp, index 2 is next, etc.
                return _deviceMinTdp + index - 1;
            }
            else
            {
                return _deviceMinTdp + index;
            }
        }

        public void Initialize(DpiScalingService dpiService, bool autoSetEnabled = true, bool preserveCurrentValue = false)
        {
            // Fetch device-specific limits before building the item list
            var limits = HardwareDetectionService.GetTdpLimits();
            _deviceMinTdp = limits.MinTdp;
            _deviceMaxTdp = limits.MaxTdp;
            InitializeData();

            DebugLogger.Log($"Picker init: limits {_deviceMinTdp}-{_deviceMaxTdp}W, preserve={preserveCurrentValue}, selected={_selectedTdp}W, items={_tdpItems.Count}", "TDP");

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
                    if (currentTdp < _deviceMinTdp || currentTdp > _deviceMaxTdp)
                    {
                        currentTdp = Math.Clamp(HudraSettings.DEFAULT_STARTUP_TDP, _deviceMinTdp, _deviceMaxTdp);
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

                // Priority 1: Default Profile TDP (if saved)
                var defaultProfile = SettingsService.GetDefaultProfile();
                if (defaultProfile?.TdpWatts > 0)
                {
                    targetTdp = defaultProfile.TdpWatts;
                    statusReason = "using Default Profile TDP";
                }
                // Priority 2: Default TDP if toggle is enabled (legacy setting)
                else if (SettingsService.GetUseStartupTdp())
                {
                    targetTdp = SettingsService.GetStartupTdp();
                    statusReason = "using default TDP from settings";
                }
                else
                {
                    // Priority 3: Last-Used TDP if default is disabled
                    targetTdp = SettingsService.GetLastUsedTdp();

                    // Priority 4: Fallback to clamped default if last-used is invalid
                    if (targetTdp < _deviceMinTdp || targetTdp > _deviceMaxTdp)
                    {
                        targetTdp = Math.Clamp(HudraSettings.DEFAULT_STARTUP_TDP, _deviceMinTdp, _deviceMaxTdp);
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

                    var setResult = await Task.Run(() =>
                    {
                        using var setService = new TDPService();
                        return setService.SetTdp(targetTdp * 1000);
                    });

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
            var oldIndex = GetIndexFromTdp(oldValue);
            if (oldIndex >= 0 && oldIndex < _tdpItems.Count)
            {
                _tdpItems[oldIndex].IsSelected = false;
            }

            // Update new item
            var newIndex = GetIndexFromTdp(newValue);
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

            var selectedIndex = GetIndexFromTdp(_selectedTdp);
            if (selectedIndex >= 0 && selectedIndex < _tdpItems.Count)
            {
                // Mark this as a programmatic scroll to prevent hardware changes
                _isProgrammaticScroll = true;

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

                // Clamp to valid scroll range. NOTE: during an item-list rebuild
                // the ScrollViewer's extent is stale until the next layout pass,
                // so this clamp can land the wheel short of the target (field
                // bug: a preserved 45W landed on the stale list's bottom item,
                // 30). The settle handlers treat programmatic scrolls as
                // corrections toward _selectedTdp and re-issue the scroll if the
                // wheel lands elsewhere, so a stale-extent landing self-heals
                // instead of silently rewriting the selection.
                var maxScroll = Math.Max(0, TdpScrollViewer.ExtentWidth - TdpScrollViewer.ViewportWidth);
                var clampedPosition = Math.Max(0, Math.Min(maxScroll, targetScrollPosition));

                if (Math.Abs(clampedPosition - TdpScrollViewer.HorizontalOffset) < 0.5)
                {
                    // Already there: no scroll events will fire, so the flag
                    // must be cleared here or user scrolls would be
                    // misclassified as programmatic forever.
                    _isProgrammaticScroll = false;
                    return;
                }

                // Perform the scroll. The flag stays set until the settle
                // handler (EnsureProperSelection) observes the scroll end - the
                // old Low-priority dispatcher reset raced the scroll events,
                // and a mis-landing processed after the early reset was
                // adopted as a USER selection, scheduling a hardware write.
                TdpScrollViewer.ScrollToHorizontalOffset(clampedPosition);
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

            return GetTdpFromIndex((int)itemIndex);
        }

        #endregion

        #region Event Handlers

        private void TdpScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (!_isInitialized) return;

            // Page-hide/teardown collapses the viewport, and the resulting
            // scroll events "center" a bogus low item (field log: a selected
            // 45W read back as 10W at the next revisit's init). Degenerate
            // geometry means no real scroll is happening - ignore it.
            if (TdpScrollViewer.ViewportWidth <= 0) return;

            var centeredTdp = GetCenteredTdpFromScroll();

            // Update selection if centered item changed. Never during a
            // programmatic scroll: those move the wheel TOWARD _selectedTdp, so
            // the logical value is authoritative - adopting the physically
            // centered item mid-flight (or after a stale-extent clamp) is what
            // silently rewrote a preserved 45W to the wheel's landing spot.
            if (!_isProgrammaticScroll && centeredTdp != _lastCenteredTdp && centeredTdp != _selectedTdp)
            {
                _suppressSelectionEvents = true;

                var oldSelected = _selectedTdp;
                _selectedTdp = centeredTdp;
                UpdateSelection(oldSelected, centeredTdp);
                OnPropertyChanged(nameof(SelectedTdp));

                // Adjacent item crossings are normal scrolling; a JUMP in one
                // event is the signature of an offset-reset adoption (the
                // suspected 45W->10W corruption path) - log those only.
                if (Math.Abs(centeredTdp - oldSelected) >= 5)
                {
                    DebugLogger.Log($"Scroll adoption jump: {oldSelected}W -> {centeredTdp}W (offset={TdpScrollViewer.HorizontalOffset:F0}, extent={TdpScrollViewer.ExtentWidth:F0}, viewport={TdpScrollViewer.ViewportWidth:F0}, intermediate={e.IsIntermediate})", "TDP");
                }

                // Play audio feedback only for user-initiated scrolls
                if (_lastCenteredTdp != -1 && _audioHelper != null && !_isProgrammaticScroll)
                {
                    _audioHelper.PlayTick();
                }

                _lastCenteredTdp = centeredTdp;
                _suppressSelectionEvents = false;

                // Schedule hardware changes only for user-initiated scrolls (not programmatic)
                if (_autoSetEnabled && _autoSetManager != null && !_isProgrammaticScroll)
                {
                    _autoSetManager.ScheduleUpdate(_selectedTdp);
                }

                // Only fire TdpChanged for user-initiated scrolls
                if (!_isProgrammaticScroll)
                {
                    TdpChanged?.Invoke(this, _selectedTdp);
                }
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
            // Same degenerate-geometry guard as ViewChanged: the deferred
            // scroll-end timer can fire after the page is already hidden.
            if (TdpScrollViewer.ViewportWidth <= 0) return;

            var centeredTdp = GetCenteredTdpFromScroll();

            if (_isProgrammaticScroll)
            {
                // A programmatic scroll moves the wheel toward _selectedTdp -
                // the value is the truth and the wheel is the thing being
                // corrected. If it landed elsewhere (stale extent during a
                // rebuild), re-issue the scroll now that layout has caught up.
                _isProgrammaticScroll = false;

                if (centeredTdp != _selectedTdp && _programmaticRetries < 3)
                {
                    _programmaticRetries++;
                    DebugLogger.Log($"Wheel landed on {centeredTdp}W after programmatic scroll to {_selectedTdp}W - re-centering (attempt {_programmaticRetries})", "TDP");
                    ScrollToSelectedItem();
                }
                else
                {
                    _programmaticRetries = 0;
                }
                return;
            }

            _programmaticRetries = 0;

            if (centeredTdp != _selectedTdp)
            {
                DebugLogger.Log($"Scroll-end adoption: {_selectedTdp}W -> {centeredTdp}W (offset={TdpScrollViewer.HorizontalOffset:F0}, extent={TdpScrollViewer.ExtentWidth:F0}, viewport={TdpScrollViewer.ViewportWidth:F0})", "TDP");
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

            if (tappedTdp >= EffectiveMinTdp && tappedTdp <= _deviceMaxTdp)
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

            return GetTdpFromIndex((int)itemIndex);
        }

        #endregion

        #region Public Methods

        public void ChangeTdpBy(int delta)
        {
            int newTdp;

            if (_includeNoneOption)
            {
                // Handle the gap between 0 (Default) and _deviceMinTdp
                if (_selectedTdp == 0 && delta > 0)
                {
                    // From 0, go to _deviceMinTdp
                    newTdp = _deviceMinTdp;
                }
                else if (_selectedTdp == _deviceMinTdp && delta < 0)
                {
                    // From _deviceMinTdp, go to 0
                    newTdp = 0;
                }
                else if (_selectedTdp == 0 && delta < 0)
                {
                    // Already at 0, can't go lower
                    newTdp = 0;
                }
                else
                {
                    // Normal case: clamp to _deviceMinTdp.._deviceMaxTdp range
                    newTdp = Math.Max(_deviceMinTdp, Math.Min(_deviceMaxTdp, _selectedTdp + delta));
                }
            }
            else
            {
                // No "None" option - simple clamp
                newTdp = Math.Max(_deviceMinTdp, Math.Min(_deviceMaxTdp, _selectedTdp + delta));
            }

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

        /// <summary>
        /// Syncs the UI to reflect a TDP value that was set externally (e.g., by a game profile).
        /// This updates the visual selection without triggering hardware TDP changes.
        /// ScrollToSelectedItem() sets _isProgrammaticScroll which prevents hardware changes.
        /// </summary>
        public void SyncToCurrentTdp(int tdpValue)
        {
            if (!_isInitialized) return;

            // Validate: value must be 0 (if IncludeNoneOption) or within device TDP range
            bool isValid = (_includeNoneOption && tdpValue == 0) ||
                           (tdpValue >= _deviceMinTdp && tdpValue <= _deviceMaxTdp);
            if (!isValid)
            {
                DebugLogger.Log($"SyncToCurrentTdp({tdpValue}W) rejected (limits {_deviceMinTdp}-{_deviceMaxTdp}W, current {_selectedTdp}W)", "TDP");
                return;
            }

            var oldValue = _selectedTdp;
            _selectedTdp = tdpValue;
            _lastCenteredTdp = tdpValue;

            UpdateSelection(oldValue, tdpValue);
            ScrollToSelectedItem(); // Sets _isProgrammaticScroll to prevent hardware changes

            // Update status text to reflect current TDP
            StatusText = $"Current TDP: {tdpValue}W (game profile)";

            OnPropertyChanged(nameof(SelectedTdp));
        }

        /// <summary>
        /// Re-read device TDP limits and rebuild the wheel if they changed.
        /// Exists because the FIRST picker of a session races
        /// FanControlService initialization: GetTdpLimits falls back to the
        /// 5-30 W constants until the fan device is detected, capping the wheel
        /// at 30 on devices that go higher. Called when fan init completes.
        /// Preserves the current selection (clamped only if now out of range).
        /// </summary>
        public void RefreshDeviceLimits()
        {
            if (!_isInitialized) return;

            var limits = HardwareDetectionService.GetTdpLimits();
            if (limits.MinTdp == _deviceMinTdp && limits.MaxTdp == _deviceMaxTdp) return;

            DebugLogger.Log($"Device TDP limits refreshed: {_deviceMinTdp}-{_deviceMaxTdp}W -> {limits.MinTdp}-{limits.MaxTdp}W (selected {_selectedTdp}W)", "TDP");

            _deviceMinTdp = limits.MinTdp;
            _deviceMaxTdp = limits.MaxTdp;
            InitializeData();

            // Selection normally survives (limits only ever widen here); clamp
            // defensively if it does not, without touching hardware.
            if (_selectedTdp != 0 || !_includeNoneOption)
            {
                _selectedTdp = Math.Clamp(_selectedTdp, _deviceMinTdp, _deviceMaxTdp);
            }

            UpdateSelection(-1, _selectedTdp);
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                ScrollToSelectedItem();
            });
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
                int tdpInMilliwatts = tdpValue * 1000;
                var result = await Task.Run(() =>
                {
                    using var tdpService = new TDPService();
                    return tdpService.SetTdp(tdpInMilliwatts);
                });

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

        public void OnGamepadBack() { }

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