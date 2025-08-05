using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using HUDRA.Utils;

namespace HUDRA.Services
{
    public interface ILauncherDetector
    {
        List<string> GetLibraryPaths();
        string LauncherName { get; }
    }

    public class SteamDetector : ILauncherDetector
    {
        public string LauncherName => "Steam";

        public List<string> GetLibraryPaths()
        {
            var paths = new List<string>();

            try
            {
                var steamPath = GetSteamInstallPath();
                if (string.IsNullOrEmpty(steamPath))
                {
                    System.Diagnostics.Debug.WriteLine("Steam installation path not found");
                    return paths;
                }

                var libraryFoldersPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(libraryFoldersPath))
                {
                    System.Diagnostics.Debug.WriteLine($"Steam libraryfolders.vdf not found at: {libraryFoldersPath}");
                    return paths;
                }

                var vdfData = VdfParser.ParseFile(libraryFoldersPath);
                if (!vdfData.ContainsKey("libraryfolders"))
                {
                    System.Diagnostics.Debug.WriteLine("No 'libraryfolders' section found in VDF");
                    return paths;
                }

                if (vdfData["libraryfolders"] is Dictionary<string, object> libraryFolders)
                {
                    foreach (var kvp in libraryFolders)
                    {
                        if (kvp.Value is Dictionary<string, object> library && 
                            library.ContainsKey("path") && 
                            library["path"] is string libraryPath)
                        {
                            // Add the steamapps\common path for games
                            var gamePath = Path.Combine(libraryPath, "steamapps", "common");
                            if (Directory.Exists(gamePath))
                            {
                                paths.Add(gamePath);
                                System.Diagnostics.Debug.WriteLine($"Found Steam library: {gamePath}");
                            }
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"Steam detector found {paths.Count} library paths");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error detecting Steam libraries: {ex.Message}");
            }

            return paths;
        }

        private string? GetSteamInstallPath()
        {
            try
            {
                // Try 64-bit registry first
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam"))
                {
                    if (key?.GetValue("InstallPath") is string path64)
                    {
                        System.Diagnostics.Debug.WriteLine($"Found Steam path in 64-bit registry: {path64}");
                        return path64;
                    }
                }

                // Try 32-bit registry
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
                {
                    if (key?.GetValue("InstallPath") is string path32)
                    {
                        System.Diagnostics.Debug.WriteLine($"Found Steam path in 32-bit registry: {path32}");
                        return path32;
                    }
                }

                // Fallback to common Steam locations
                var commonPaths = new[]
                {
                    @"C:\Program Files (x86)\Steam",
                    @"C:\Program Files\Steam",
                    @"D:\Steam",
                    @"E:\Steam"
                };

                foreach (var commonPath in commonPaths)
                {
                    if (Directory.Exists(commonPath) && 
                        File.Exists(Path.Combine(commonPath, "steam.exe")))
                    {
                        System.Diagnostics.Debug.WriteLine($"Found Steam at common location: {commonPath}");
                        return commonPath;
                    }
                }

                System.Diagnostics.Debug.WriteLine("Steam installation not found");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error finding Steam installation: {ex.Message}");
                return null;
            }
        }
    }

    public class UWPDetector : ILauncherDetector
    {
        public string LauncherName => "Game Pass/UWP";

