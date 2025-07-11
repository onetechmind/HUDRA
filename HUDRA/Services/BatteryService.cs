using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;

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
        }

        private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
        {
            PowerStatusChanged?.Invoke(this, EventArgs.Empty);
        }

        public BatteryInfo GetStatus()
        {
            if (GetSystemPowerStatus(out SYSTEM_POWER_STATUS status))
            {
                return new BatteryInfo
                {
                    Percentage = status.BatteryLifePercent == 255 ? 0 : status.BatteryLifePercent,
                    IsCharging = (status.BatteryFlag & 8) != 0,
                    IsAcPowered = status.ACLineStatus == 1,
                    RemainingTime = status.BatteryLifeTime > 0 ? TimeSpan.FromSeconds(status.BatteryLifeTime) : null
                };
            }

            return new BatteryInfo { Percentage = 0, IsCharging = false, IsAcPowered = false, RemainingTime = null };
        }

        public void Dispose()
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll")]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
    }
}
