namespace HUDRA.Models
{
    public enum DeviceManufacturer
    {
        Unknown,
        Lenovo,
        GPD,
        OneXPlayer
    }

    public class DetectedDevice
    {
        // Detected/matched device info
        public DeviceManufacturer Manufacturer { get; set; } = DeviceManufacturer.Unknown;
        public string DeviceName { get; set; } = "Unknown";  // e.g., "Legion Go", "Win 4 Series"

        // Raw WMI strings for debugging
        public string RawManufacturer { get; set; } = "";    // From Win32_ComputerSystem
        public string RawModel { get; set; } = "";           // From Win32_ComputerSystem
        public string RawVersion { get; set; } = "";         // From Win32_ComputerSystem

        // Feature flags (computed from detection)
        public bool SupportsFanControl { get; set; } = false;
        public bool SupportsLenovoWmi { get; set; } = false; // For TDP via WMI

        // Detection schema version - bump CURRENT_DETECTION_VERSION in HardwareDetectionService
        // to invalidate cached results whenever detection logic changes
        public int DetectionVersion { get; set; } = 0;

        // Convenience properties
        public bool IsLenovo => Manufacturer == DeviceManufacturer.Lenovo;
        public bool IsGPD => Manufacturer == DeviceManufacturer.GPD;
        public bool IsOneXPlayer => Manufacturer == DeviceManufacturer.OneXPlayer;
        public bool IsKnownDevice => Manufacturer != DeviceManufacturer.Unknown;
    }
}
