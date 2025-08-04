using HUDRA.Models;
using HUDRA.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace HUDRA.Controls
{
    public sealed partial class FpsLimiterControl : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<FpsLimitChangedEventArgs>? FpsLimitChanged;

        private RtssFpsLimiterService? _fpsLimiterService;
        private FpsLimitSettings _fpsSettings = new();
        private bool _isRtssSupported = false;
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

        public bool IsRtssNotFound
        {
            get => !_isRtssSupported;
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
        }

        public async void Initialize(RtssFpsLimiterService fpsLimiterService)
        {
            _fpsLimiterService = fpsLimiterService;
            await RefreshRtssStatus();
        }

        public async Task RefreshRtssStatus()
        {
            if (_fpsLimiterService == null)
                return;

            try
            {
                var detection = await _fpsLimiterService.DetectRtssInstallationAsync(forceRefresh: true);
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
                IsRtssSupported = false;
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