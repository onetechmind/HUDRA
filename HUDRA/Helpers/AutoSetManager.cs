using HUDRA.Configuration;
using Microsoft.UI.Xaml;
using System;
using System.Threading.Tasks;

namespace HUDRA.Helpers
{
    public class AutoSetManager<T> : IDisposable
    {
        private readonly DispatcherTimer _timer;
        private readonly Func<T, Task<bool>> _setValueAction;
        private readonly Action<string> _updateStatusAction;
        private T _pendingValue;
        private bool _isProcessing;

        public AutoSetManager(TimeSpan delay, Func<T, Task<bool>> setValueAction, Action<string> updateStatusAction)
        {
            _setValueAction = setValueAction;
            _updateStatusAction = updateStatusAction;
            _timer = new DispatcherTimer { Interval = delay };
            _timer.Tick += OnTimerTick;
        }

        public void ScheduleUpdate(T value)
        {
            if (_isProcessing) return;

            _pendingValue = value;
            _timer.Stop();
            _timer.Start();
        }

        private async void OnTimerTick(object sender, object e)
        {
            _timer.Stop();

            if (_isProcessing) return;

            _isProcessing = true;

            try
            {
                // FIXED: Remove asterisks - they're not valid C# syntax
                var success = await _setValueAction(_pendingValue);
                if (!success)
                {
                    _updateStatusAction?.Invoke("Update failed");
                }
            }
            catch (Exception ex)
            {
                _updateStatusAction?.Invoke($"Error: {ex.Message}");
            }
            finally
            {
                _isProcessing = false;
            }
        }

        public void Dispose()
        {
            _timer?.Stop();
        }
    }

    // Specialized managers for type safety
    public class TdpAutoSetManager : AutoSetManager<int>
    {
        public TdpAutoSetManager(Func<int, Task<bool>> setTdpAction, Action<string> updateStatus)
            : base(HudraSettings.TDP_AUTO_SET_DELAY, setTdpAction, updateStatus)
        {
        }
    }

    public class ResolutionAutoSetManager : AutoSetManager<int>
    {
        public ResolutionAutoSetManager(Func<int, Task<bool>> setResolutionAction, Action<string> updateStatus)
            : base(HudraSettings.RESOLUTION_AUTO_SET_DELAY, setResolutionAction, updateStatus)
        {
        }
    }

    public class RefreshRateAutoSetManager : AutoSetManager<int>
    {
        public RefreshRateAutoSetManager(Func<int, Task<bool>> setRefreshRateAction, Action<string> updateStatus)
            : base(HudraSettings.REFRESH_RATE_AUTO_SET_DELAY, setRefreshRateAction, updateStatus)
        {
        }
    }
}