using System;
using System.Threading;
using System.Threading.Tasks;
using HUDRA.Configuration;
using HUDRA.Controls;
using HUDRA.Services;
using HUDRA.Services.Power;
using Microsoft.UI.Dispatching;

namespace HUDRA.Services.Web
{
    /// <summary>
    /// Thread-safe bridge providing web server access to HUDRA's existing services.
    /// Services are resolved lazily via Func delegates since they're created at different times.
    /// </summary>
    public class ServiceBridge : IDisposable
    {
        // Lazy service resolvers (services may not exist yet at bridge creation time)
        private readonly Func<TdpMonitorService?> _getTdpMonitor;
        private readonly Func<TemperatureMonitorService?> _getTemperatureMonitor;
        private readonly Func<FanControlService?> _getFanControl;
        private readonly Func<BatteryService?> _getBattery;
        private readonly Func<EnhancedGameDetectionService?> _getGameDetection;
        private readonly Func<GameProfileService?> _getGameProfile;
        private readonly Func<RtssFpsLimiterService?> _getFpsLimiter;
        private readonly Func<LosslessScalingService?> _getLosslessScaling;
        private readonly Func<PowerProfileService?> _getPowerProfile;
        private readonly Func<TurboService?> _getTurboService;

        private readonly DispatcherQueue _dispatcherQueue;

        // TDP — single long-lived instance with serialized access
        private readonly TDPService _tdpService;
        private readonly SemaphoreSlim _tdpLock = new(1, 1);
        private bool _disposed;

        // AMD — lazy fallback if the Scaling page hasn't been opened yet
        private AmdAdlxService? _ownedAmdService;
        private readonly object _amdServiceLock = new();

        public ServiceBridge(
            DispatcherQueue dispatcherQueue,
            Func<TdpMonitorService?> getTdpMonitor,
            Func<TemperatureMonitorService?> getTemperatureMonitor,
            Func<FanControlService?> getFanControl,
            Func<BatteryService?> getBattery,
            Func<EnhancedGameDetectionService?> getGameDetection,
            Func<GameProfileService?> getGameProfile,
            Func<RtssFpsLimiterService?> getFpsLimiter,
            Func<LosslessScalingService?> getLosslessScaling,
            Func<PowerProfileService?> getPowerProfile,
            Func<TurboService?> getTurboService)
        {
            _dispatcherQueue = dispatcherQueue;
            _getTdpMonitor = getTdpMonitor;
            _getTemperatureMonitor = getTemperatureMonitor;
            _getFanControl = getFanControl;
            _getBattery = getBattery;
            _getGameDetection = getGameDetection;
            _getGameProfile = getGameProfile;
            _getFpsLimiter = getFpsLimiter;
            _getLosslessScaling = getLosslessScaling;
            _getPowerProfile = getPowerProfile;
            _getTurboService = getTurboService;

            _tdpService = new TDPService();
        }

        // --- Stateless services (safe to create fresh from any thread) ---

        public AudioService CreateAudioService() => new();
        public BrightnessService CreateBrightnessService() => new();
        public HdrService CreateHdrService() => new();
        public ResolutionService CreateResolutionService() => new();

        // --- Thread-safe property reads ---

        public TemperatureData? GetCurrentTemperature() =>
            _getTemperatureMonitor()?.CurrentTemperature;

        public BatteryInfo? GetCurrentBattery() =>
            _getBattery()?.CurrentInfo;

        public double GetCurrentFanSpeed() =>
            _getFanControl()?.CurrentFanSpeed ?? 0.0;

        public bool IsFanControlInitialized() =>
            _getFanControl() != null;

        // --- TDP operations (serialized via SemaphoreSlim) ---

        public async Task<(bool Success, int TdpWatts, string Message)> GetCurrentTdpAsync()
        {
            await _tdpLock.WaitAsync();
            try
            {
                return _tdpService.GetCurrentTdp();
            }
            finally
            {
                _tdpLock.Release();
            }
        }

        public async Task<(bool Success, string Message)> SetTdpAsync(int tdpWatts)
        {
            await _tdpLock.WaitAsync();
            try
            {
                var result = _tdpService.SetTdp(tdpWatts * 1000); // Convert to milliwatts
                if (result.Success)
                {
                    _getTdpMonitor()?.UpdateTargetTdp(tdpWatts);
                    SettingsService.SetLastUsedTdp(tdpWatts);
                }
                return result;
            }
            finally
            {
                _tdpLock.Release();
            }
        }

        // --- Dispatcher-marshaled operations ---