        public List<string> GetLibraryPaths()
        {
            var paths = new List<string>();

            try
            {
                // Get all drives and check for WindowsApps directories
                var drives = DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed);

                foreach (var drive in drives)
                {
                    var windowsAppsPaths = new[]
                    {
                        Path.Combine(drive.RootDirectory.FullName, "Program Files", "WindowsApps"),
                        Path.Combine(drive.RootDirectory.FullName, "Program Files (x86)", "WindowsApps"),
                        Path.Combine(drive.RootDirectory.FullName, "WindowsApps")
                    };

                    foreach (var windowsAppsPath in windowsAppsPaths)
                    {
                        if (Directory.Exists(windowsAppsPath))
                        {
                            try
                            {
                                // Scan for game packages in WindowsApps
                                var gamePackages = FindGamePackages(windowsAppsPath);
                                paths.AddRange(gamePackages);
                                System.Diagnostics.Debug.WriteLine($"Found {gamePackages.Count} UWP game packages in: {windowsAppsPath}");
                            }
                            catch (UnauthorizedAccessException)
                            {
                                // WindowsApps often requires admin access, but we can still add the directory
                                // and let the IsXboxGame method handle filtering
                                paths.Add(windowsAppsPath);
                                System.Diagnostics.Debug.WriteLine($"Added WindowsApps directory (access limited): {windowsAppsPath}");
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error scanning WindowsApps directory {windowsAppsPath}: {ex.Message}");
                            }
                        }
                    }
                }

                // Also check for Microsoft.Gaming directories
                foreach (var drive in drives)
                {
                    var gamingPath = Path.Combine(drive.RootDirectory.FullName, "Microsoft.Gaming");
                    if (Directory.Exists(gamingPath))
                    {
                        paths.Add(gamingPath);
                        System.Diagnostics.Debug.WriteLine($"Found Microsoft.Gaming directory: {gamingPath}");
                    }
                }

                System.Diagnostics.Debug.WriteLine($"UWP detector found {paths.Count} library paths");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error detecting UWP libraries: {ex.Message}");
            }

            return paths;
        }

        private List<string> FindGamePackages(string windowsAppsPath)
        {
            var gamePaths = new List<string>();

            try
            {
                var directories = Directory.GetDirectories(windowsAppsPath);

                foreach (var directory in directories)
                {
                    var dirName = Path.GetFileName(directory);
                    
                    // Check if this directory matches Game Pass/Xbox game patterns
                    if (IsLikelyGamePackage(dirName))
                    {
                        gamePaths.Add(directory);
                        System.Diagnostics.Debug.WriteLine($"Found potential game package: {dirName}");
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Expected for some WindowsApps subdirectories
                System.Diagnostics.Debug.WriteLine($"Access denied scanning packages in: {windowsAppsPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error scanning packages in {windowsAppsPath}: {ex.Message}");
            }

            return gamePaths;
        }

        private bool IsLikelyGamePackage(string packageName)
        {
            // Game Pass and Xbox games typically have these patterns in their package names
            var gameIndicators = new[]
            {
                "_8wekyb3d8bbwe", // Microsoft Corporation
                "_cw5n1h2txyewy", // Another Microsoft suffix
                "Microsoft.",
                "MicrosoftCorporation",
                "Xbox",
                "GamePass"
            };

            // Exclude known system/non-game packages
            var systemExclusions = new[]
            {
                "Microsoft.Windows",
                "Microsoft.VCLibs",
                "Microsoft.DirectX",
                "Microsoft.Services",
                "Microsoft.UI",
                "Microsoft.DesktopAppInstaller",
                "Microsoft.Store",
                "Microsoft.Xbox.TCUI",
                "Microsoft.XboxGameOverlay",
                "Microsoft.XboxGamingOverlay",
                "Microsoft.XboxIdentityProvider",
                "Microsoft.XboxSpeechToTextOverlay"
            };

            var lowerPackageName = packageName.ToLowerInvariant();

            // Exclude system packages first
            if (systemExclusions.Any(exclusion => lowerPackageName.Contains(exclusion.ToLowerInvariant())))
            {
                return false;
            }

            // Check for game indicators
            return gameIndicators.Any(indicator => lowerPackageName.Contains(indicator.ToLowerInvariant()));
        }
    }

    public class GameLauncherConfigService
    {
        private readonly List<ILauncherDetector> _detectors;
        private List<string>? _cachedGamePaths;

        public GameLauncherConfigService()
        {
            _detectors = new List<ILauncherDetector>
            {
                new SteamDetector(),
                new UWPDetector()
                // Future: Add EpicDetector, OriginDetector, etc.
            };
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