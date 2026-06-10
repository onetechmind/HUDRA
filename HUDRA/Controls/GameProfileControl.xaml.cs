using HUDRA.AttachedProperties;
using HUDRA.Interfaces;
using HUDRA.Models;
using HUDRA.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace HUDRA.Controls
{
    public sealed partial class GameProfileControl : UserControl, INotifyPropertyChanged, IGamepadNavigable
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? ProfileChanged;

        private GameProfile _profile = new();
        private ResolutionService? _resolutionService;
        private DpiScalingService? _dpiScalingService;
        private bool _isInitialized = false;
        private bool _suppressEvents = false;
        private bool _tdpPickerInitialized = false;

        // FPS limit slider state
        private int _maxFpsLimit = 240;
        private bool _isUpdatingFpsSlider = false;

        public int MaxFpsLimit => _maxFpsLimit;

        // Feature availability
        private bool _isRtssAvailable = false;
        private bool _isHdrSupported = false;
        private bool _isAmdAvailable = false;
        private bool _isFanControlAvailable = false;
        private HdrService? _hdrService;

        // Gamepad navigation state
        private GamepadNavigationService? _gamepadNavigationService;
        private int _currentFocusedElement = 0;
        private bool _gamepadIsFocused = false;
        private bool _isSliderActivated = false;

        // Focus elements mapping (dynamic based on feature availability):
        // Base: 0=AutoRevert, 1=TdpPicker, [FpsLimit if RTSS], Resolution, RefreshRate, Hdr
        // Conditional: FpsLimit (if RTSS), FanCurve (if fan), RSR (if AMD), AFMF (if AMD), AntiLag (if AMD)
        private int MaxFocusIndex
        {
            get
            {
                int count = 5; // TdpPicker, AutoRevert, Resolution, RefreshRate, Hdr
                if (_isRtssAvailable) count++; // FpsLimit
                if (_isFanControlAvailable) count++; // FanCurve
                if (_isAmdAvailable) count += 4; // RSR, RsrSharpness, AFMF, AntiLag
                return count - 1;
            }
        }

        public GameProfile Profile
        {
            get => _profile;
            set
            {
                _profile = value ?? new GameProfile();
                OnPropertyChanged();
                LoadProfileIntoUI();
            }
        }

        // Visibility properties for conditional sections
        public Visibility RtssAvailableVisibility => _isRtssAvailable ? Visibility.Visible : Visibility.Collapsed;
        public Visibility AmdAvailableVisibility => _isAmdAvailable ? Visibility.Visible : Visibility.Collapsed;
        public Visibility FanControlAvailableVisibility => _isFanControlAvailable ? Visibility.Visible : Visibility.Collapsed;

        // Helper to get element type from focus index
        private enum FocusElement { TdpPicker, AutoRevert, Resolution, RefreshRate, FpsLimit, Hdr, FanCurve, Rsr, RsrSharpness, Afmf, AntiLag }

        private FocusElement GetElementAtIndex(int index)
        {
            // Base elements: AutoRevert, TdpPicker (always first two)
            if (index == 0) return FocusElement.AutoRevert;
            if (index == 1) return FocusElement.TdpPicker;

            int offset = 2;

            // FpsLimit (if RTSS) — directly after TDP
            if (_isRtssAvailable)
            {
                if (index == offset) return FocusElement.FpsLimit;
                offset++;
            }

            // Resolution, RefreshRate (always)
            if (index == offset) return FocusElement.Resolution;
            offset++;
            if (index == offset) return FocusElement.RefreshRate;
            offset++;

            // Hdr (always present, may be disabled)
            if (index == offset) return FocusElement.Hdr;
            offset++;

            // FanCurve (if fan control)
            if (_isFanControlAvailable)
            {
                if (index == offset) return FocusElement.FanCurve;
                offset++;
            }

            // AMD features (if AMD)
            if (_isAmdAvailable)
            {
                if (index == offset) return FocusElement.Rsr;
                if (index == offset + 1) return FocusElement.RsrSharpness;
                if (index == offset + 2) return FocusElement.Afmf;
                if (index == offset + 3) return FocusElement.AntiLag;
            }

            return FocusElement.AutoRevert; // Fallback
        }

        // Focus brush properties for gamepad navigation
        public Brush TdpFocusBrush => GetFocusBrush(FocusElement.TdpPicker);
        public Brush AutoRevertFocusBrush => GetFocusBrush(FocusElement.AutoRevert);
        public Brush ResolutionFocusBrush => GetFocusBrush(FocusElement.Resolution);
        public Brush RefreshRateFocusBrush => GetFocusBrush(FocusElement.RefreshRate);
        public Brush FpsLimitFocusBrush => GetFocusBrush(FocusElement.FpsLimit);
        public Brush HdrFocusBrush => GetFocusBrush(FocusElement.Hdr);
        public Brush FanCurveFocusBrush => GetFocusBrush(FocusElement.FanCurve);
        public Brush RsrFocusBrush => GetFocusBrush(FocusElement.Rsr);
        public Brush RsrSharpnessFocusBrush => GetFocusBrush(FocusElement.RsrSharpness);
        public Brush AfmfFocusBrush => GetFocusBrush(FocusElement.Afmf);
        public Brush AntiLagFocusBrush => GetFocusBrush(FocusElement.AntiLag);

        // HDR properties
        public bool IsHdrSupported => _isHdrSupported;
        public double HdrOpacity => _isHdrSupported ? 1.0 : 0.5;

        private Brush GetFocusBrush(FocusElement element)
        {
            if (_gamepadIsFocused && _gamepadNavigationService?.IsGamepadActive == true
                && GetElementAtIndex(_currentFocusedElement) == element)
            {
                // Show DodgerBlue when a slider is activated, DarkViolet otherwise
                if ((element == FocusElement.RsrSharpness || element == FocusElement.FpsLimit) && _isSliderActivated)
                {
                    return new SolidColorBrush(Colors.DodgerBlue);
                }
                return new SolidColorBrush(Colors.DarkViolet);
            }
            return new SolidColorBrush(Colors.Transparent);
        }

        // IGamepadNavigable implementation
        public bool CanNavigateUp => _currentFocusedElement > 0;
        public bool CanNavigateDown => _currentFocusedElement < MaxFocusIndex;
        public bool CanNavigateLeft =>
            GetElementAtIndex(_currentFocusedElement) == FocusElement.TdpPicker || _isSliderActivated;
        public bool CanNavigateRight =>
            GetElementAtIndex(_currentFocusedElement) == FocusElement.TdpPicker || _isSliderActivated;
        public bool CanActivate => true;
        public FrameworkElement NavigationElement => this;

        public bool IsSlider => GetElementAtIndex(_currentFocusedElement) == FocusElement.RsrSharpness
                             || GetElementAtIndex(_currentFocusedElement) == FocusElement.FpsLimit;
        public bool IsSliderActivated
        {
            get => _isSliderActivated;
            set
            {
                _isSliderActivated = value;
                UpdateGamepadFocusVisuals();
            }
        }
        public void AdjustSliderValue(int direction)
        {
            if (!_isSliderActivated) return;

            var element = GetElementAtIndex(_currentFocusedElement);
            if (element == FocusElement.RsrSharpness && RsrSharpnessSlider != null)
            {
                double increment = 5.0; // 5% increments
                double newValue = RsrSharpnessSlider.Value + (direction * increment);
                newValue = Math.Clamp(newValue, RsrSharpnessSlider.Minimum, RsrSharpnessSlider.Maximum);
                RsrSharpnessSlider.Value = newValue;
            }
            else if (element == FocusElement.FpsLimit && FpsLimitSlider != null)
            {
                double newValue = Math.Clamp(FpsLimitSlider.Value + direction, 0, _maxFpsLimit);
                FpsLimitSlider.Value = newValue;
            }
        }

        public bool HasComboBoxes => true;
        public bool IsComboBoxOpen { get; set; } = false;
        public int ComboBoxOriginalIndex { get; set; } = -1;
        public bool IsNavigatingComboBox { get; set; } = false;

        public ComboBox? GetFocusedComboBox()
        {
            var element = GetElementAtIndex(_currentFocusedElement);
            return element switch
            {
                FocusElement.Resolution => ResolutionComboBox,
                FocusElement.RefreshRate => RefreshRateComboBox,
                FocusElement.Hdr => HdrComboBox,
                FocusElement.FanCurve => FanCurvePresetComboBox,
                FocusElement.Rsr => RsrComboBox,
                FocusElement.Afmf => AfmfComboBox,
                FocusElement.AntiLag => AntiLagComboBox,
                _ => null
            };
        }

        public void ProcessCurrentSelection()
        {
            // ComboBox selection is handled by the ComboBox itself
        }

        public void OnGamepadNavigateUp()
        {
            if (_currentFocusedElement > 0)
            {
                _currentFocusedElement--;
                UpdateGamepadFocusVisuals();
            }
        }

        public void OnGamepadNavigateDown()
        {
            if (_currentFocusedElement < MaxFocusIndex)
            {
                _currentFocusedElement++;
                UpdateGamepadFocusVisuals();
            }
        }

        public void OnGamepadNavigateLeft()
        {
            var element = GetElementAtIndex(_currentFocusedElement);

            if (element == FocusElement.TdpPicker)
            {
                TdpPicker.ChangeTdpBy(-1);
            }
            else if (_isSliderActivated && element == FocusElement.RsrSharpness)
            {
                AdjustSliderValue(-1);
            }
        }

        public void OnGamepadNavigateRight()
        {
            var element = GetElementAtIndex(_currentFocusedElement);

            if (element == FocusElement.TdpPicker)
            {
                TdpPicker.ChangeTdpBy(1);
            }
            else if (_isSliderActivated && element == FocusElement.RsrSharpness)
            {
                AdjustSliderValue(1);
            }
        }

        public void OnGamepadActivate()
        {
            var element = GetElementAtIndex(_currentFocusedElement);
            switch (element)
            {
                case FocusElement.AutoRevert:
                    AutoRevertToggle.IsOn = !AutoRevertToggle.IsOn;
                    break;
                case FocusElement.Resolution:
                    ResolutionComboBox.IsDropDownOpen = true;
                    break;
                case FocusElement.RefreshRate:
                    RefreshRateComboBox.IsDropDownOpen = true;
                    break;
                case FocusElement.FpsLimit:
                    if (_isRtssAvailable)
                    {
                        _isSliderActivated = !_isSliderActivated;
                        IsSliderActivated = _isSliderActivated;
                    }
                    break;
                case FocusElement.Hdr:
                    if (_isHdrSupported) HdrComboBox.IsDropDownOpen = true;
                    break;
                case FocusElement.FanCurve:
                    if (_isFanControlAvailable) FanCurvePresetComboBox.IsDropDownOpen = true;
                    break;
                case FocusElement.Rsr:
                    RsrComboBox.IsDropDownOpen = true;
                    break;
                case FocusElement.RsrSharpness:
                    // Toggle slider activation (only if RSR is set to "On")
                    if (_profile.RsrEnabled == true)
                    {
                        _isSliderActivated = !_isSliderActivated;
                        IsSliderActivated = _isSliderActivated;
                    }
                    break;
                case FocusElement.Afmf:
                    AfmfComboBox.IsDropDownOpen = true;
                    break;
                case FocusElement.AntiLag:
                    AntiLagComboBox.IsDropDownOpen = true;
                    break;
                // TdpPicker doesn't use activation
            }
        }

        public void OnGamepadBack() { }


        // Focus memory: the position inside this composite is saved when
        // leaving the page and restored on return (after OnGamepadFocusReceived
        // has reset it to 0).
        public int GamepadFocusMemory
        {
            get => _currentFocusedElement;
            set
            {
                _currentFocusedElement = Math.Clamp(value, 0, MaxFocusIndex);
                UpdateGamepadFocusVisuals();
            }
        }

        public void OnGamepadFocusReceived()
        {
            if (_gamepadNavigationService == null)
            {
                InitializeGamepadNavigationService();
            }
            _currentFocusedElement = 0;
            _gamepadIsFocused = true;
            UpdateGamepadFocusVisuals();
        }

        public void OnGamepadFocusLost()
        {
            _gamepadIsFocused = false;
            UpdateGamepadFocusVisuals();
        }

        public void FocusLastElement()
        {
            _currentFocusedElement = MaxFocusIndex;
            _gamepadIsFocused = true;
            UpdateGamepadFocusVisuals();
        }

        private void InitializeGamepadNavigationService()
        {
            if (Application.Current is App app && app.MainWindow is MainWindow mainWindow)
            {
                _gamepadNavigationService = mainWindow.GamepadNavigationService;
            }
        }

        private void UpdateGamepadFocusVisuals()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                OnPropertyChanged(nameof(TdpFocusBrush));
                OnPropertyChanged(nameof(AutoRevertFocusBrush));
                OnPropertyChanged(nameof(ResolutionFocusBrush));
                OnPropertyChanged(nameof(RefreshRateFocusBrush));
                OnPropertyChanged(nameof(FpsLimitFocusBrush));
                OnPropertyChanged(nameof(HdrFocusBrush));
                OnPropertyChanged(nameof(FanCurveFocusBrush));
                OnPropertyChanged(nameof(RsrFocusBrush));
                OnPropertyChanged(nameof(RsrSharpnessFocusBrush));
                OnPropertyChanged(nameof(AfmfFocusBrush));
                OnPropertyChanged(nameof(AntiLagFocusBrush));

                // Scroll current element into view
                ScrollCurrentElementIntoView();
            });
        }

        private void ScrollCurrentElementIntoView()
        {
            FrameworkElement? elementToScroll = GetElementAtIndex(_currentFocusedElement) switch
            {
                FocusElement.TdpPicker => TdpPicker,
                FocusElement.AutoRevert => AutoRevertToggle,
                FocusElement.Resolution => ResolutionComboBox,
                FocusElement.RefreshRate => RefreshRateComboBox,
                FocusElement.FpsLimit => FpsLimitSlider,
                FocusElement.Hdr => HdrComboBox,
                FocusElement.FanCurve => FanCurvePresetComboBox,
                FocusElement.Rsr => RsrComboBox,
                FocusElement.RsrSharpness => RsrSharpnessSlider,
                FocusElement.Afmf => AfmfComboBox,
                FocusElement.AntiLag => AntiLagComboBox,
                _ => null
            };

            elementToScroll?.StartBringIntoView(new BringIntoViewOptions
            {
                AnimationDesired = true,
                VerticalAlignmentRatio = 0.5
            });
        }

        public GameProfileControl()
        {
            this.InitializeComponent();
            this.Loaded += GameProfileControl_Loaded;

            // Gamepad navigation - only accessible via parent expander
            GamepadNavigation.SetIsEnabled(this, true);
            GamepadNavigation.SetNavigationGroup(this, "MainControls");
            GamepadNavigation.SetNavigationOrder(this, 1);
            GamepadNavigation.SetCanNavigate(this, false);
        }

        private void GameProfileControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized)
            {
                _isInitialized = true;  // Set before Initialize so LoadProfileIntoUI doesn't return early
                Initialize();
            }
        }

        /// <summary>
        /// Initialize the control with available system features.
        /// </summary>
        public void Initialize()
        {
            _resolutionService = new ResolutionService();
            _hdrService = new HdrService();

            // Get DPI scaling service from app
            if (Application.Current is App app && app.MainWindow is MainWindow mainWindow)
            {
                _dpiScalingService = mainWindow.DpiScalingService;
            }

            // Check feature availability
            _isRtssAvailable = RtssFpsLimiterService.GetCachedInstallationStatus();

            // Check HDR availability
            try
            {
                _isHdrSupported = _hdrService.IsHdrSupported();
            }
            catch
            {
                _isHdrSupported = false;
            }

            // Check AMD availability
            try
            {
                using var amdService = new AmdAdlxService();
                _isAmdAvailable = amdService.IsAmdGpuAvailable();
            }
            catch
            {
                _isAmdAvailable = false;
            }

            // Check fan control availability
            if (Application.Current is App appInstance)
            {
                _isFanControlAvailable = appInstance.FanControlService?.IsDeviceAvailable == true;
            }

            // Compute max available refresh rate for FPS slider.
            // Resolution.Equals compares only Width/Height, so GetAvailableResolutions deduplicates
            // by size and loses per-resolution refresh rates. Use GetAvailableRefreshRates instead.
            try
            {
                if (_resolutionService != null)
                {
                    var sysRes = _resolutionService.GetCurrentResolution();
                    if (sysRes.Success)
                    {
                        var rates = _resolutionService.GetAvailableRefreshRates(sysRes.CurrentResolution);
                        if (rates.Count > 0)
                            _maxFpsLimit = rates.Max();
                    }
                }
            }
            catch { }
            OnPropertyChanged(nameof(MaxFpsLimit));

            // Populate combo boxes with "Default" options
            PopulateResolutionComboBox();
            PopulateRefreshRateComboBox();

            // Initialize TDP picker
            InitializeTdpPicker();

            // Update visibility and HDR properties
            OnPropertyChanged(nameof(RtssAvailableVisibility));
            OnPropertyChanged(nameof(AmdAvailableVisibility));
            OnPropertyChanged(nameof(FanControlAvailableVisibility));
            OnPropertyChanged(nameof(IsHdrSupported));
            OnPropertyChanged(nameof(HdrOpacity));

            // Load initial profile state
            LoadProfileIntoUI();
        }

        private void InitializeTdpPicker()
        {
            if (_tdpPickerInitialized || _dpiScalingService == null) return;

            try
            {
                // Initialize the TDP picker with autoSetEnabled=false so it doesn't auto-apply changes
                // Use preserveCurrentValue=true since we'll set the value manually
                TdpPicker.Initialize(_dpiScalingService, autoSetEnabled: false, preserveCurrentValue: true);
                TdpPicker.TdpChanged += TdpPicker_TdpChanged;

                // Set to 0 (not set) by default - this will show as first item
                // Note: The TdpPicker min is 5W, so we need to handle 0 specially
                // For now, we'll use min TDP to represent "not set" visually
                // and track the actual 0 value in the profile

                _tdpPickerInitialized = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize TDP picker: {ex.Message}");
            }
        }

        private void TdpPicker_TdpChanged(object? sender, int newTdp)
        {
            if (_suppressEvents) return;

            _profile.TdpWatts = newTdp;
            NotifyProfileChanged();
        }

        private void PopulateResolutionComboBox()
        {
            _suppressEvents = true;
            ResolutionComboBox.Items.Clear();

            // Add "Default" option first
            ResolutionComboBox.Items.Add(new ComboBoxItem
            {
                Content = "Default",
                Tag = "Default",
                Style = (Style)Application.Current.Resources["HudraComboBoxItemStyle"]
            });

            if (_resolutionService != null)
            {
                var resolutions = _resolutionService.GetAvailableResolutions();
                var uniqueResolutions = resolutions
                    .Select(r => new { r.Width, r.Height })
                    .Distinct()
                    .OrderByDescending(r => r.Width * r.Height);

                foreach (var res in uniqueResolutions)
                {
                    ResolutionComboBox.Items.Add(new ComboBoxItem
                    {
                        Content = $"{res.Width}x{res.Height}",
                        Tag = $"{res.Width}x{res.Height}",
                        Style = (Style)Application.Current.Resources["HudraComboBoxItemStyle"]
                    });
                }
            }

            // Select "Default" by default
            ResolutionComboBox.SelectedIndex = 0;
            _suppressEvents = false;
        }

        private void PopulateRefreshRateComboBox()
        {
            _suppressEvents = true;
            RefreshRateComboBox.Items.Clear();

            // Add "Default" option first
            RefreshRateComboBox.Items.Add(new ComboBoxItem
            {
                Content = "Default",
                Tag = 0,
                Style = (Style)Application.Current.Resources["HudraComboBoxItemStyle"]
            });

            if (_resolutionService != null)
            {
                // Get refresh rates for current resolution (or all if none selected)
                ResolutionService.Resolution currentRes;
                if (_profile.ResolutionWidth > 0 && _profile.ResolutionHeight > 0)
                {
                    currentRes = new ResolutionService.Resolution
                    {
                        Width = _profile.ResolutionWidth,
                        Height = _profile.ResolutionHeight
                    };
                }
                else
                {
                    // Use current system resolution
                    var sysRes = _resolutionService.GetCurrentResolution();
                    currentRes = sysRes.Success ? sysRes.CurrentResolution : new ResolutionService.Resolution { Width = 1920, Height = 1080 };
                }

                var refreshRates = _resolutionService.GetAvailableRefreshRates(currentRes);

                foreach (var rate in refreshRates)
                {
                    RefreshRateComboBox.Items.Add(new ComboBoxItem
                    {
                        Content = $"{rate} Hz",
                        Tag = rate,
                        Style = (Style)Application.Current.Resources["HudraComboBoxItemStyle"]
                    });
                }
            }

            // Select "Default" by default
            RefreshRateComboBox.SelectedIndex = 0;
            _suppressEvents = false;
        }

        private void FpsLimitSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_suppressEvents || _isUpdatingFpsSlider) return;

            int fps = (int)e.NewValue;
            if (FpsLimitValueLabel != null)
                FpsLimitValueLabel.Text = fps == 0 ? "Off" : $"{fps} FPS";
            _profile.FpsLimit = fps;
            NotifyProfileChanged();
        }

        private void LoadProfileIntoUI()
        {
            if (!_isInitialized)
            {
                System.Diagnostics.Debug.WriteLine("GameProfileControl: LoadProfileIntoUI skipped - not initialized");
                return;
            }

            _suppressEvents = true;

            System.Diagnostics.Debug.WriteLine($"GameProfileControl: LoadProfileIntoUI - TDP={_profile.TdpWatts}W, AutoRevert={_profile.AutoRevertOnClose}, Resolution={_profile.ResolutionWidth}x{_profile.ResolutionHeight}, RefreshRate={_profile.RefreshRateHz}Hz");

            // Load Auto-Revert setting
            AutoRevertToggle.IsOn = _profile.AutoRevertOnClose;

            // Load TDP picker value (0 = not set, which is shown as "0W" option)
            if (_tdpPickerInitialized)
            {
                TdpPicker.SetSelectedTdpWhenReady(_profile.TdpWatts);
            }

            // Load Resolution
            if (_profile.ResolutionWidth > 0 && _profile.ResolutionHeight > 0)
            {
                SelectComboBoxByTag(ResolutionComboBox, $"{_profile.ResolutionWidth}x{_profile.ResolutionHeight}");
            }
            else
            {
                ResolutionComboBox.SelectedIndex = 0; // "Default"
            }

            // Refresh the refresh rate options based on resolution
            PopulateRefreshRateComboBox();

            // Load Refresh Rate
            if (_profile.RefreshRateHz > 0)
            {
                SelectComboBoxByTag(RefreshRateComboBox, _profile.RefreshRateHz);
            }
            else
            {
                RefreshRateComboBox.SelectedIndex = 0; // "Default"
            }

            // Load FPS Limit
            if (FpsLimitSlider != null)
            {
                _isUpdatingFpsSlider = true;
                int displayFps = Math.Max(0, _profile.FpsLimit); // treat -1 (legacy Default) as 0
                FpsLimitSlider.Value = Math.Clamp(displayFps, 0, _maxFpsLimit);
                if (FpsLimitValueLabel != null)
                    FpsLimitValueLabel.Text = displayFps == 0 ? "Off" : $"{displayFps} FPS";
                _isUpdatingFpsSlider = false;
            }

            // Load HDR
            SelectTriStateComboBox(HdrComboBox, _profile.HdrEnabled);

            // Load RSR
            SelectTriStateComboBox(RsrComboBox, _profile.RsrEnabled);
            RsrSharpnessSlider.IsEnabled = _profile.RsrEnabled == true;
            RsrSharpnessSlider.Value = _profile.RsrSharpness;
            RsrSharpnessText.Text = _profile.RsrSharpness.ToString();
            UpdateRsrSharpnessOpacity();

            // Load AFMF
            SelectTriStateComboBox(AfmfComboBox, _profile.AfmfEnabled);

            // Load Anti-Lag
            SelectTriStateComboBox(AntiLagComboBox, _profile.AntiLagEnabled);

            // Load Fan Curve
            SelectComboBoxByTag(FanCurvePresetComboBox, _profile.FanCurvePreset);

            _suppressEvents = false;
        }

        /// <summary>
        /// Selects the appropriate item in a tri-state ComboBox (Default/Off/On) based on a nullable bool.
        /// </summary>
        private void SelectTriStateComboBox(ComboBox comboBox, bool? value)
        {
            if (!value.HasValue)
            {
                comboBox.SelectedIndex = 0; // "Default"
            }
            else if (value.Value)
            {
                comboBox.SelectedIndex = 2; // "On"
            }
            else
            {
                comboBox.SelectedIndex = 1; // "Off"
            }
        }

        /// <summary>
        /// Converts a tri-state ComboBox selection to a nullable bool.
        /// </summary>
        private bool? GetTriStateValue(ComboBoxItem? item)
        {
            if (item?.Tag is string tag)
            {
                return tag switch
                {
                    "On" => true,
                    "Off" => false,
                    _ => null // "Default"
                };
            }
            return null;
        }

        private void UpdateRsrSharpnessOpacity()
        {
            var opacity = _profile.RsrEnabled == true ? 1.0 : 0.5;
            RsrSharpnessPanel.Opacity = opacity;
        }

        private void SelectComboBoxByTag(ComboBox comboBox, object tag)
        {
            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                if (comboBox.Items[i] is ComboBoxItem item &&
                    item.Tag?.ToString() == tag.ToString())
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }

            // If not found, select first item ("Default")
            if (comboBox.Items.Count > 0)
            {
                comboBox.SelectedIndex = 0;
            }
        }

        // ComboBox event handlers
        private void ResolutionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents) return;

            if (ResolutionComboBox.SelectedItem is ComboBoxItem item)
            {
                if (item.Tag?.ToString() == "Default")
                {
                    _profile.ResolutionWidth = 0;
                    _profile.ResolutionHeight = 0;
                }
                else if (item.Tag is string resString)
                {
                    var parts = resString.Split('x');
                    if (parts.Length == 2 &&
                        int.TryParse(parts[0], out int width) &&
                        int.TryParse(parts[1], out int height))
                    {
                        _profile.ResolutionWidth = width;
                        _profile.ResolutionHeight = height;
                    }
                }

                // Update refresh rate options for new resolution
                PopulateRefreshRateComboBox();
                NotifyProfileChanged();
            }
        }

        private void RefreshRateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents) return;

            if (RefreshRateComboBox.SelectedItem is ComboBoxItem item && item.Tag is int rate)
            {
                _profile.RefreshRateHz = rate;
                NotifyProfileChanged();
            }
        }

        private void HdrComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents) return;

            _profile.HdrEnabled = GetTriStateValue(HdrComboBox.SelectedItem as ComboBoxItem);
            NotifyProfileChanged();
        }

        private void AutoRevertToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents) return;

            _profile.AutoRevertOnClose = AutoRevertToggle.IsOn;
            NotifyProfileChanged();
        }

        private void RsrComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents) return;

            _profile.RsrEnabled = GetTriStateValue(RsrComboBox.SelectedItem as ComboBoxItem);
            RsrSharpnessSlider.IsEnabled = _profile.RsrEnabled == true;
            UpdateRsrSharpnessOpacity();
            NotifyProfileChanged();
        }

        private void RsrSharpnessSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_suppressEvents) return;

            _profile.RsrSharpness = (int)e.NewValue;
            RsrSharpnessText.Text = _profile.RsrSharpness.ToString();
            NotifyProfileChanged();
        }

        private void AfmfComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents) return;

            _profile.AfmfEnabled = GetTriStateValue(AfmfComboBox.SelectedItem as ComboBoxItem);
            NotifyProfileChanged();
        }

        private void AntiLagComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents) return;

            _profile.AntiLagEnabled = GetTriStateValue(AntiLagComboBox.SelectedItem as ComboBoxItem);
            NotifyProfileChanged();
        }

        private void FanCurvePresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents) return;

            if (FanCurvePresetComboBox.SelectedItem is ComboBoxItem item && item.Tag is string preset)
            {
                _profile.FanCurvePreset = preset;
                NotifyProfileChanged();
            }
        }

        private void NotifyProfileChanged()
        {
            _profile.HasProfile = _profile.HasAnySettingsConfigured;
            ProfileChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
