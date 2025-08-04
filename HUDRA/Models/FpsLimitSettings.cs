using System.Collections.Generic;

namespace HUDRA.Models
{
    public class FpsLimitSettings
    {
        public int SelectedFpsLimit { get; set; } = 60;
        public List<int> AvailableFpsOptions { get; set; } = new();
        public bool IsRtssAvailable { get; set; } = false;
        public string RtssInstallPath { get; set; } = "";
        public string RtssVersion { get; set; } = "";
        public bool IsCurrentlyLimited { get; set; } = false;
    }
}