using Microsoft.UI.Dispatching;
using System;
using System.Diagnostics;
using System.Threading;

namespace HUDRA.Services
{
    public class TdpDriftEventArgs : EventArgs
    {
        public int CurrentTdp { get; }
        public int TargetTdp { get; }
        public bool CorrectionApplied { get; }

        public TdpDriftEventArgs(int currentTdp, int targetTdp, bool correctionApplied)
        {
            CurrentTdp = currentTdp;
            TargetTdp = targetTdp;
            CorrectionApplied = correctionApplied;
        }
    }

    public class TdpMonitorService : IDisposable
    {
        private readonly TDPService _tdpService;
        private readonly DispatcherQueue _dispatcher;
        private readonly object _monitorLock = new();
        // Serializes the hardware write itself. Kept separate from _monitorLock so a
        // slow SMU write can never block UpdateTargetTdp on the UI thread.
        private readonly object _writeLock = new();
        private Timer? _timer;
        private int _targetTdp;
        private bool _disposed;

        public event EventHandler<TdpDriftEventArgs>? TdpDriftDetected;

        public TdpMonitorService(DispatcherQueue dispatcher)
        {
            _dispatcher = dispatcher;
            _tdpService = new TDPService();
        }

        public void UpdateTargetTdp(int targetTdp)
        {
            lock (_monitorLock)
            {
                _targetTdp = targetTdp;
            }
        }

        public void Start()
        {
            lock (_monitorLock)
            {
                if (_timer != null)
                    return;

                _timer = new Timer(CheckTdpCallback, null,
                    TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
            }
        }

        public void Stop()
        {
            lock (_monitorLock)
            {
                _timer?.Dispose();
                _timer = null;
            }
        }

        /// <summary>
        /// The TDP the monitor currently re-asserts, or 0 if none has been established.
        /// </summary>
        public int TargetTdp
        {
            get
            {
                lock (_monitorLock)
                {
                    return _targetTdp;
                }
            }
        }

        /// <summary>
        /// Re-applies the current target immediately, independently of the sticky timer
        /// (which may not even be running). Returns false when there is no usable target
        /// or the write failed, so the caller can fall back to its own source of truth.
        /// </summary>
        public bool TryReapplyNow()
        {
            if (_disposed) return false;

            return ReassertTarget("immediate re-apply");
        }

        private void CheckTdpCallback(object? state)
        {
            lock (_monitorLock)
            {
                if (_timer == null) return;
            }

            ReassertTarget("sticky re-assert");
        }

        /// <summary>
        /// Resolves the target at call time and writes it. Resolving inside the write
        /// lock is what keeps a queued re-apply from writing a value the user has since
        /// changed.
        /// </summary>
        private bool ReassertTarget(string reason)
        {
            lock (_writeLock)
            {
                // Re-checked inside the lock: Dispose tears down the TDPService under the
                // same lock, so a re-apply racing disposal must not reach a dead service.
                if (_disposed) return false;

                int target;
                lock (_monitorLock)
                {
                    target = _targetTdp;
                }

                // Safety check - never try to set 0W TDP
                if (target <= 0)
                {
                    Debug.WriteLine($"TDP monitor skipping {reason} - invalid target TDP: {target}W");
                    return false;
                }

                try
                {
                    // Unconditional re-assert. The live SMU read is unreliable on some APUs
                    // (e.g. StrixHalo returns 0 for the arg-0 query), so instead of
                    // read-compare-correct we re-apply the target every interval. The SMU
                    // write is idempotent and echo-verified, so re-writing an unchanged limit
                    // is harmless; if a game or the firmware lowered it, this restores it.
                    var setResult = _tdpService.SetTdp(target * 1000);
                    if (!setResult.Success)
                    {
                        Debug.WriteLine($"TDP {reason} to {target}W failed: {setResult.Message}");
                    }

                    return setResult.Success;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"TDP monitor error during {reason}: {ex.Message}");
                    return false;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            lock (_monitorLock)
            {
                _timer?.Dispose();
                _timer = null;
            }

            lock (_writeLock)
            {
                _disposed = true;
                _tdpService.Dispose();
            }
        }
    }
}
