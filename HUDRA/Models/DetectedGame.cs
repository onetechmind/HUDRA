using System;
using LiteDB;

namespace HUDRA.Models
{
    public class DetectedGame
    {
        [BsonId]
        public string ProcessName { get; set; } = string.Empty;
        
        public string DisplayName { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public string InstallLocation { get; set; } = string.Empty;
        
        public GameSource Source { get; set; } = GameSource.Unknown;
        
        public string LauncherInfo { get; set; } = string.Empty;
        public string PackageInfo { get; set; } = string.Empty;
        public DateTime LastDetected { get; set; } = DateTime.Now;
        public DateTime FirstDetected { get; set; } = DateTime.Now;
    }

    public enum GameSource
    {
        Steam,
        Epic,
        Origin,
        GOG,
        Ubisoft,
        Xbox,
        Directory,
        Manual,
        Unknown
    }
}