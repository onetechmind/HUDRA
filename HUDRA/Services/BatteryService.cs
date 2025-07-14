using Microsoft.Win32;
using System;
using Windows.System.Power;

namespace HUDRA.Services
{
    public class BatteryInfo
    {
        public int Percentage { get; set; }
        public bool IsCharging { get; set; }
        public bool IsAcPowered { get; set; }
        public TimeSpan? RemainingTime { get; set; }
    }

    public class BatteryService : IDisposable
    {
        public event EventHandler? PowerStatusChanged;

        public BatteryService()
        {
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            PowerManager.RemainingChargePercentChanged += OnPowerManagerChanged;
            PowerManager.PowerSupplyStatusChanged += OnPowerManagerChanged;
            PowerManager.BatteryStatusChanged += OnPowerManagerChanged;
            PowerManager.RemainingDischargeTimeChanged += OnPowerManagerChanged;
        }

        private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
        {
            PowerStatusChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnPowerManagerChanged(object? sender)
        {
            PowerStatusChanged?.Invoke(this, EventArgs.Empty);
        }

        public BatteryInfo GetStatus()
        {
            var percent = PowerManager.RemainingChargePercent;
            var isCharging = PowerManager.BatteryStatus == BatteryStatus.Charging;
            var isAc = PowerManager.PowerSupplyStatus != PowerSupplyStatus.NotPresent &&
                       PowerManager.PowerSupplyStatus != PowerSupplyStatus.Inadequate;
            var time = PowerManager.RemainingDischargeTime;

            return new BatteryInfo
            {
                Percentage = percent,
                IsCharging = isCharging,
                IsAcPowered = isAc,
                RemainingTime = time == TimeSpan.Zero || time == TimeSpan.FromSeconds(-1) ? null : time
            };
        }

        public void Dispose()
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            PowerManager.RemainingChargePercentChanged -= OnPowerManagerChanged;
            PowerManager.PowerSupplyStatusChanged -= OnPowerManagerChanged;
            PowerManager.BatteryStatusChanged -= OnPowerManagerChanged;
            PowerManager.RemainingDischargeTimeChanged -= OnPowerManagerChanged;
        }
    }
}
