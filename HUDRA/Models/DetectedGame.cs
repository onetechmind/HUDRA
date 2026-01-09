using System;
using System.Collections.Generic;

namespace HUDRA.Models
{
    public class DetectedGame
    {
        public string ProcessName { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public string InstallLocation { get; set; } = string.Empty;

        public GameSource Source { get; set; } = GameSource.Unknown;

        public string LauncherInfo { get; set; } = string.Empty;
        public string PackageInfo { get; set; } = string.Empty;
        public DateTime LastDetected { get; set; } = DateTime.Now;
        public DateTime FirstDetected { get; set; } = DateTime.Now;

        // List of all executable names found in game folder (up to 5 levels deep)
        // Used for matching running processes, especially for Xbox games where actual exe differs from config
        public List<string> AlternativeExecutables { get; set; } = new List<string>();

        // Path to downloaded SteamGridDB artwork (grid image)
        public string? ArtworkPath { get; set; }

        // Per-game profile settings (JSON serialized GameProfile)
        // Null or empty if no profile is configured
        public string? ProfileJson { get; set; }
    }

    public enum GameSource
    {
        BattleNet,
        Epic,
        GOG,
        Origin,
        Riot,
        Rockstar,
        Steam,
        Ubisoft,
        Xbox,
        Directory,
        Manual,
        Unknown
    }
}