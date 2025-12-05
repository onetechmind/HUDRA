namespace HUDRA.Models
{
    public class LosslessScalingDetectionResult
    {
        public bool IsInstalled { get; set; }
        public string InstallPath { get; set; } = "";
        public string Version { get; set; } = "";
        public bool IsRunning { get; set; }
        public bool HasSettingsFile { get; set; }
    }
}
