using HUDRA.Models;
using HUDRA.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace HUDRA.Pages
{
    public sealed partial class ScalingPage : Page
    {
        public ScalingPageViewModel ViewModel { get; private set; }

        public ScalingPage()
        {
            this.InitializeComponent();
            ViewModel = new ScalingPageViewModel();
            this.DataContext = ViewModel;
        }

        public void Initialize()
        {
            ViewModel.Initialize();
        }

        private async void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.IsApplying = true;
            
            // Small delay to ensure UI updates immediately
            await Task.Delay(1);
            
            await ViewModel.ApplySettingsAsync();
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ResetSettings();
        }

        private async void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.IsApplying = true;
            
            // Small delay to ensure UI updates immediately
            await Task.Delay(1);
            
            await ViewModel.RestoreUserSettingsAsync();
        }
    }

    public class ScalingPageViewModel : INotifyPropertyChanged
    {
        private readonly LosslessScalingSettingsService _settingsService;
        private readonly ResolutionService _resolutionService;
        private LosslessScalingSettings _settings;
        private bool _isApplying;
        private string _statusMessage = "Processing...";
        private string _optimalGameResolution = "1280×720";
        private string _nativeResolution = "1920×1080";
        private bool _losslessScalingSectionExpanded = true;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ScalingPageViewModel()
        {
            var losslessScalingService = new LosslessScalingService();
            _settingsService = new LosslessScalingSettingsService(losslessScalingService);
            _resolutionService = new ResolutionService();
            _settings = new LosslessScalingSettings();
            
            ApplySettingsCommand = new RelayCommand(async () => await ApplySettingsAsync());
            ResetSettingsCommand = new RelayCommand(ResetSettings);
            
            // Calculate optimal game resolution on startup
            CalculateOptimalResolutions();
        }

        public bool UpscalingEnabled
        {
            get => _settings.UpscalingEnabled;
            set
            {
                if (_settings.UpscalingEnabled != value)
                {
                    _settings.UpscalingEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        public int FrameGenSelectedIndex
        {
            get => (int)_settings.FrameGenMultiplier + 1; // Convert enum (0,1,2,3) to slider (1,2,3,4)
            set
            {
                var enumValue = value - 1; // Convert slider (1,2,3,4) to enum (0,1,2,3)
                var newValue = (LosslessScalingFrameGen)enumValue;
                if (_settings.FrameGenMultiplier != newValue)
                {
                    _settings.FrameGenMultiplier = newValue;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FrameGenLabel));
                }
            }
        }

        public string FrameGenLabel
        {
            get => GetFrameGenDisplayText(_settings.FrameGenMultiplier);
        }

        public int FlowScale
        {
            get => _settings.FlowScale;
            set
            {
                var clampedValue = Math.Clamp(value, 0, 100);
                if (_settings.FlowScale != clampedValue)
                {
                    _settings.FlowScale = clampedValue;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsApplying
        {
            get => _isApplying;
            set
            {
                if (_isApplying != value)
                {
                    _isApplying = value;
                    OnPropertyChanged();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        public string OptimalGameResolution
        {
            get => _optimalGameResolution;
            set
            {
                if (_optimalGameResolution != value)
                {
                    _optimalGameResolution = value;
                    OnPropertyChanged();
                }
            }
        }

        public string NativeResolution
        {
            get => _nativeResolution;
            set
            {
                if (_nativeResolution != value)
                {
                    _nativeResolution = value;
                    OnPropertyChanged();
                }
            }
        }

        public RelayCommand ApplySettingsCommand { get; }
        public RelayCommand ResetSettingsCommand { get; }

        public bool LosslessScalingSectionExpanded
        {
            get => _losslessScalingSectionExpanded;
            set
            {
                if (_losslessScalingSectionExpanded != value)
                {
                    _losslessScalingSectionExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        public void Initialize()
        {
            LoadSettings();
        }

        private void LoadSettings()
        {
            try
            {
                _settings = SettingsService.GetLosslessScalingSettings();
                OnPropertyChanged(nameof(UpscalingEnabled));
                OnPropertyChanged(nameof(FrameGenSelectedIndex));
                OnPropertyChanged(nameof(FrameGenLabel));
                OnPropertyChanged(nameof(FlowScale));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading settings: {ex.Message}");
            }
        }

        public async Task ApplySettingsAsync()
        {
            try
            {
                StatusMessage = "Restarting Lossless Scaling...";
                
                // Save to app settings first
                SettingsService.SetLosslessScalingSettings(_settings);
                
                // Apply to Lossless Scaling
                await _settingsService.ApplySettingsAndRestartAsync(_settings);
            }
            catch (Exception ex)
            {
                StatusMessage = "Error applying settings";
                System.Diagnostics.Debug.WriteLine($"Error applying settings: {ex.Message}");
                // TODO: Show error dialog to user
            }
            finally
            {
                IsApplying = false;
            }
        }

        public async Task RestoreUserSettingsAsync()
        {
            try
            {
                StatusMessage = "Restoring backup settings...";
                
                // Restore user's backed up settings
                await _settingsService.RestoreUserSettingsAsync();
                
                // Reload current settings from the restored file
                LoadSettings();
            }
            catch (Exception ex)
            {
                StatusMessage = "Error restoring settings";
                System.Diagnostics.Debug.WriteLine($"Error restoring settings: {ex.Message}");
                // TODO: Show error dialog to user
            }
            finally
            {
                IsApplying = false;
            }
        }

        public void ResetSettings()
        {
            _settings = new LosslessScalingSettings();
            OnPropertyChanged(nameof(UpscalingEnabled));
            OnPropertyChanged(nameof(FrameGenSelectedIndex));
            OnPropertyChanged(nameof(FrameGenLabel));
            OnPropertyChanged(nameof(FlowScale));
        }

        private void CalculateOptimalResolutions()
        {
            try
            {
                var (success, currentRes, _) = _resolutionService.GetCurrentResolution();
                if (success && currentRes != null)
                {
                    NativeResolution = $"{currentRes.Width}×{currentRes.Height}";
                    OptimalGameResolution = CalculateOptimalGameResolution(currentRes.Width, currentRes.Height);
                }
                else
                {
                    // Fallback values
                    NativeResolution = "1920×1080";
                    OptimalGameResolution = "1280×720";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error calculating optimal resolution: {ex.Message}");
                // Use safe fallback values
                NativeResolution = "1920×1080";
                OptimalGameResolution = "1280×720";
            }
        }

        private string CalculateOptimalGameResolution(int nativeWidth, int nativeHeight)
        {
            // Calculate aspect ratio
            double aspectRatio = (double)nativeWidth / nativeHeight;
            
            // Define common gaming resolutions with their aspect ratios
            var gameResolutions = new[]
            {
                // 16:9 resolutions
                new { Width = 1280, Height = 720, AspectRatio = 16.0/9.0, Name = "720p" },
                new { Width = 1920, Height = 1080, AspectRatio = 16.0/9.0, Name = "1080p" },
                new { Width = 960, Height = 540, AspectRatio = 16.0/9.0, Name = "540p" },
                
                // 16:10 resolutions  
                new { Width = 1280, Height = 800, AspectRatio = 16.0/10.0, Name = "800p" },
                new { Width = 1920, Height = 1200, AspectRatio = 16.0/10.0, Name = "1200p" },
                new { Width = 960, Height = 600, AspectRatio = 16.0/10.0, Name = "600p" },
                
                // 21:9 ultrawide
                new { Width = 1720, Height = 720, AspectRatio = 21.0/9.0, Name = "720p UW" },
                new { Width = 2580, Height = 1080, AspectRatio = 21.0/9.0, Name = "1080p UW" },
                
                // 4:3 resolutions
                new { Width = 1024, Height = 768, AspectRatio = 4.0/3.0, Name = "768p" },
                new { Width = 1280, Height = 960, AspectRatio = 4.0/3.0, Name = "960p" }
            };
            
            // Find the best matching resolution - prioritize perfect integer scaling and smallest resolution
            var bestMatch = gameResolutions
                .Where(res => Math.Abs(res.AspectRatio - aspectRatio) < 0.01) // Same aspect ratio
                .Where(res => res.Width <= nativeWidth && res.Height <= nativeHeight) // Fits in native
                .Where(res => 
                    (nativeWidth % res.Width == 0 && nativeHeight % res.Height == 0) || // Perfect integer scaling
                    (Math.Abs(nativeWidth / (double)res.Width - nativeHeight / (double)res.Height) < 0.01)) // Even scaling factor
                .OrderBy(res => (nativeWidth % res.Width == 0 && nativeHeight % res.Height == 0) ? 0 : 1) // Perfect integer scaling first
                .ThenByDescending(res => nativeWidth / (double)res.Width) // Then largest scaling factor (smallest resolution)
                .FirstOrDefault();
            
            if (bestMatch != null)
            {
                return $"{bestMatch.Width}×{bestMatch.Height}";
            }
            
            // Fallback: calculate half resolution with same aspect ratio
            int fallbackWidth = nativeWidth / 2;
            int fallbackHeight = nativeHeight / 2;
            
            // Round to even numbers for better scaling
            fallbackWidth = (fallbackWidth / 2) * 2;
            fallbackHeight = (fallbackHeight / 2) * 2;
            
            return $"{fallbackWidth}×{fallbackHeight}";
        }

        private static string GetFrameGenDisplayText(LosslessScalingFrameGen frameGen)
        {
            return frameGen switch
            {
                LosslessScalingFrameGen.Disabled => "Off",
                LosslessScalingFrameGen.TwoX => "2x",
                LosslessScalingFrameGen.ThreeX => "3x",
                LosslessScalingFrameGen.FourX => "4x",
                _ => "Off"
            };
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RelayCommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object? parameter) => _execute();

        public event EventHandler? CanExecuteChanged;
    }
}