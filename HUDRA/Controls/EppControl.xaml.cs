using HUDRA.Services;
using HUDRA.Services.Power;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HUDRA.Controls
{
    /// <summary>
    /// Home-page slider for the processor Energy Performance Preference
    /// (powercfg PERFEPP, 0 = max performance, 100 = max efficiency). EPP only
    /// biases the CPU's CPPC boost behavior; on a shared-TDP APU a high value
    /// indirectly frees budget for the iGPU in GPU-bound games, but it is not
    /// a direct CPU/GPU power split. The Windows power-slider overlay can
    /// override it on AC power.
    /// </summary>
    public sealed partial class EppControl : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<int>? EppChanged;

        private PowerProfileService? _powerProfileService;
        private bool _initialized = false;

        // Slider state
        private int _currentEpp = -1; // last value confirmed applied to Windows
        private bool _isUpdatingSlider = false;

        // Debounce timer — applies the EPP after the user stops changing
        private DispatcherTimer? _eppDebounceTimer;
        private int _pendingEpp = -1;

        public EppControl()
        {
            this.InitializeComponent();

            _eppDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _eppDebounceTimer.Tick += EppDebounceTimer_Tick;
        }

        /// <summary>
        /// Called by MainWindow on every HOME visit. First call restores the
        /// persisted EPP (or displays the live value without writing when the
        /// user has never set one); subsequent calls just re-query and sync.
        /// </summary>
        public async void Initialize(PowerProfileService powerProfileService)
        {
            _powerProfileService = powerProfileService;

            try
            {
                if (!_initialized)
                {
                    _initialized = true;

                    var storedEpp = SettingsService.GetEppValue();
                    if (storedEpp >= 0)
                    {
                        var result = await _powerProfileService.SetEppAsync(storedEpp);
                        if (result.Success)
                        {
                            SyncToEpp(storedEpp);
                            return;
                        }
                        System.Diagnostics.Debug.WriteLine($"EppControl: failed to restore EPP {storedEpp}: {result.Message}");
                    }
                }

                // Never set, restore failed, or a re-visit: reflect reality
                var (success, liveEpp) = await _powerProfileService.GetEppAsync();
                if (success)
                {
                    if (EppSlider != null) EppSlider.IsEnabled = true;
                    SyncToEpp(liveEpp);
                }
                else if (_currentEpp < 0)
                {
                    // Setting unreadable (no CPPC / exotic power plan)
                    if (EppSlider != null) EppSlider.IsEnabled = false;
                    if (EppCaption != null) EppCaption.Text = "Not supported on this power plan";
                    System.Diagnostics.Debug.WriteLine("EppControl: EPP not readable - control disabled");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EppControl: initialization failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Syncs the UI to an EPP set externally (e.g. by a profile) without triggering hardware changes.
        /// </summary>
        public void SyncToEpp(int epp)
        {
            var clamped = PowercfgEppParser.ClampEpp(epp);
            _currentEpp = clamped;

            _isUpdatingSlider = true;
            if (EppSlider != null) EppSlider.Value = clamped;
            if (EppValueLabel != null) EppValueLabel.Text = FormatEppLabel(clamped);
            _isUpdatingSlider = false;
        }

        private void OnEppSliderValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            if (_isUpdatingSlider) return;

            int newEpp = PowercfgEppParser.ClampEpp((int)e.NewValue);

            // Label always tracks the slider, even when the value change is
            // cancelled below (same stale-label bug FpsLimiterControl hit).
            if (EppValueLabel != null)
                EppValueLabel.Text = FormatEppLabel(newEpp);

            if (newEpp == _currentEpp)
            {
                // Back at the value Windows already has: cancel any pending
                // write instead of letting a stale intermediate value apply
                // itself 500 ms later.
                _pendingEpp = -1;
                _eppDebounceTimer?.Stop();
                return;
            }

            // Restart debounce so we only hit powercfg after the user settles
            _pendingEpp = newEpp;
            _eppDebounceTimer?.Stop();
            _eppDebounceTimer?.Start();
        }

        private async void EppDebounceTimer_Tick(object? sender, object e)
        {
            _eppDebounceTimer?.Stop();
            if (_pendingEpp < 0 || _powerProfileService == null) return;

            var epp = _pendingEpp;
            _pendingEpp = -1;

            var result = await _powerProfileService.SetEppAsync(epp);
            if (result.Success)
            {
                _currentEpp = epp;
                SettingsService.SetEppValue(epp);
                EppChanged?.Invoke(this, epp);
                System.Diagnostics.Debug.WriteLine($"⚡ EppControl: Debounced — applied EPP {epp}");
            }
            else
            {
                // Revert the slider to the last applied value
                SyncToEpp(_currentEpp);
                System.Diagnostics.Debug.WriteLine($"EppControl: failed to apply EPP {epp}, reverted slider: {result.Message}");
            }
        }

        private static string FormatEppLabel(int epp)
        {
            var tier = epp <= 25 ? "Performance" : epp <= 74 ? "Balanced" : "Efficiency";
            return $"{epp} · {tier}";
        }

        public void Dispose()
        {
            if (_eppDebounceTimer != null)
            {
                _eppDebounceTimer.Stop();
                _eppDebounceTimer.Tick -= EppDebounceTimer_Tick;
                _eppDebounceTimer = null;
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
