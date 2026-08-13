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

        private void CheckTdpCallback(object? state)
        {
            int target;
            lock (_monitorLock)
            {
                if (_timer == null) return;
                target = _targetTdp;
            }

            // Safety check - never try to set 0W TDP
            if (target <= 0)
            {
                Debug.WriteLine($"TDP monitor skipping check - invalid target TDP: {target}W");
                return;
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
                    Debug.WriteLine($"TDP sticky re-assert to {target}W failed: {setResult.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TDP monitor error: {ex.Message}");
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

            _tdpService.Dispose();
            _disposed = true;
        }
    }
}
