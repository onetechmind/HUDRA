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
        private TdpAutoSetManager? _autoSetManager;
        private bool _autoSetEnabled = true;
        private Border? _startPadding;
        private Border? _endPadding;
        private int _selectedTdp = HudraSettings.DEFAULT_STARTUP_TDP;
        private bool _isScrolling = false;
        private bool _isManualScrolling = false; // Add this for mouse drag detection

        private string _statusText = "Current TDP: Not Set";
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
                    _selectedTdp = value;
                    UpdateNumberOpacity();
                    ScrollToTdp(_selectedTdp);
                    OnPropertyChanged();
                    TdpChanged?.Invoke(this, _selectedTdp);
                }
            }
        }

        public Border PickerBorder => TdpPickerBorder;

        public TdpPickerControl()
        {
            this.InitializeComponent();
            // Don't initialize here - wait for explicit Initialize() call
        }

        public void Initialize(DpiScalingService dpiService, bool autoSetEnabled = true)
        {
            _dpiService = dpiService ?? throw new ArgumentNullException(nameof(dpiService));
            _autoSetEnabled = autoSetEnabled;
            _autoSetManager = autoSetEnabled ? new TdpAutoSetManager(SetTdpAsync, status => StatusText = status) : null;

            InitializePicker();
            LoadCurrentTdp();
        }

        private void InitializePicker()
        {
            if (_dpiService == null) return;

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

            Loaded += (s, e) => SetupPaddingAndScroll();
            TdpScrollViewer.SizeChanged += (s, e) => SetupPaddingAndScroll();
        }

        private void SetupPaddingAndScroll()
        {
            if (TdpScrollViewer.ActualWidth <= 0 || _dpiService == null) return;

            var startPaddingWidth = (TdpScrollViewer.ActualWidth - _dpiService.ItemWidth) / 2;

            if (_startPadding != null) _startPadding.Width = startPaddingWidth;
            if (_endPadding != null) _endPadding.Width = startPaddingWidth;

            NumbersPanel.UpdateLayout();

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (sender, args) =>
            {
                timer.Stop();
                ScrollToTdp(_selectedTdp);
            };
            timer.Start();
        }

        private void ScrollToTdp(int tdpValue)
        {
            if (_dpiService == null) return;

            var tdpIndex = tdpValue - HudraSettings.MIN_TDP;
            var scrollViewerCenter = TdpScrollViewer.ActualWidth / 2;
            var startPadding = _startPadding?.Width ?? ((TdpScrollViewer.ActualWidth - _dpiService.ItemWidth) / 2);

            var numberLeftEdge = startPadding + (tdpIndex * _dpiService.ItemWidth);
            var numberCenter = numberLeftEdge + (_dpiService.ItemWidth / 2);
            var targetScrollPosition = numberCenter - scrollViewerCenter;

            var maxScroll = Math.Max(0, TdpScrollViewer.ExtentWidth - TdpScrollViewer.ViewportWidth);
            targetScrollPosition = Math.Max(0, Math.Min(maxScroll, targetScrollPosition));

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
            if (_isScrolling) return;

            var centeredTdp = GetCenteredTdp();
            if (centeredTdp != _selectedTdp)
            {
                _selectedTdp = centeredTdp;
                UpdateNumberOpacity();
                _autoSetManager?.ScheduleUpdate(_selectedTdp);
                TdpChanged?.Invoke(this, _selectedTdp);
            }

            if (!e.IsIntermediate)
            {
                SnapToCurrentTdp();
            }
        }

        private void SnapToCurrentTdp()
        {
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
            for (int i = 1; i <= HudraSettings.TotalTdpCount; i++)
            {
                if (i < NumbersPanel.Children.Count - 1 && NumbersPanel.Children[i] is TextBlock textBlock)
                {
                    var tdpValue = (i - 1) + HudraSettings.MIN_TDP;
                    textBlock.Opacity = tdpValue == _selectedTdp ? 1.0 : 0.4;
                    textBlock.FontSize = tdpValue == _selectedTdp ? 28 : 24;
                }
            }
        }

        private async void LoadCurrentTdp()
        {
            if (!_autoSetEnabled)
            {
                int startupTdp = SettingsService.GetStartupTdp();
                SelectedTdp = startupTdp;
                StatusText = $"Default TDP: {startupTdp}W";
                TdpChanged?.Invoke(this, _selectedTdp);
                return;
            }

            try
            {
                var tdpService = new TDPService();
                StatusText = $"TDP Service: {tdpService.InitializationStatus}";

                var result = tdpService.GetCurrentTdp();

                int targetTdp;
                if (result.Success && result.TdpWatts >= HudraSettings.MIN_TDP && result.TdpWatts <= HudraSettings.MAX_TDP)
                {
                    targetTdp = result.TdpWatts;
                    StatusText = $"Current TDP: {targetTdp}W ({(tdpService.IsDllMode ? "DLL-FAST" : "EXE-SLOW")})";
                }
                else
                {
                    targetTdp = SettingsService.GetStartupTdp();
                    StatusText = $"TDP defaulted to {targetTdp}W - {result.Message}";
                }

                // Always force set TDP on startup to ensure all limits match
                DispatcherQueue.TryEnqueue(async () =>
                {
                    await Task.Delay(200); // Small delay

                    using var setService = new TDPService();
                    var setResult = setService.SetTdp(targetTdp * 1000);

                    if (setResult.Success)
                    {
                        StatusText = $"TDP synchronized: {targetTdp}W (all limits)";
                    }
                    else
                    {
                        StatusText = $"TDP sync failed: {setResult.Message}";
                    }
                });

                // Set the UI value and trigger events
                SelectedTdp = targetTdp;
                TdpChanged?.Invoke(this, _selectedTdp);

                tdpService.Dispose();
            }
            catch (Exception ex)
            {
                int fallbackTdp = SettingsService.GetStartupTdp();
                SelectedTdp = fallbackTdp;
                StatusText = $"Error reading TDP (defaulted to {fallbackTdp}W): {ex.Message}";
                TdpChanged?.Invoke(this, _selectedTdp);
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

                if (_autoSetManager != null)
                {
                    // Schedule the update with a slight delay
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

            // Mouse/pen pointer events for drag scrolling
            TdpScrollViewer.PointerPressed += (s, e) =>
            {
                if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse)
                {
                    lastPointerX = e.GetCurrentPoint(TdpScrollViewer).Position.X;
                    _isManualScrolling = true;
                    _isScrolling = true;
                    TdpScrollViewer.CapturePointer(e.Pointer);

                    // Only mark the event as handled when the press isn't on a number
                    // so clicks on TextBlocks still raise their Tapped events
                    if (e.OriginalSource is not TextBlock)
                    {
                        e.Handled = true;
                    }
                }
            };

            TdpScrollViewer.PointerMoved += (s, e) =>
            {
                if (_isManualScrolling && e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse)
                {
                    var currentX = e.GetCurrentPoint(TdpScrollViewer).Position.X;
                    var deltaX = lastPointerX - currentX;
                    deltaX *= 0.8; // Adjust sensitivity

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
                    HandleScrollEnd();
                }
            };

            // Touch scroll end detection
            double lastScrollPosition = 0;
            DispatcherTimer? scrollStopTimer = null;

            TdpScrollViewer.ViewChanged += (s, e) =>
            {
                var currentPosition = TdpScrollViewer.HorizontalOffset;
                if (!_isManualScrolling && Math.Abs(currentPosition - lastScrollPosition) > 0.1)
                {
                    lastScrollPosition = currentPosition;
                    scrollStopTimer?.Stop();
                    scrollStopTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                    scrollStopTimer.Tick += (sender, args) =>
                    {
                        scrollStopTimer.Stop();
                        HandleScrollEnd();
                    };
                    scrollStopTimer.Start();
                }
            };
        }

        private void HandleScrollEnd()
        {
            var centeredTdp = GetCenteredTdp();
            if (centeredTdp != _selectedTdp)
            {
                _selectedTdp = centeredTdp;
                UpdateNumberOpacity();
                _autoSetManager?.ScheduleUpdate(_selectedTdp);
                TdpChanged?.Invoke(this, _selectedTdp);
            }
            SnapToCurrentTdp();
        }

        private void Number_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is TextBlock tb && int.TryParse(tb.Text, out int tdp))
            {
                SelectedTdp = tdp;
                _autoSetManager?.ScheduleUpdate(_selectedTdp);
                SnapToCurrentTdp();
                e.Handled = true;
            }
        }

        // Cleanup method
        public void Dispose()
        {
            _autoSetManager?.Dispose();
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}