        public Task RunOnUiThreadAsync(Action action)
        {
            var tcs = new TaskCompletionSource();
            bool enqueued = _dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    action();
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            if (!enqueued)
                tcs.SetException(new InvalidOperationException("Failed to enqueue on UI thread"));

            return tcs.Task;
        }

        public Task<T> RunOnUiThreadAsync<T>(Func<T> func)
        {
            var tcs = new TaskCompletionSource<T>();
            bool enqueued = _dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    tcs.SetResult(func());
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            if (!enqueued)
                tcs.SetException(new InvalidOperationException("Failed to enqueue on UI thread"));

            return tcs.Task;
        }

        // --- Fan control (needs dispatcher for some ops) ---

        public FanControlService? GetFanControlService() => _getFanControl();

        public void EnableFanControl(TemperatureMonitorService tempMonitor)
        {
            _getFanControl()?.EnableTemperatureControl(tempMonitor);
        }

        public void DisableFanControl()
        {
            _getFanControl()?.DisableTemperatureControl();
        }

        // --- Game detection ---

        public EnhancedGameDetectionService? GetGameDetection() => _getGameDetection();
        public GameProfileService? GetGameProfile() => _getGameProfile();

        // --- FPS Limiter ---

        public RtssFpsLimiterService? GetFpsLimiter() => _getFpsLimiter();

        // --- Lossless Scaling ---

        public LosslessScalingService? GetLosslessScaling() => _getLosslessScaling();

        /// <summary>
        /// Triggers the Lossless Scaling hotkey: switches focus to the running game window,
        /// waits 500 ms, then fires the hotkey exactly as the native LS button does.
        /// Returns a human-readable error string on failure, or null on success.
        /// </summary>
        public async Task<string?> TriggerLosslessScalingAsync()
        {
            var ls = _getLosslessScaling();
            if (ls == null) return "Lossless Scaling service unavailable";
            if (!ls.IsLosslessScalingRunning()) return "Lossless Scaling is not running";

            var detection = _getGameDetection();
            if (detection?.CurrentGame == null) return "No game currently detected";

            if (!detection.SwitchToGame()) return "Could not switch to game window";

            await Task.Delay(500);

            var (hotkey, modifiers) = ls.ParseHotkeyFromSettings();
            bool ok = await ls.ExecuteHotkeyAsync(hotkey, modifiers);
            return ok ? null : "Failed to send Lossless Scaling hotkey";
        }

        // --- Power ---

        public PowerProfileService? GetPowerProfile() => _getPowerProfile();
        public TurboService? GetTurboService() => _getTurboService();
        public TemperatureMonitorService? GetTemperatureMonitor() => _getTemperatureMonitor();
        public BatteryService? GetBatteryService() => _getBattery();
        public TdpMonitorService? GetTdpMonitor() => _getTdpMonitor();

        // --- AMD ---

        private AmdAdlxService? GetAmdService()
        {
            // Prefer the control's already-initialised singleton (avoids double ADLX init)
            var shared = AmdFeaturesControl.SharedService;
            if (shared != null) return shared;

            if (_ownedAmdService != null) return _ownedAmdService;
            lock (_amdServiceLock)
            {
                if (_ownedAmdService == null)
                {
                    try { _ownedAmdService = new AmdAdlxService(); }
                    catch { return null; }
                }
            }
            return _ownedAmdService;
        }

        public async Task<(bool available, bool rsrEnabled, int rsrSharpness, bool afmfEnabled, bool antiLagEnabled)> GetAmdStateAsync()
        {
            var svc = GetAmdService();
            if (svc == null || !svc.IsAmdGpuAvailable())
                return (false, false, 80, false, false);

            var (rsrOk, rsrEnabled, rsrSharpness) = await svc.GetRsrStateAsync();
            var (afmfOk, afmfEnabled) = await svc.GetAfmfStateAsync();
            var (antiLagOk, antiLagEnabled) = await svc.GetAntiLagStateAsync();

            return (true,
                rsrOk ? rsrEnabled : false,
                rsrOk ? rsrSharpness : 80,
                afmfOk ? afmfEnabled : false,
                antiLagOk ? antiLagEnabled : false);
        }

        public async Task<bool> SetAmdRsrAsync(bool enabled, int sharpness)
        {
            var svc = GetAmdService();
            if (svc == null || !svc.IsAmdGpuAvailable()) return false;
            return await svc.SetRsrEnabledAsync(enabled, sharpness);
        }

        public async Task<bool> SetAmdRsrSharpnessAsync(int sharpness)
        {
            var svc = GetAmdService();
            if (svc == null || !svc.IsAmdGpuAvailable()) return false;
            // Sharpness only applies when RSR is enabled; keep current enabled state
            var (ok, enabled, _) = await svc.GetRsrStateAsync();
            if (!ok || !enabled) return false;
            return await svc.SetRsrEnabledAsync(true, sharpness);
        }

