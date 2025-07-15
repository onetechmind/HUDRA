using HUDRA.Configuration;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using Windows.System.Power;

namespace HUDRA.Services
{
    public class BatteryInfo
    {
        public int Percent { get; set; }
        public bool IsCharging { get; set; }
        public bool OnAc { get; set; }
        public TimeSpan RemainingDischargeTime { get; set; }
    }

    public class BatteryService : IDisposable
    {
        private readonly DispatcherQueue _dispatcher;
        private readonly DispatcherTimer _timer;
        private bool _disposed;

        public BatteryInfo CurrentInfo { get; private set; } = new BatteryInfo();

        public event EventHandler<BatteryInfo>? BatteryInfoUpdated;

        public BatteryService(DispatcherQueue dispatcher)
        {
            _dispatcher = dispatcher;
            _timer = new DispatcherTimer { Interval = HudraSettings.BATTERY_UPDATE_INTERVAL };
            _timer.Tick += (s, e) => UpdateBatteryInfo();

            PowerManager.RemainingChargePercentChanged += OnPowerChanged;
            PowerManager.BatteryStatusChanged += OnPowerChanged;
            PowerManager.PowerSupplyStatusChanged += OnPowerChanged;
            PowerManager.RemainingDischargeTimeChanged += OnPowerChanged;

            UpdateBatteryInfo();
            _timer.Start();
        }

        private void OnPowerChanged(object? sender, object e)
        {
            UpdateBatteryInfo();
        }

        private void UpdateBatteryInfo()
        {
            if (_disposed) return;

            var percent = (int)PowerManager.RemainingChargePercent;
            var batteryStatus = PowerManager.BatteryStatus;
            var supplyStatus = PowerManager.PowerSupplyStatus;
            var remaining = PowerManager.RemainingDischargeTime;

            bool isCharging = batteryStatus == BatteryStatus.Charging;
            bool onAc = supplyStatus == PowerSupplyStatus.Adequate || isCharging;

            CurrentInfo = new BatteryInfo
            {
                Percent = percent,
                IsCharging = isCharging,
                OnAc = onAc,
                RemainingDischargeTime = remaining
            };

            _dispatcher.TryEnqueue(() => BatteryInfoUpdated?.Invoke(this, CurrentInfo));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            PowerManager.RemainingChargePercentChanged -= OnPowerChanged;
            PowerManager.BatteryStatusChanged -= OnPowerChanged;
            PowerManager.PowerSupplyStatusChanged -= OnPowerChanged;
            PowerManager.RemainingDischargeTimeChanged -= OnPowerChanged;
        }
    }
}
