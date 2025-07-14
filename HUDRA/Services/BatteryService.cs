using Microsoft.UI.Dispatching;
using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;
using HUDRA.Configuration;

namespace HUDRA.Services
{
    public class BatteryInfo
    {
        public int Percent { get; init; }
        public bool IsCharging { get; init; }
        public bool IsOnAc { get; init; }
        public int RemainingSeconds { get; init; }
    }

    public class BatteryService : IDisposable
    {
        private readonly DispatcherTimer _timer;
        private bool _disposed;

        public event EventHandler<BatteryInfo>? BatteryInfoUpdated;

        public BatteryService()
        {
            _timer = new DispatcherTimer { Interval = HudraSettings.BATTERY_UPDATE_INTERVAL };
            _timer.Tick += (s, e) => UpdateBatteryInfo();
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }

        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();

        private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.StatusChange)
            {
                UpdateBatteryInfo();
            }
        }

        private void UpdateBatteryInfo()
        {
            if (GetSystemPowerStatus(out SYSTEM_POWER_STATUS status))
            {
                var info = new BatteryInfo
                {
                    Percent = status.BatteryLifePercent == 255 ? 100 : status.BatteryLifePercent,
                    IsCharging = (status.BatteryFlag & 0x08) == 0x08 || status.ACLineStatus == 1 && status.BatteryLifePercent < 100,
                    IsOnAc = status.ACLineStatus == 1,
                    RemainingSeconds = status.BatteryLifeTime
                };
                BatteryInfoUpdated?.Invoke(this, info);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }

        [DllImport("kernel32.dll")]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

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
    }
}