        public async Task<bool> SetAmdAfmfAsync(bool enabled)
        {
            var svc = GetAmdService();
            if (svc == null || !svc.IsAmdGpuAvailable()) return false;
            return await svc.SetAfmfEnabledAsync(enabled);
        }

        public async Task<bool> SetAmdAntiLagAsync(bool enabled)
        {
            var svc = GetAmdService();
            if (svc == null || !svc.IsAmdGpuAvailable()) return false;
            return await svc.SetAntiLagEnabledAsync(enabled);
        }

        // --- Web mutation notification ---

        /// <summary>
        /// Fired after any state-changing web API call so that native UI and
        /// other web clients can refresh.
        /// </summary>
        public event Action? WebMutationOccurred;

        public void NotifyWebMutation()
        {
            // Fire-and-forget on thread pool to avoid blocking the API response
            Task.Run(() =>
            {
                try { WebMutationOccurred?.Invoke(); }
                catch { /* observers shouldn't throw, but guard anyway */ }
            });
        }

        /// <summary>
        /// Builds a lightweight snapshot of all mutable control state for SignalR broadcast.
        /// Excludes resolution (rarely changes) and games (loaded separately).
        /// </summary>
        public async Task<object> BuildControlStateSnapshotAsync()
        {
            var tdpResult = await GetCurrentTdpAsync();
            var eppValue = await GetEppForSnapshotAsync();
            var audio = CreateAudioService();
            var brightness = CreateBrightnessService();
            var hdr = CreateHdrService();
            var resolution = CreateResolutionService();
            var fpsLimiter = GetFpsLimiter();
            var fanCurve = SettingsService.GetFanCurve();
            var temp = GetCurrentTemperature();
            var battery = GetCurrentBattery();
            var amdState = await GetAmdStateAsync();

            var currentRes = resolution.GetCurrentResolution();
            var currentRefresh = resolution.GetCurrentRefreshRate();

            return new
            {
                tdp = new
                {
                    current = tdpResult.Success ? tdpResult.TdpWatts : 0,
                    min = HudraSettings.MIN_TDP,
                    max = HudraSettings.MAX_TDP,
                    stickyEnabled = SettingsService.GetTdpCorrectionEnabled()
                },
                volume = new
                {
                    level = (int)(audio.GetMasterVolumeScalar() * 100),
                    muted = audio.GetMuteStatus()
                },
                brightness = brightness.GetBrightness(),
                fpsLimit = fpsLimiter?.GetCurrentFpsLimit() ?? 0,
                hdr = new
                {
                    enabled = hdr.IsHdrEnabled(),
                    supported = hdr.IsHdrSupported()
                },
                resolution = currentRes.Success ? new { width = currentRes.CurrentResolution.Width, height = currentRes.CurrentResolution.Height } : null,
                refreshRate = currentRefresh.Success ? currentRefresh.RefreshRate : 0,
                fan = new
                {
                    speed = GetCurrentFanSpeed(),
                    preset = fanCurve.ActivePreset,
                    enabled = SettingsService.GetFanCurveEnabled()
                },
                temperature = temp != null ? new { cpu = Math.Round(temp.CpuTemperature, 1), gpu = Math.Round(temp.GpuTemperature, 1) } : null,
                battery = battery != null ? new { percent = battery.Percent, isCharging = battery.IsCharging, onAc = battery.OnAc } : null,
                amd = new
                {
                    available = amdState.available,
                    rsrEnabled = amdState.rsrEnabled,
                    rsrSharpness = amdState.rsrSharpness,
                    afmfEnabled = amdState.afmfEnabled,
                    antiLagEnabled = amdState.antiLagEnabled
                },
                epp = eppValue
            };
        }

        /// <summary>
        /// EPP for the periodic snapshot. The persisted value is authoritative
        /// once the user has set one (native and web writes both update it);
        /// only before that do we shell out to powercfg, so the 3s broadcast
        /// doesn't spawn a process per tick. null = unavailable.
        /// </summary>
        private async Task<int?> GetEppForSnapshotAsync()
        {
            var stored = SettingsService.GetEppValue();
            if (stored >= 0) return stored;

            var powerProfile = GetPowerProfile();
            if (powerProfile == null) return null;

            var result = await powerProfile.GetEppAsync();
            return result.Success ? result.Value : null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _tdpService.Dispose();
            _tdpLock.Dispose();
            _ownedAmdService?.Dispose();
        }
    }
}
