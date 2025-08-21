using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace HUDRA.Services
{
    public interface ILauncherDetector
    {
        List<string> GetLibraryPaths();
        string LauncherName { get; }
    }

    // SteamDetector removed - GameLib.NET now handles Steam game detection

    // UWPDetector removed - XboxGameProvider now handles Xbox/UWP game detection via PowerShell

    /// <summary>
    /// Provides fallback game directory detection for games not covered by enhanced detection.
    /// Note: Steam and Xbox game detection are now handled by GameLib.NET and XboxGameProvider respectively.
    /// This service primarily provides fallback directory paths for unknown games.
    /// </summary>
    public class GameLauncherConfigService
    {
        private readonly List<ILauncherDetector> _detectors;
        private List<string>? _cachedGamePaths;

        public GameLauncherConfigService()
        {
            // All specific launcher detectors have been removed since:
            // - Steam detection is handled by GameLib.NET
            // - Xbox/UWP detection is handled by XboxGameProvider
            // This service now only provides fallback directory scanning
            _detectors = new List<ILauncherDetector>();
        }

        public List<string> GetAllGameLibraryPaths()
        {
            if (_cachedGamePaths != null)
            {
                return _cachedGamePaths;
            }

            var allPaths = new List<string>();

            foreach (var detector in _detectors)
            {
                try
                {
                    var paths = detector.GetLibraryPaths();
                    allPaths.AddRange(paths);
                    System.Diagnostics.Debug.WriteLine($"{detector.LauncherName} contributed {paths.Count} library paths");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error getting paths from {detector.LauncherName}: {ex.Message}");
                }
            }

            // Remove duplicates and cache the results
            _cachedGamePaths = allPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            
            System.Diagnostics.Debug.WriteLine($"GameLauncherConfigService cached {_cachedGamePaths.Count} total library paths");
            
            return _cachedGamePaths;
        }

        public List<string> GetFallbackGameDirectories()
        {
            return new List<string>
            {
                // Steam fallbacks
                @"\Steam\steamapps\common\",
                @"\Program Files (x86)\Steam\steamapps\common\",
                @"\Program Files\Steam\steamapps\common\",
                
                // Epic Games
                @"\Epic Games\",
                @"\Program Files\Epic Games\",
                @"\Program Files (x86)\Epic Games\",
                
                // Origin/EA
                @"\Origin Games\",
                @"\EA Games\",
                @"\Program Files\Origin Games\",
                @"\Program Files (x86)\Origin Games\",
                
                // Ubisoft
                @"\Ubisoft\Ubisoft Game Launcher\games\",
                @"\Program Files\Ubisoft\Ubisoft Game Launcher\games\",
                @"\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\games\",
                
                // GOG
                @"\GOG Galaxy\Games\",
                @"\Program Files\GOG Galaxy\Games\",
                @"\Program Files (x86)\GOG Galaxy\Games\",
                
                // Game Pass/UWP/Microsoft Store games
                @"\Program Files\WindowsApps\",
                @"\Program Files (x86)\WindowsApps\",
                @"\WindowsApps\",
                @"\Microsoft.Gaming\",
                
                // Generic game folders
                @"\Games\",
                @"\Program Files\Games\",
                @"\Program Files (x86)\Games\",
                
                // Additional common game directories
                @"\Riot Games\",
                @"\Battle.net\",
                @"\Blizzard Entertainment\",
                @"\Rockstar Games\",
                @"\Take-Two Interactive\",
                @"\Square Enix\",
                @"\Activision\",
                @"\SEGA\",
                @"\Capcom\",
                @"\Valve\",
                @"\2K Games\",
                @"\Bethesda Game Studios\",
                @"\CD Projekt RED\"
            };
        }

        public void ClearCache()
        {
            _cachedGamePaths = null;
        }
    }
}