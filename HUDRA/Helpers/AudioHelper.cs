using System;
using System.IO;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace HUDRA.Helpers
{
    public class AudioHelper : IDisposable
    {
        private MediaPlayer? _mediaPlayer;
        private bool _disposed = false;
        private DateTime _lastTickTime = DateTime.MinValue;
        private readonly TimeSpan _minTickInterval = TimeSpan.FromMilliseconds(150);

        public AudioHelper()
        {
            try
            {
                _mediaPlayer = new MediaPlayer();
                _mediaPlayer.Volume = 1.0;

                string tickPath = Path.Combine(AppContext.BaseDirectory, "Assets", "tick.wav");
                if (File.Exists(tickPath))
                {
                    var mediaSource = MediaSource.CreateFromUri(new Uri($"file:///{tickPath}"));
                    _mediaPlayer.Source = mediaSource;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Tick sound file not found: {tickPath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize AudioHelper: {ex.Message}");
            }
        }

        public void PlayTick()
        {
            try
            {
                if (_mediaPlayer?.Source != null && !_disposed)
                {
                    // Rate limiting - prevent rapid fire
                    var now = DateTime.Now;
                    if (now - _lastTickTime < _minTickInterval)
                    {
                        return; // Skip this tick
                    }
                    _lastTickTime = now;

                    // Stop current playback and restart
                    _mediaPlayer.Pause();
                    _mediaPlayer.Position = TimeSpan.Zero;
                    _mediaPlayer.Play();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to play tick sound: {ex.Message}");
            }
        }

        public void SetVolume(double volume)
        {
            if (_mediaPlayer != null && !_disposed)
            {
                _mediaPlayer.Volume = Math.Clamp(volume, 0.0, 1.0);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _mediaPlayer?.Dispose();
                _mediaPlayer = null;
                _disposed = true;
            }
        }
    }
}