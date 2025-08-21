using System;

namespace HUDRA.Models
{
    public class GameInfo
    {
        public string ProcessName { get; set; } = string.Empty;
        public string WindowTitle { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public IntPtr WindowHandle { get; set; }
        public string ExecutablePath { get; set; } = string.Empty;
    }
}