using HUDRA.Configuration;
using HUDRA.Services;
using HUDRA.Interfaces;
using HUDRA.AttachedProperties;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HUDRA.Controls
{
    public sealed partial class AudioControlsControl : UserControl, INotifyPropertyChanged, IGamepadNavigable
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<AudioStateChangedEventArgs>? AudioStateChanged;

        private AudioService? _audioService;
        private bool _isUpdatingSlider = false;
        private double _previousVolumeLevel = 50.0; // Remember volume before muting
        private GamepadNavigationService? _gamepadNavigationService;
        private int _currentFocusedControl = 0; // 0 = MuteButton, 1 = VolumeSlider
        private bool _isFocused = false;
        private bool _isSliderActivated = false;

        private string _audioStatusText = "Audio: Not Set";
        public string AudioStatusText
        {
            get => _audioStatusText;
            set
            {
                if (_audioStatusText != value)
                {
                    _audioStatusText = value;
                    OnPropertyChanged();
                }
            }
        }

        // IGamepadNavigable implementation
        public bool CanNavigateUp => false;
        public bool CanNavigateDown => false;
        public bool CanNavigateLeft => _currentFocusedControl == 1; // Can move left from Slider to Button
        public bool CanNavigateRight => _currentFocusedControl == 0; // Can move right from Button to Slider
        public bool CanActivate => true;
        public FrameworkElement NavigationElement => this;
        
        // Slider-specific interface implementations
        public bool IsSlider => _currentFocusedControl == 1; // True when VolumeSlider is focused
        public bool IsSliderActivated 
        { 
            get => _isSliderActivated; 
            set 
            { 
                _isSliderActivated = value;
                OnPropertyChanged(nameof(SliderFocusBrush));
            } 
        }
        
        // ComboBox interface implementations - AudioControl has no ComboBoxes
        public bool HasComboBoxes => false;
        public bool IsComboBoxOpen { get; set; } = false;
        public ComboBox? GetFocusedComboBox() => null;
        public int ComboBoxOriginalIndex { get; set; } = -1;
        public bool IsNavigatingComboBox { get; set; } = false;
        public void ProcessCurrentSelection() { /* Not applicable - no ComboBoxes */ }

        public Brush FocusBorderBrush
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true)
                {
                    return new SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
                }
                return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        public Thickness FocusBorderThickness
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true)
                {
                    return new Thickness(2);
                }
                return new Thickness(0);
            }
        }

        public Brush MuteButtonFocusBrush
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

        public Brush SliderFocusBrush
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true && _currentFocusedControl == 1)
                {
                    // Different color when slider is activated for value adjustment
                    return new SolidColorBrush(_isSliderActivated ? Microsoft.UI.Colors.DodgerBlue : Microsoft.UI.Colors.MediumOrchid);
                }
                return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }


        public AudioControlsControl()
        {
            this.InitializeComponent();
        }

        public void Initialize()
        {
            _audioService = new AudioService();

            // Get gamepad service
            if (Application.Current is App app && app.MainWindow is MainWindow mainWindow)
            {
                _gamepadNavigationService = mainWindow.GamepadNavigationService;
            }

            SetupEventHandlers();
            LoadCurrentAudioState();
        }

        private void SetupEventHandlers()
        {
            if (MuteButton != null)
            {
                MuteButton.Click += OnMuteButtonClick;
            }

            if (VolumeSlider != null)
            {
                VolumeSlider.ValueChanged += OnVolumeSliderValueChanged;
            }
        }

        private void OnMuteButtonClick(object sender, RoutedEventArgs e)
        {
            if (_audioService == null) return;

            try
            {
                bool currentlyMuted = _audioService.GetMuteStatus();

                // If we're muting, remember the current volume level
                if (!currentlyMuted && VolumeSlider != null && VolumeSlider.Value > 0)
                {
                    _previousVolumeLevel = VolumeSlider.Value;
                }

                // Explicitly set the mute state
                _audioService.SetMute(!currentlyMuted);

                // Give the system a moment to update then refresh the UI
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
                {
                    UpdateMuteButtonIcon();
                    UpdateVolumeSliderForMuteState();

                    bool isNowMuted = _audioService.GetMuteStatus();
                    AudioStateChanged?.Invoke(this, new AudioStateChangedEventArgs(
                        isNowMuted,
                        VolumeSlider?.Value ?? 0,
                        $"Audio {(isNowMuted ? "muted" : "unmuted")}"
                    ));

                    AudioStatusText = isNowMuted ? "Audio: Muted" : $"Audio: {(int)(VolumeSlider?.Value ?? 0)}%";
                });
            }
            catch (Exception ex)
            {
                AudioStatusText = $"Audio Error: {ex.Message}";
            }
        }

        private void OnVolumeSliderValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_audioService == null || _isUpdatingSlider) return;

            try
            {
                // If system is muted and user moves slider, unmute first
                bool isMuted = _audioService.GetMuteStatus();
                if (isMuted && e.NewValue > 0)
                {
                    _audioService.SetMute(false); // Unmute explicitly
                    UpdateMuteButtonIcon();

                    AudioStateChanged?.Invoke(this, new AudioStateChangedEventArgs(
                        false,
                        e.NewValue,
                        $"Audio unmuted via slider"
                    ));
                }

                // Set the actual volume level
                var level = (float)(e.NewValue / 100.0);
                _audioService.SetMasterVolumeScalar(level);

                // Update status (only if not muted)
                if (!_audioService.GetMuteStatus())
                {
                    AudioStatusText = $"Audio: {(int)e.NewValue}%";

                    AudioStateChanged?.Invoke(this, new AudioStateChangedEventArgs(
                        false,
                        e.NewValue,
                        $"Volume: {(int)e.NewValue}%"
                    ));
                }

                // Remember this level for future unmuting
                if (e.NewValue > 0)
                {
                    _previousVolumeLevel = e.NewValue;
                }
            }
            catch (Exception ex)
            {
                AudioStatusText = $"Audio Error: {ex.Message}";
            }
        }

        private void UpdateMuteButtonIcon()
        {
            if (_audioService == null || MuteButtonIcon == null) return;

            try
            {
                bool isMuted = _audioService.GetMuteStatus();

                // Force icon update on UI thread
                DispatcherQueue.TryEnqueue(() =>
                {
                    MuteButtonIcon.Glyph = isMuted ? "\uE74F" : "\uE767"; // Mute : Volume
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating mute button icon: {ex.Message}");
            }
        }

        private void UpdateVolumeSliderForMuteState()
        {
            if (_audioService == null || VolumeSlider == null) return;

            bool isMuted = _audioService.GetMuteStatus();

            _isUpdatingSlider = true; // Prevent ValueChanged events during update

            if (isMuted)
            {
                // Show 0 on slider when muted, but remember actual volume
                if (VolumeSlider.Value > 0)
                {
                    _previousVolumeLevel = VolumeSlider.Value;
                }
                VolumeSlider.Value = 0;
            }
            else
            {
                // Restore previous volume level when unmuted
                if (_previousVolumeLevel > 0)
                {
                    VolumeSlider.Value = _previousVolumeLevel;
                }
                else
                {
                    // Fallback: get actual system volume
                    var systemVolume = _audioService.GetMasterVolumeScalar() * 100.0;
                    VolumeSlider.Value = systemVolume;
                }
            }

            _isUpdatingSlider = false;
        }

        private void LoadCurrentAudioState()
        {
            if (_audioService == null) return;

            try
            {
                // Get current system volume
                var initialVolume = _audioService.GetMasterVolumeScalar() * 100.0;

                _isUpdatingSlider = true;
                if (VolumeSlider != null)
                {
                    VolumeSlider.Value = initialVolume;
                }
                _isUpdatingSlider = false;

                _previousVolumeLevel = initialVolume;

                // Update mute button state
                UpdateMuteButtonIcon();

                // Update slider for mute state
                UpdateVolumeSliderForMuteState();

                // Set initial status
                bool isMuted = _audioService.GetMuteStatus();
                AudioStatusText = isMuted ? "Audio: Muted" : $"Audio: {(int)initialVolume}%";

                // Fire initial state event
                AudioStateChanged?.Invoke(this, new AudioStateChangedEventArgs(
                    isMuted,
                    initialVolume,
                    AudioStatusText
                ));
            }
            catch (Exception ex)
            {
                AudioStatusText = $"Audio Error: {ex.Message}";
            }
        }


        public void Dispose()
        {
            // No auto-set managers to dispose for audio controls
        }

        // IGamepadNavigable event handlers
        public void OnGamepadNavigateUp() { }
        public void OnGamepadNavigateDown() { }
        
        public void OnGamepadNavigateLeft()
        {
            if (_currentFocusedControl == 1) // From Slider to MuteButton
            {
                _currentFocusedControl = 0;
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 Audio: Moved left to Mute Button");
            }
        }

        public void OnGamepadNavigateRight()
        {
            if (_currentFocusedControl == 0) // From MuteButton to Slider
            {
                _currentFocusedControl = 1;
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 Audio: Moved right to Volume Slider");
            }
        }

        public void OnGamepadActivate()
        {
            if (_currentFocusedControl == 0) // MuteButton
            {
                OnMuteButtonClick(MuteButton, new RoutedEventArgs());
                System.Diagnostics.Debug.WriteLine($"🎮 Audio: Triggered Mute Button");
            }
            // Slider focus/interaction is handled by the slider itself when focused
        }

        public void OnGamepadFocusReceived()
        {
            _isFocused = true;
            _currentFocusedControl = 0; // Start with MuteButton
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 Audio: Received gamepad focus");
        }

        public void OnGamepadFocusLost()
        {
            _isFocused = false;
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 Audio: Lost gamepad focus");
        }

        private void UpdateFocusVisuals()
        {
            // Dispatch on UI thread to ensure bindings update reliably with gamepad navigation
            DispatcherQueue.TryEnqueue(() =>
            {
                OnPropertyChanged(nameof(FocusBorderBrush));
                OnPropertyChanged(nameof(FocusBorderThickness));
                OnPropertyChanged(nameof(MuteButtonFocusBrush));
                OnPropertyChanged(nameof(SliderFocusBrush));
            });
        }

        public void AdjustSliderValue(int direction)
        {
            if (VolumeSlider == null || _currentFocusedControl != 1) return;
            
            const double increment = 5.0; // 5% increment
            double currentValue = VolumeSlider.Value;
            double newValue = Math.Clamp(currentValue + (direction * increment), 0, 100);
            
            VolumeSlider.Value = newValue;
            System.Diagnostics.Debug.WriteLine($"🎮 Audio: Adjusted volume to {newValue}% (direction: {direction})");
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // Event argument class for audio state changes
    public class AudioStateChangedEventArgs : EventArgs
    {
        public bool IsMuted { get; }
        public double VolumeLevel { get; }
        public string StatusMessage { get; }

        public AudioStateChangedEventArgs(bool isMuted, double volumeLevel, string statusMessage)
        {
            IsMuted = isMuted;
            VolumeLevel = volumeLevel;
            StatusMessage = statusMessage;
        }
    }
}