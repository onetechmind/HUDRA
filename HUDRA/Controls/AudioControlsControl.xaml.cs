using HUDRA.Configuration;
using HUDRA.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HUDRA.Controls
{
    public sealed partial class AudioControlsControl : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<AudioStateChangedEventArgs>? AudioStateChanged;

        private AudioService? _audioService;
        private bool _isUpdatingSlider = false;
        private double _previousVolumeLevel = 50.0; // Remember volume before muting

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

        // Properties for gamepad navigation
        public Button MuteButtonControl => MuteButton;
        public Slider VolumeSliderControl => VolumeSlider;

        public AudioControlsControl()
        {
            this.InitializeComponent();
        }

        public void Initialize()
        {
            _audioService = new AudioService();

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
                // Get current state BEFORE toggling
                bool wasUnmuted = !_audioService.GetMuteStatus();

                // If we're about to mute, remember current volume
                if (wasUnmuted && VolumeSlider != null && VolumeSlider.Value > 0)
                {
                    _previousVolumeLevel = VolumeSlider.Value;
                }

                // Toggle the mute state
                _audioService.ToggleMute();

                // Add a small delay to ensure system state has updated
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
                {
                    // Now update UI based on ACTUAL system state
                    UpdateMuteButtonIcon();
                    UpdateVolumeSliderForMuteState();

                    // Fire event with current state
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
                    _audioService.ToggleMute(); // Unmute
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
                    System.Diagnostics.Debug.WriteLine($"Mute button icon updated: {(isMuted ? "MUTED" : "UNMUTED")} - Glyph: {MuteButtonIcon.Glyph}");
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

        // Public methods for external control (gamepad)
        public void ToggleMute()
        {
            OnMuteButtonClick(MuteButton, new RoutedEventArgs());
        }

        public void ChangeVolumeBy(double delta)
        {
            if (VolumeSlider == null) return;

            var newValue = Math.Max(0, Math.Min(100, VolumeSlider.Value + delta));
            VolumeSlider.Value = newValue;
        }

        public void Dispose()
        {
            // No auto-set managers to dispose for audio controls
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