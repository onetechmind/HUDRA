using HUDRA.Models;
using HUDRA.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
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
        private FpsLimitSettings _fpsSettings = new();
        private bool _isRtssSupported = false;
        private bool _isRtssInstalled = false;
        private bool _isGameRunning = false;

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

        public FpsLimiterControl()
        {
            this.InitializeComponent();
            this.DataContext = this;
            
            // Set initial installation status from cache (preloaded in App.xaml.cs)
            IsRtssInstalled = RtssFpsLimiterService.GetCachedInstallationStatus();
        }

        public async void Initialize(RtssFpsLimiterService fpsLimiterService)
        {
            _fpsLimiterService = fpsLimiterService;
            
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
                var selectedFps = _fpsSettings.AvailableFpsOptions[FpsLimitComboBox.SelectedIndex];
                if (selectedFps != _fpsSettings.SelectedFpsLimit)
                {
                    _fpsSettings.SelectedFpsLimit = selectedFps;
                    OnPropertyChanged(nameof(FpsSettings));
                    
                    // Always notify of the change - the handler will decide whether to apply
                    FpsLimitChanged?.Invoke(this, new FpsLimitChangedEventArgs(selectedFps));
                }
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