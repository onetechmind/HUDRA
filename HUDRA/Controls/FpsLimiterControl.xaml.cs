using HUDRA.Models;
using HUDRA.Services;
using HUDRA.Interfaces;
using HUDRA.AttachedProperties;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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

    public sealed partial class FpsLimiterControl : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<FpsLimitChangedEventArgs>? FpsLimitChanged;

        private RtssFpsLimiterService? _fpsLimiterService;
        private HdrService? _hdrService;
        private FpsLimitSettings _fpsSettings = new();
        private bool _isRtssSupported = false;
        private bool _isRtssInstalled = false;
        private bool _isGameRunning = false;

        // HDR fields
        private bool _isHdrSupported = false;
        private bool _isUpdatingHdrToggle = false;
        private bool _cachedHdrState = false;
        private DispatcherTimer? _hdrPollTimer;

        // Slider state
        private int _maxFps = 120;
        private int _currentFps = 0;
        private bool _isUpdatingSlider = false;

        // Debounce timer — applies the FPS limit to RTSS after the user stops changing
        private DispatcherTimer? _fpsDebounceTimer;
        private int _pendingFps = -1;

        public int MaxFps
        {
            get => _maxFps;
            private set
            {
                if (_maxFps != value)
                {
                    _maxFps = value;
                    OnPropertyChanged();
                }
            }
        }

        public FpsLimitSettings FpsSettings
        {
            get => _fpsSettings;
            set
            {
                if (_fpsSettings != value)
                {
                    _fpsSettings = value;
                    OnPropertyChanged();
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

        public bool IsRtssNotFound => !_isRtssSupported;
        public bool IsRtssNotInstalled => !_isRtssInstalled;

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

        public FpsLimiterControl()
        {
            this.InitializeComponent();
            this.DataContext = this;

            IsRtssInstalled = RtssFpsLimiterService.GetCachedInstallationStatus();

            // Debounce: 500 ms after user stops changing before sending to RTSS
            _fpsDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _fpsDebounceTimer.Tick += FpsDebounceTimer_Tick;
        }

        public async void Initialize(RtssFpsLimiterService fpsLimiterService, HdrService hdrService)
        {
            _fpsLimiterService = fpsLimiterService;
            _hdrService = hdrService;

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

            LoadHdrState();
            StartHdrPolling();
        }

        public void RefreshHdrState() => LoadHdrState();

        private void LoadHdrState()
        {
            if (_hdrService == null) return;

            try
            {
                _isUpdatingHdrToggle = true;
                IsHdrSupported = _hdrService.IsHdrSupported();
                _cachedHdrState = _hdrService.IsHdrEnabled();

                if (HdrToggle != null)
                    HdrToggle.IsOn = _cachedHdrState;

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
            _hdrPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _hdrPollTimer.Tick += OnHdrPollTimerTick;
            _hdrPollTimer.Start();
        }

        private void OnHdrPollTimerTick(object? sender, object e)
        {
            if (_hdrService == null || _isUpdatingHdrToggle) return;

            try
            {
                bool currentHdrState = _hdrService.IsHdrEnabled();
                if (currentHdrState != _cachedHdrState)
                {
                    _isUpdatingHdrToggle = true;
                    _cachedHdrState = currentHdrState;
                    if (HdrToggle != null) HdrToggle.IsOn = currentHdrState;
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
            if (_isUpdatingHdrToggle || _hdrService == null || HdrToggle == null) return;

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
                    _isUpdatingHdrToggle = true;
                    HdrToggle.IsOn = !HdrToggle.IsOn;
                    _isUpdatingHdrToggle = false;
                    System.Diagnostics.Debug.WriteLine("FpsLimiterControl: Failed to change HDR state, reverted toggle");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FpsLimiterControl: Failed to toggle HDR: {ex.Message}");
                _isUpdatingHdrToggle = true;
                HdrToggle.IsOn = !HdrToggle.IsOn;
                _isUpdatingHdrToggle = false;
            }
        }

        // ── Slider value changed ─────────────────────────────────────────────────

        private void OnFpsSliderValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isUpdatingSlider) return;

            int newFps = (int)Math.Clamp(e.NewValue, 0, _maxFps);
            if (newFps == _currentFps) return;

            // Update label immediately for responsiveness
            if (FpsValueLabel != null)
                FpsValueLabel.Text = newFps == 0 ? "Off" : $"{newFps} FPS";

            // Restart debounce so we only hit RTSS after the user settles
            _pendingFps = newFps;
            _fpsDebounceTimer?.Stop();
            _fpsDebounceTimer?.Start();
        }

        private void FpsDebounceTimer_Tick(object? sender, object e)
        {
            _fpsDebounceTimer?.Stop();
            if (_pendingFps < 0) return;

            var fps = _pendingFps;
            _pendingFps = -1;
            _currentFps = fps;
            _fpsSettings.SelectedFpsLimit = fps;

            FpsLimitChanged?.Invoke(this, new FpsLimitChangedEventArgs(fps));
            System.Diagnostics.Debug.WriteLine($"FpsLimiter: Debounced — applying {(fps == 0 ? "Unlimited" : fps + " FPS")}");
        }

        // ── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Derives the NumberBox maximum from the list of pre-calculated options (max non-zero value = refresh rate).
        /// Called by MainWindow when RTSS and resolution are ready.
        /// </summary>
        public void UpdateFpsOptions(List<int> fpsOptions)
        {
            _fpsSettings.AvailableFpsOptions = fpsOptions;
            var maxFps = fpsOptions.Where(x => x > 0).DefaultIfEmpty(120).Max();
            MaxFps = maxFps;

            // Sync the NumberBox to whatever value is already selected
            SyncToFpsLimit(_fpsSettings.SelectedFpsLimit);
        }

        /// <summary>
        /// Syncs the UI to an FPS limit set externally (e.g. by a game profile) without triggering hardware changes.
        /// </summary>
        public void SyncToFpsLimit(int fpsLimit)
        {
            var clamped = Math.Clamp(fpsLimit, 0, _maxFps);
            _currentFps = clamped;
            _fpsSettings.SelectedFpsLimit = clamped;

            _isUpdatingSlider = true;
            if (FpsSlider != null) FpsSlider.Value = clamped;
            if (FpsValueLabel != null) FpsValueLabel.Text = clamped == 0 ? "Off" : $"{clamped} FPS";
            _isUpdatingSlider = false;

            System.Diagnostics.Debug.WriteLine($"FpsLimiter synced to: {(clamped == 0 ? "Off" : clamped + " FPS")} (external)");
        }

        public async Task RefreshRtssStatus()
        {
            if (_fpsLimiterService == null) return;

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
            if (_fpsLimiterService == null) return;

            try
            {
                var detection = await _fpsLimiterService.SmartRefreshRtssStatusAsync();
                var newIsRtssInstalled = detection.IsInstalled;
                var newIsRtssSupported = detection.IsInstalled && detection.IsRunning;

                if (_isRtssInstalled != newIsRtssInstalled)
                {
                    IsRtssInstalled = newIsRtssInstalled;
                    System.Diagnostics.Debug.WriteLine($"RTSS installation status changed to: {newIsRtssInstalled}");
                }

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
                if (_isRtssInstalled) IsRtssInstalled = false;
                if (_isRtssSupported) IsRtssSupported = false;
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

            if (_fpsDebounceTimer != null)
            {
                _fpsDebounceTimer.Stop();
                _fpsDebounceTimer.Tick -= FpsDebounceTimer_Tick;
                _fpsDebounceTimer = null;
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
