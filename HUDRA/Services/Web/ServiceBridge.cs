using System;
using System.Threading;
using System.Threading.Tasks;
using HUDRA.Configuration;
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

        // --- Power ---

        public PowerProfileService? GetPowerProfile() => _getPowerProfile();
        public TurboService? GetTurboService() => _getTurboService();
        public TemperatureMonitorService? GetTemperatureMonitor() => _getTemperatureMonitor();
        public BatteryService? GetBatteryService() => _getBattery();
        public TdpMonitorService? GetTdpMonitor() => _getTdpMonitor();

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
            var audio = CreateAudioService();
            var brightness = CreateBrightnessService();
            var hdr = CreateHdrService();
            var fpsLimiter = GetFpsLimiter();
            var fanCurve = SettingsService.GetFanCurve();
            var temp = GetCurrentTemperature();
            var battery = GetCurrentBattery();

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
                fan = new
                {
                    speed = GetCurrentFanSpeed(),
                    preset = fanCurve.ActivePreset,
                    enabled = SettingsService.GetFanCurveEnabled()
                },
                temperature = temp != null ? new { cpu = Math.Round(temp.CpuTemperature, 1), gpu = Math.Round(temp.GpuTemperature, 1) } : null,
                battery = battery != null ? new { percent = battery.Percent, isCharging = battery.IsCharging, onAc = battery.OnAc } : null
            };
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _tdpService.Dispose();
            _tdpLock.Dispose();
        }
    }
}
