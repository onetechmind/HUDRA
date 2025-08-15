using System;
using System.Collections.Generic;

namespace HUDRA.Services.FanControl
{
    public enum FanControlMode
    {
        Hardware,  // Let device handle fan control
        Software   // Manual fan control
    }

    public enum FanControlCapability
    {
        BasicSpeedControl,
        AdvancedCurves,
        MultipleProfiles,
        TemperatureSensors,
        RPMReporting
    }
    public class FanCurvePoint
    {
        public double Temperature { get; set; }  // 0-90°C
        public double FanSpeed { get; set; }     // 0-100%
    }

    public class FanCurve
    {
        public FanCurvePoint[] Points { get; set; } = new FanCurvePoint[5];
        public bool IsEnabled { get; set; }
        public string ActivePreset { get; set; } = string.Empty; // NEW: Track active preset
    }

    public class FanCurvePreset
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public FanCurvePoint[] Points { get; set; } = Array.Empty<FanCurvePoint>();

        public static readonly FanCurvePreset Stealth = new()
        {
            Name = "Stealth",
            Description = "Ultra-quiet mode for light gaming, passive use, or low ambient temps. Prioritizes silence over temps.",
            Points = new FanCurvePoint[]
            {
                new() { Temperature = 35, FanSpeed = 5 },
                new() { Temperature = 50, FanSpeed = 15 },
                new() { Temperature = 60, FanSpeed = 35 },
                new() { Temperature = 65, FanSpeed = 65 },
                new() { Temperature = 80, FanSpeed = 100 }
            }
        };

        public static readonly FanCurvePreset Cruise = new()
        {
            Name = "Cruise",
            Description = "Balanced all-purpose mode for general gaming and multitasking. Keeps noise moderate while holding temps in check.",
            Points = new FanCurvePoint[]
            {
                new() { Temperature = 35, FanSpeed = 5 },
                new() { Temperature = 45, FanSpeed = 10 },
                new() { Temperature = 55, FanSpeed = 30 },
                new() { Temperature = 65, FanSpeed = 65 },
                new() { Temperature = 80, FanSpeed = 100 }
            }
        };

        public static readonly FanCurvePreset Warp = new()
        {
            Name = "Warp",
            Description = "Performance-first mode for heavy gaming or high TDP loads. Ramps early to maintain max clocks and cool headroom.",
            Points = new FanCurvePoint[]
            {
                new() { Temperature = 35, FanSpeed = 10 },
                new() { Temperature = 45, FanSpeed = 30 },
                new() { Temperature = 60, FanSpeed = 55 },
                new() { Temperature = 65, FanSpeed = 65 },
                new() { Temperature = 75, FanSpeed = 100 }
            }
        };

        public static readonly FanCurvePreset[] AllPresets = { Stealth, Cruise, Warp };
    }

    public class DeviceCapabilities
    {
        public HashSet<FanControlCapability> SupportedFeatures { get; set; } = new();
        public int MinFanSpeed { get; set; } = 0;
        public int MaxFanSpeed { get; set; } = 100;
        public bool SupportsAutoDetection { get; set; } = true;
        public string[] SupportedModels { get; set; } = Array.Empty<string>();
    }

    public class ECRegisterMap
    {
        public ushort FanControlAddress { get; set; }
        public ushort FanDutyAddress { get; set; }
        public ushort StatusCommandPort { get; set; } = 0x4E;
        public ushort DataPort { get; set; } = 0x4F;
        public byte FanValueMin { get; set; } = 0;
        public byte FanValueMax { get; set; } = 255;
        public ushort? TemperatureAddress { get; set; }
        public ushort? RPMAddress { get; set; }

        //EC Communication Protocol
        public ECProtocolConfig Protocol { get; set; } = new ECProtocolConfig();
    }

    public class ECProtocolConfig
    {
        // NO default values - each device must specify its own protocol
        public byte AddressSelectHigh { get; set; }
        public byte AddressSetHigh { get; set; }
        public byte AddressSelectLow { get; set; }
        public byte AddressSetLow { get; set; }
        public byte DataSelect { get; set; }
        public byte DataCommand { get; set; }
        public byte AddressPort { get; set; }
        public byte ReadDataSelect { get; set; }
    }

    public class FanStatus
    {
        public bool IsControlEnabled { get; set; }
        public double CurrentDutyPercent { get; set; }
        public int? CurrentRPM { get; set; }
        public double? Temperature { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }

    public class FanControlResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public Exception? Exception { get; set; }

        public static FanControlResult SuccessResult(string message = "Operation completed successfully")
            => new() { Success = true, Message = message };

        public static FanControlResult FailureResult(string message, Exception? exception = null)
            => new() { Success = false, Message = message, Exception = exception };
    }

    public interface IFanControlDevice : IDisposable
    {
        string ManufacturerName { get; }
        string DeviceName { get; }
        DeviceCapabilities Capabilities { get; }
        ECRegisterMap RegisterMap { get; }
        bool IsInitialized { get; }
        uint? TurboButtonECAddress { get; }

        bool Initialize();
        bool SetFanControl(FanControlMode mode);
        bool SetFanDuty(double percent);
        FanStatus GetFanStatus();
        bool IsDeviceSupported();
    }
}