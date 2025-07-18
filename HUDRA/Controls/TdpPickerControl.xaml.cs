using HUDRA.Configuration;
using HUDRA.Helpers;
using HUDRA.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace HUDRA.Controls
{
    public sealed partial class TdpPickerControl : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<int>? TdpChanged;

        private DpiScalingService? _dpiService;
        private NavigationService? _navigationService;
        private TdpAutoSetManager? _autoSetManager;
        private bool _autoSetEnabled = true;
        private Border? _startPadding;
        private Border? _endPadding;
        private int _selectedTdp = HudraSettings.DEFAULT_STARTUP_TDP;
        private bool _isScrolling = false;
        private bool _isManualScrolling = false;
        private bool _isInitialized = false;
        private bool _suppressScrollEvents = false;
        private bool _suppressTdpChangeEvents = false;
        private string _statusText = "Current TDP: Not Set";

        private bool _showLabel = true;
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
                    System.Diagnostics.Debug.WriteLine($"TDP Picker: SelectedTdp changing from {_selectedTdp} to {value}");

                    _selectedTdp = value;

                    if (_isInitialized && !_suppressTdpChangeEvents)
                    {
                        UpdateNumberOpacity();

                        // Only scroll if we're not currently scrolling and navigation isn't happening
                        if (!_isScrolling && !IsNavigating())
                        {
                            ScrollToTdpSilently(_selectedTdp);
                        }
                    }

                    OnPropertyChanged();

                    if (!_suppressTdpChangeEvents)
                    {
                        TdpChanged?.Invoke(this, _selectedTdp);
                    }
                }
            }
        }

        public Border PickerBorder => TdpPickerBorder;

        public TdpPickerControl()
        {
            this.InitializeComponent();
        }

        public void Initialize(DpiScalingService dpiService, bool autoSetEnabled = true, bool preserveCurrentValue = false)
        {
            System.Diagnostics.Debug.WriteLine($"TDP Picker Initialize: autoSetEnabled={autoSetEnabled}, preserveCurrentValue={preserveCurrentValue}");

            _dpiService = dpiService ?? throw new ArgumentNullException(nameof(dpiService));
            _autoSetEnabled = autoSetEnabled;
            _autoSetManager = autoSetEnabled ? new TdpAutoSetManager(SetTdpAsync, status => StatusText = status) : null;

            // Try to get navigation service from app
            if (Application.Current is App app && app.MainWindow is MainWindow mainWindow)
            {
                _navigationService = mainWindow.NavigationService;
            }

            InitializePicker();

            // Set initialized flag BEFORE loading TDP so visual updates work
            _isInitialized = true;
            System.Diagnostics.Debug.WriteLine("TDP Picker: Set _isInitialized = true");

            if (!preserveCurrentValue)
            {
                LoadCurrentTdp();
            }
            else
            {
                // For preserved value, just update visuals without changing the selected value
                System.Diagnostics.Debug.WriteLine($"TDP Picker: Preserving current value {_selectedTdp} - skipping LoadCurrentTdp");

                // Just update the visual appearance without changing any values
                UpdateNumberOpacity();
                StatusText = $"Current TDP: {_selectedTdp}W (preserved)";

                // Position scroll after a delay to ensure layout is complete
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (!IsNavigating())
                    {
                        System.Diagnostics.Debug.WriteLine($"TDP Picker: Positioning scroll to preserved value: {_selectedTdp}");
                        ScrollToTdpSilently(_selectedTdp);
                    }
                });
            }
        }

        private void InitializePicker()
        {
            if (_dpiService == null) return;

            System.Diagnostics.Debug.WriteLine("TDP Picker: InitializePicker called");

            NumbersPanel.Children.Clear();
            NumbersPanel.Spacing = _dpiService.SpacingWidth;

            // Add start padding
            var startPadding = new Border { Width = 100 };
            NumbersPanel.Children.Add(startPadding);
            _startPadding = startPadding;

            // Create number TextBlocks
            for (int tdpValue = HudraSettings.MIN_TDP; tdpValue <= HudraSettings.MAX_TDP; tdpValue++)
            {
                var textBlock = new TextBlock
                {
                    Text = tdpValue.ToString(),
                    FontSize = 24,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Code"),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    Width = _dpiService.NumberWidth,
                    Opacity = tdpValue == _selectedTdp ? 1.0 : 0.4
                };
                textBlock.Tapped += Number_Tapped;
                NumbersPanel.Children.Add(textBlock);
            }

            // Add end padding
            var endPadding = new Border { Width = 100 };
            NumbersPanel.Children.Add(endPadding);
            _endPadding = endPadding;

            SetupScrollViewerEvents();

            Loaded += OnTdpPickerLoaded;
            TdpScrollViewer.SizeChanged += OnScrollViewerSizeChanged;
        }

        private void OnTdpPickerLoaded(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("TDP Picker: OnTdpPickerLoaded");
            SetupPaddingAndScroll();
        }

        private void OnScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("TDP Picker: OnScrollViewerSizeChanged");
            SetupPaddingAndScroll();
        }

        private void SetupPaddingAndScroll()
        {
            if (TdpScrollViewer.ActualWidth <= 0 || _dpiService == null || IsNavigating()) return;

            System.Diagnostics.Debug.WriteLine($"TDP Picker: SetupPaddingAndScroll - Width: {TdpScrollViewer.ActualWidth}");

            var startPaddingWidth = (TdpScrollViewer.ActualWidth - _dpiService.ItemWidth) / 2;

            if (_startPadding != null) _startPadding.Width = startPaddingWidth;
            if (_endPadding != null) _endPadding.Width = startPaddingWidth;

            NumbersPanel.UpdateLayout();

            // Always position to current TDP after layout AND ensure visual highlighting
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (sender, args) =>
            {
                timer.Stop();
                if (!IsNavigating())
                {
                    System.Diagnostics.Debug.WriteLine($"TDP Picker: Positioning scroll to TDP {_selectedTdp}");
                    ScrollToTdpSilently(_selectedTdp);

                    // Force visual update after scroll positioning
                    UpdateNumberOpacity();
                }
            };
            timer.Start();
        }

        private bool IsNavigating()
        {
            return _navigationService?.IsNavigating ?? false;
        }

        // Silent scroll that doesn't trigger events
        private void ScrollToTdpSilently(int tdpValue)
        {
            if (IsNavigating()) return;

            _suppressScrollEvents = true;
            ScrollToTdp(tdpValue);

            // Clear suppression after a short delay
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                _suppressScrollEvents = false;
            };
            timer.Start();
        }

        private void ScrollToTdp(int tdpValue)
        {
            if (_dpiService == null || TdpScrollViewer.ActualWidth <= 0) return;

            var tdpIndex = tdpValue - HudraSettings.MIN_TDP;
            var scrollViewerCenter = TdpScrollViewer.ActualWidth / 2;
            var startPadding = _startPadding?.Width ?? ((TdpScrollViewer.ActualWidth - _dpiService.ItemWidth) / 2);

            var numberLeftEdge = startPadding + (tdpIndex * _dpiService.ItemWidth);
            var numberCenter = numberLeftEdge + (_dpiService.ItemWidth / 2);
            var targetScrollPosition = numberCenter - scrollViewerCenter;

            var maxScroll = Math.Max(0, TdpScrollViewer.ExtentWidth - TdpScrollViewer.ViewportWidth);
            targetScrollPosition = Math.Max(0, Math.Min(maxScroll, targetScrollPosition));

            System.Diagnostics.Debug.WriteLine($"TDP Picker: ScrollToTdp({tdpValue}) - target position: {targetScrollPosition}");
            TdpScrollViewer.ScrollToHorizontalOffset(targetScrollPosition);
        }

        private int GetCenteredTdp()
        {
            if (_dpiService == null) return _selectedTdp;

            var scrollOffset = TdpScrollViewer.HorizontalOffset;
            var scrollViewerCenter = TdpScrollViewer.ActualWidth / 2;
            var startPadding = _startPadding?.Width ?? ((TdpScrollViewer.ActualWidth - _dpiService.ItemWidth) / 2);

            var visibleCenterPosition = scrollOffset + scrollViewerCenter;
            var adjustedPosition = visibleCenterPosition - startPadding - (_dpiService.ItemWidth / 2);
            var slotIndex = Math.Round(adjustedPosition / _dpiService.ItemWidth);

            var tdpValue = (int)(slotIndex + HudraSettings.MIN_TDP);
            return Math.Max(HudraSettings.MIN_TDP, Math.Min(HudraSettings.MAX_TDP, tdpValue));
        }

        private void TdpScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (_isScrolling || IsNavigating() || _suppressScrollEvents) return;

            var centeredTdp = GetCenteredTdp();
            if (centeredTdp != _selectedTdp)
            {
                System.Diagnostics.Debug.WriteLine($"TDP Picker: ViewChanged - updating TDP from {_selectedTdp} to {centeredTdp}");

                _suppressTdpChangeEvents = true;
                _selectedTdp = centeredTdp;
                UpdateNumberOpacity();
                OnPropertyChanged(nameof(SelectedTdp));
                _suppressTdpChangeEvents = false;

                // Only schedule hardware changes if auto-set is enabled
                if (_autoSetEnabled)
                {
                    _autoSetManager?.ScheduleUpdate(_selectedTdp);
                }

                TdpChanged?.Invoke(this, _selectedTdp);
            }

            if (!e.IsIntermediate)
            {
                SnapToCurrentTdp();
            }
        }

        private void SnapToCurrentTdp()
        {
            if (IsNavigating()) return;

            _isScrolling = true;
            ScrollToTdp(_selectedTdp);

            var timer = new DispatcherTimer { Interval = HudraSettings.SNAP_ANIMATION_DELAY };
            timer.Tick += (s, args) =>
            {
                _isScrolling = false;
                timer.Stop();
            };
            timer.Start();
        }

        private void UpdateNumberOpacity()
        {
            System.Diagnostics.Debug.WriteLine($"TDP Picker: UpdateNumberOpacity called - selected TDP: {_selectedTdp}");

            if (!_isInitialized)
            {
                System.Diagnostics.Debug.WriteLine("TDP Picker: UpdateNumberOpacity - not initialized, skipping");
                return;
            }

            for (int i = 1; i <= HudraSettings.TotalTdpCount; i++)
            {
                if (i < NumbersPanel.Children.Count - 1 && NumbersPanel.Children[i] is TextBlock textBlock)
                {
                    var tdpValue = (i - 1) + HudraSettings.MIN_TDP;
                    bool isSelected = tdpValue == _selectedTdp;

                    textBlock.Opacity = isSelected ? 1.0 : 0.4;
                    textBlock.FontSize = isSelected ? 28 : 24;

                    if (isSelected)
                    {
                        System.Diagnostics.Debug.WriteLine($"TDP Picker: Highlighting TDP {tdpValue} (opacity: 1.0, fontSize: 28)");
                    }
                }
            }
        }

        private async void LoadCurrentTdp()
        {
            System.Diagnostics.Debug.WriteLine($"TDP Picker: LoadCurrentTdp - autoSetEnabled: {_autoSetEnabled}");

            if (!_autoSetEnabled)
            {
                // Settings mode - always use startup TDP from settings
                int startupTdp = SettingsService.GetStartupTdp();
                System.Diagnostics.Debug.WriteLine($"TDP Picker: Settings mode - using startup TDP: {startupTdp}");
                SelectedTdp = startupTdp;
                StatusText = $"Default TDP: {startupTdp}W";

                // Force visual update immediately for settings mode
                UpdateNumberOpacity();

                // Schedule scroll positioning after layout
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (!IsNavigating())
                    {
                        System.Diagnostics.Debug.WriteLine($"TDP Picker: Settings mode - positioning scroll to {startupTdp}");
                        ScrollToTdpSilently(startupTdp);
                        UpdateNumberOpacity(); // Force highlighting after scroll
                    }
                });

                return;
            }

            // Main page mode - implement correct priority order
            try
            {
                int targetTdp;
                string statusReason;

                // Priority 1: Default TDP if toggle is enabled
                if (SettingsService.GetUseStartupTdp())
                {
                    targetTdp = SettingsService.GetStartupTdp();
                    statusReason = "using default TDP from settings";
                    System.Diagnostics.Debug.WriteLine($"TDP Picker: Priority 1 - Default TDP enabled, using: {targetTdp}W");
                }
                else
                {
                    // Priority 2: Last-Used TDP if default is disabled
                    targetTdp = SettingsService.GetLastUsedTdp();

                    // Priority 3: Fallback to 10W if last-used is invalid
                    if (targetTdp < HudraSettings.MIN_TDP || targetTdp > HudraSettings.MAX_TDP)
                    {
                        targetTdp = HudraSettings.DEFAULT_STARTUP_TDP; // 10W
                        statusReason = "using fallback (10W) - invalid last-used TDP";
                        System.Diagnostics.Debug.WriteLine($"TDP Picker: Priority 3 - Invalid last-used TDP, fallback to: {targetTdp}W");
                    }
                    else
                    {
                        statusReason = "using last-used TDP";
                        System.Diagnostics.Debug.WriteLine($"TDP Picker: Priority 2 - Last-used TDP: {targetTdp}W");
                    }
                }

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

                // Set the UI value FIRST before hardware operations
                System.Diagnostics.Debug.WriteLine($"TDP Picker: LoadCurrentTdp - setting SelectedTdp to {targetTdp}");
                SelectedTdp = targetTdp;

                // Force visual update immediately
                UpdateNumberOpacity();

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
                UpdateNumberOpacity(); // Force visual update on fallback too
                StatusText = $"TDP: {fallbackTdp}W (fallback due to error): {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"TDP Picker: LoadCurrentTdp - exception, using fallback: {fallbackTdp}W");
            }
        }

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

        private void SetupScrollViewerEvents()
        {
            double lastPointerX = 0;

            TdpScrollViewer.PointerPressed += (s, e) =>
            {
                if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse && !IsNavigating())
                {
                    lastPointerX = e.GetCurrentPoint(TdpScrollViewer).Position.X;
                    _isManualScrolling = true;
                    _isScrolling = true;
                    TdpScrollViewer.CapturePointer(e.Pointer);

                    if (e.OriginalSource is not TextBlock)
                    {
                        e.Handled = true;
                    }
                }
            };

            TdpScrollViewer.PointerMoved += (s, e) =>
            {
                if (_isManualScrolling && e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse && !IsNavigating())
                {
                    var currentX = e.GetCurrentPoint(TdpScrollViewer).Position.X;
                    var deltaX = lastPointerX - currentX;
                    deltaX *= 0.8;

                    var newScrollPosition = TdpScrollViewer.HorizontalOffset + deltaX;
                    var maxScroll = TdpScrollViewer.ExtentWidth - TdpScrollViewer.ViewportWidth;
                    newScrollPosition = Math.Max(0, Math.Min(maxScroll, newScrollPosition));

                    TdpScrollViewer.ScrollToHorizontalOffset(newScrollPosition);
                    lastPointerX = currentX;
                }
            };

            TdpScrollViewer.PointerReleased += (s, e) =>
            {
                if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse && _isManualScrolling)
                {
                    _isManualScrolling = false;
                    _isScrolling = false;
                    TdpScrollViewer.ReleasePointerCapture(e.Pointer);

                    if (!IsNavigating())
                    {
                        HandleScrollEnd();
                    }
                }
            };

            // Touch scroll end detection
            double lastScrollPosition = 0;
            DispatcherTimer? scrollStopTimer = null;

            TdpScrollViewer.ViewChanged += (s, e) =>
            {
                if (IsNavigating() || _suppressScrollEvents) return;

                var currentPosition = TdpScrollViewer.HorizontalOffset;
                if (!_isManualScrolling && Math.Abs(currentPosition - lastScrollPosition) > 0.1)
                {
                    lastScrollPosition = currentPosition;
                    scrollStopTimer?.Stop();
                    scrollStopTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                    scrollStopTimer.Tick += (sender, args) =>
                    {
                        scrollStopTimer.Stop();
                        if (!IsNavigating() && !_suppressScrollEvents)
                        {
                            HandleScrollEnd();
                        }
                    };
                    scrollStopTimer.Start();
                }
            };
        }

        private void HandleScrollEnd()
        {
            if (IsNavigating() || _suppressScrollEvents) return;

            var centeredTdp = GetCenteredTdp();
            if (centeredTdp != _selectedTdp)
            {
                _suppressTdpChangeEvents = true;
                _selectedTdp = centeredTdp;
                UpdateNumberOpacity();
                OnPropertyChanged(nameof(SelectedTdp));
                _suppressTdpChangeEvents = false;

                if (_autoSetEnabled)
                {
                    _autoSetManager?.ScheduleUpdate(_selectedTdp);
                }

                TdpChanged?.Invoke(this, _selectedTdp);
            }
            SnapToCurrentTdp();
        }

        private void Number_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (IsNavigating()) return;

            if (sender is TextBlock tb && int.TryParse(tb.Text, out int tdp))
            {
                System.Diagnostics.Debug.WriteLine($"TDP Picker: Number {tdp} tapped");
                SelectedTdp = tdp;

                if (_autoSetEnabled)
                {
                    _autoSetManager?.ScheduleUpdate(_selectedTdp);
                }

                SnapToCurrentTdp();
                e.Handled = true;
            }
        }

        // Called by pages when they become visible to ensure proper scroll positioning
        public void EnsureScrollPositionAfterLayout()
        {
            if (IsNavigating()) return;

            System.Diagnostics.Debug.WriteLine($"TDP Picker: EnsureScrollPositionAfterLayout called for TDP {_selectedTdp}");

            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (!IsNavigating() && TdpScrollViewer.ActualWidth > 0)
                {
                    ScrollToTdpSilently(_selectedTdp);
                    // Force visual highlighting after positioning
                    UpdateNumberOpacity();
                }
                else if (TdpScrollViewer.ActualWidth <= 0)
                {
                    // Wait for layout
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                    timer.Tick += (s, e) =>
                    {
                        timer.Stop();
                        if (!IsNavigating() && TdpScrollViewer.ActualWidth > 0)
                        {
                            ScrollToTdpSilently(_selectedTdp);
                            // Force visual highlighting after positioning
                            UpdateNumberOpacity();
                        }
                    };
                    timer.Start();
                }
            });
        }

        // Reset positioning flag when reinitializing
        public void ResetScrollPositioning()
        {
            System.Diagnostics.Debug.WriteLine("TDP Picker: ResetScrollPositioning called");
            _suppressScrollEvents = false;
            _suppressTdpChangeEvents = false;
        }

        public void Dispose()
        {
            _autoSetManager?.Dispose();

            // Clean up event handlers
            Loaded -= OnTdpPickerLoaded;
            TdpScrollViewer.SizeChanged -= OnScrollViewerSizeChanged;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}