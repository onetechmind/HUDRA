using HUDRA.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GameLib;

namespace HUDRA.Services.GameLibraryProviders
{
    public class GameLibNetProvider : IGameLibraryProvider
    {
        public string ProviderName => "GameLib.NET";
        public GameSource GameSource => GameSource.Directory; // Will be overridden per launcher
        public bool IsAvailable { get; private set; } = true;

        // Cached LauncherManager to avoid repeated MEF DirectoryCatalog scans (1-2 sec each)
        private static LauncherManager? _cachedLauncherManager;
        private static readonly object _launcherLock = new object();

        public event EventHandler<string>? ScanProgressChanged;

        /// <summary>
        /// Clears the cached LauncherManager to force a fresh scan.
        /// This is necessary to detect newly installed games during manual rescans.
        /// </summary>
        public void ClearCache()
        {
            lock (_launcherLock)
            {
                _cachedLauncherManager = null;
                System.Diagnostics.Debug.WriteLine("GameLib.NET: Cache cleared - next scan will create fresh LauncherManager");
            }
        }

        public async Task<Dictionary<string, DetectedGame>> GetGamesAsync()
        {
            var detectedGames = new Dictionary<string, DetectedGame>(StringComparer.OrdinalIgnoreCase);

            try
            {
                ScanProgressChanged?.Invoke(this, "Initializing GameLib.NET...");
                System.Diagnostics.Debug.WriteLine("GameLib.NET: Starting scan...");

                // Get or create cached LauncherManager on background thread to avoid blocking UI
                // MEF DirectoryCatalog scan can take several seconds
                var (launcherManager, launchers) = await Task.Run(() =>
                {
                    LauncherManager manager;
                    lock (_launcherLock)
                    {
                        if (_cachedLauncherManager == null)
                        {
                            System.Diagnostics.Debug.WriteLine("GameLib.NET: Creating LauncherManager (first time - MEF scan)...");

                            // Suppress MEF DirectoryCatalog warnings during first-time initialization
                            // GameLib.NET scans all DLLs in the app directory, causing warnings for native DLLs
                            var originalListeners = Trace.Listeners.Cast<TraceListener>().ToArray();
                            Trace.Listeners.Clear();
                            try
                            {
                                _cachedLauncherManager = new LauncherManager();
                            }
                            finally
                            {
                                Trace.Listeners.AddRange(originalListeners);
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("GameLib.NET: Using cached LauncherManager");
                        }
                        manager = _cachedLauncherManager;
                    }

                    // GetLaunchers can also be slow, so run it on background thread too
                    var launcherList = manager.GetLaunchers();
                    return (manager, launcherList);
                });

                System.Diagnostics.Debug.WriteLine($"GameLib.NET: Found {launchers?.Count() ?? 0} launchers");
                ScanProgressChanged?.Invoke(this, $"Found {launchers?.Count() ?? 0} launchers");

                foreach (var launcher in launchers)
                {
                    try
                    {
                        var launcherName = launcher.Name;
                        System.Diagnostics.Debug.WriteLine($"GameLib.NET: Scanning {launcherName}...");
                        ScanProgressChanged?.Invoke(this, $"Scanning {launcherName}...");

                        var gameSource = GetGameSourceFromLauncher(launcherName);
                        var gameCount = launcher.Games?.Count() ?? 0;
                        System.Diagnostics.Debug.WriteLine($"GameLib.NET: {launcherName} has {gameCount} games");
                        
                        foreach (var game in launcher.Games)
                        {
                            try
                            {
                                // Get game properties using reflection to check available properties
                                var gameType = game.GetType();
                                var properties = gameType.GetProperties().ToDictionary(p => p.Name, p => p);

                                System.Diagnostics.Debug.WriteLine($"GameLib.NET: Processing game from {launcherName}, available properties: {string.Join(", ", properties.Keys)}");

                                // Try to get executable path (Steam and Ubisoft have special handling)
                                string? executablePath = null;

                                // For Steam games, construct path from InstallDir + executable info
                                if (launcherName.ToLowerInvariant().Contains("steam"))
                                {
                                    executablePath = GetSteamExecutablePath(game, properties);
                                }
                                // For Ubisoft games, use similar special handling
                                else if (launcherName.ToLowerInvariant().Contains("ubisoft") || launcherName.ToLowerInvariant().Contains("uplay"))
                                {
                                    executablePath = GetUbisoftExecutablePath(game, properties);
                                }
                                else
                                {
                                    // For other launchers, try standard properties
                                    var executableProperties = new[] { "ExecutablePath", "ExePath", "Executable", "Path", "LaunchPath", "Target", "Command" };

                                    foreach (var propName in executableProperties)
                                    {
                                        if (properties.ContainsKey(propName))
                                        {
                                            executablePath = properties[propName].GetValue(game)?.ToString();
                                            System.Diagnostics.Debug.WriteLine($"GameLib.NET: Found executable using property '{propName}': {executablePath}");
                                            if (!string.IsNullOrWhiteSpace(executablePath))
                                                break;
                                        }
                                    }
                                }

                                System.Diagnostics.Debug.WriteLine($"GameLib.NET: Found executable path: '{executablePath}'");

                                // Skip games without executable paths
                                if (string.IsNullOrWhiteSpace(executablePath))
                                {
                                    System.Diagnostics.Debug.WriteLine($"GameLib.NET: Skipping game - no executable path");
                                    continue;
                                }

                                if (!File.Exists(executablePath))
                                {
                                    System.Diagnostics.Debug.WriteLine($"GameLib.NET: Skipping game - executable does not exist: {executablePath}");
                                    continue;
                                }

                                var processName = Path.GetFileNameWithoutExtension(executablePath);
                                
                                // Skip if we already have this process from another launcher
                                if (detectedGames.ContainsKey(processName))
                                {
                                    System.Diagnostics.Debug.WriteLine($"GameLib.NET: Skipping duplicate game: {processName}");
                                    continue;
                                }

                                // Try to get game name
                                string? gameName = null;
                                if (properties.ContainsKey("Name"))
                                    gameName = properties["Name"].GetValue(game)?.ToString();
                                else if (properties.ContainsKey("Title"))
                                    gameName = properties["Title"].GetValue(game)?.ToString();

                                // Try to get install directory
                                string? installDir = null;
                                if (properties.ContainsKey("InstallDir"))
                                    installDir = properties["InstallDir"].GetValue(game)?.ToString();
                                else if (properties.ContainsKey("InstallLocation"))
                                    installDir = properties["InstallLocation"].GetValue(game)?.ToString();
                                else if (properties.ContainsKey("Path"))
                                    installDir = properties["Path"].GetValue(game)?.ToString();

                                // Try to get game ID
                                string? gameId = null;
                                if (properties.ContainsKey("GameId"))
                                    gameId = properties["GameId"].GetValue(game)?.ToString();
                                else if (properties.ContainsKey("Id"))
                                    gameId = properties["Id"].GetValue(game)?.ToString();

                                // Construct proper platform launch URL
                                string launcherInfo = ConstructLauncherUrl(launcherName, gameId);

                                var detectedGame = new DetectedGame
                                {
                                    ProcessName = processName,
                                    DisplayName = !string.IsNullOrWhiteSpace(gameName) ? gameName : processName,
                                    ExecutablePath = executablePath,
                                    InstallLocation = !string.IsNullOrWhiteSpace(installDir) ? installDir : Path.GetDirectoryName(executablePath) ?? string.Empty,
                                    Source = gameSource,
                                    LauncherInfo = launcherInfo,
                                    PackageInfo = !string.IsNullOrWhiteSpace(gameId) ? gameId : string.Empty,
                                    LastDetected = DateTime.Now
                                };

                                detectedGames[processName] = detectedGame;
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error processing game from {launcherName}: {ex.Message}");
                            }
                        }

                        ScanProgressChanged?.Invoke(this, $"Completed {launcherName} - found {launcher.Games?.Count() ?? 0} games");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error scanning launcher {launcher.Name}: {ex.Message}");
                        ScanProgressChanged?.Invoke(this, $"Error scanning {launcher.Name}");
                    }
                }

                ScanProgressChanged?.Invoke(this, $"GameLib.NET scan complete - {detectedGames.Count} games found");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GameLib.NET provider error: {ex.Message}");
                ScanProgressChanged?.Invoke(this, "GameLib.NET scan failed");
                IsAvailable = false;
            }

            return detectedGames;
        }

        private string? GetSteamExecutablePath(object game, Dictionary<string, System.Reflection.PropertyInfo> properties)
        {
            try
            {
                // Get base install directory
                string? installDir = null;
                if (properties.ContainsKey("InstallDir"))
                    installDir = properties["InstallDir"].GetValue(game)?.ToString();

                System.Diagnostics.Debug.WriteLine($"GameLib.NET: Steam InstallDir: '{installDir}'");

                if (string.IsNullOrWhiteSpace(installDir))
                    return null;

                // Try to get executable name from various sources
                string? executableName = null;

                // Method 1: Check Executables (plural) property
                if (properties.ContainsKey("Executables"))
                {
                    var executables = properties["Executables"].GetValue(game);
                    System.Diagnostics.Debug.WriteLine($"GameLib.NET: Steam Executables property type: {executables?.GetType()}");
                    
                    if (executables != null)
                    {
                        // Try to handle as collection
                        if (executables is System.Collections.IEnumerable enumerable && !(executables is string))
                        {
                            foreach (var item in enumerable)
                            {
                                var itemStr = item?.ToString();
                                System.Diagnostics.Debug.WriteLine($"GameLib.NET: Steam Executables item: '{itemStr}'");
                                if (!string.IsNullOrWhiteSpace(itemStr) && itemStr.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                {
                                    executableName = itemStr;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            executableName = executables.ToString();
                        }
                    }
                }

                // Method 2: Parse LaunchString if no executable found yet
                if (string.IsNullOrWhiteSpace(executableName) && properties.ContainsKey("LaunchString"))
                {
                    var launchString = properties["LaunchString"].GetValue(game)?.ToString();
                    System.Diagnostics.Debug.WriteLine($"GameLib.NET: Steam LaunchString: '{launchString}'");
                    
                    if (!string.IsNullOrWhiteSpace(launchString))
                    {
                        // Extract executable from launch string (usually first .exe file mentioned)
                        var parts = launchString.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var part in parts)
                        {
                            var cleanPart = part.Trim('"', '\'');
                            if (cleanPart.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                executableName = Path.GetFileName(cleanPart);
                                break;
                            }
                        }
                    }
                }

                // Method 3: Look for common executable patterns in install directory
                if (string.IsNullOrWhiteSpace(executableName))
                {
                    System.Diagnostics.Debug.WriteLine($"GameLib.NET: Trying to find .exe files in Steam install directory");
                    if (Directory.Exists(installDir))
                    {
                        var exeFiles = Directory.GetFiles(installDir, "*.exe", SearchOption.TopDirectoryOnly);
                        if (exeFiles.Length > 0)
                        {
                            executableName = Path.GetFileName(exeFiles[0]);
                            System.Diagnostics.Debug.WriteLine($"GameLib.NET: Found exe file in directory: '{executableName}'");
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(executableName))
                {
                    var fullPath = Path.Combine(installDir, executableName);
                    System.Diagnostics.Debug.WriteLine($"GameLib.NET: Constructed Steam executable path: '{fullPath}'");
                    return fullPath;
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GameLib.NET: Error getting Steam executable path: {ex.Message}");
                return null;
            }
        }

        private string? GetUbisoftExecutablePath(object game, Dictionary<string, System.Reflection.PropertyInfo> properties)
        {
            try
            {
                // Get base install directory
                string? installDir = null;
                if (properties.ContainsKey("InstallDir"))
                    installDir = properties["InstallDir"].GetValue(game)?.ToString();

                System.Diagnostics.Debug.WriteLine($"GameLib.NET: Ubisoft InstallDir: '{installDir}'");

                if (string.IsNullOrWhiteSpace(installDir))
                    return null;

                // Try to get executable name from various sources
                string? executableName = null;

                // Method 1: Check Executables (plural) property
                if (properties.ContainsKey("Executables"))
                {
                    var executables = properties["Executables"].GetValue(game);
                    System.Diagnostics.Debug.WriteLine($"GameLib.NET: Ubisoft Executables property type: {executables?.GetType()}");

                    if (executables != null)
                    {
                        // Try to handle as collection
                        if (executables is System.Collections.IEnumerable enumerable && !(executables is string))
                        {
                            foreach (var item in enumerable)
                            {
                                var itemStr = item?.ToString();
                                System.Diagnostics.Debug.WriteLine($"GameLib.NET: Ubisoft Executables item: '{itemStr}'");
                                if (!string.IsNullOrWhiteSpace(itemStr) && itemStr.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                {
                                    executableName = itemStr;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            executableName = executables.ToString();
                        }
                    }
                }

                // Method 2: Parse LaunchString if no executable found yet
                if (string.IsNullOrWhiteSpace(executableName) && properties.ContainsKey("LaunchString"))
                {
                    var launchString = properties["LaunchString"].GetValue(game)?.ToString();
                    System.Diagnostics.Debug.WriteLine($"GameLib.NET: Ubisoft LaunchString: '{launchString}'");

                    if (!string.IsNullOrWhiteSpace(launchString))
                    {
                        // Extract executable from launch string (usually first .exe file mentioned)
                        var parts = launchString.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var part in parts)
                        {
                            var cleanPart = part.Trim('"', '\'');
                            if (cleanPart.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                // Check if it's a full path or just filename
                                if (Path.IsPathRooted(cleanPart))
                                {
                                    System.Diagnostics.Debug.WriteLine($"GameLib.NET: Found full path in LaunchString: '{cleanPart}'");
                                    return cleanPart; // Return full path directly
                                }
                                else
                                {
                                    executableName = Path.GetFileName(cleanPart);
                                    break;
                                }
                            }
                        }
                    }
                }

                // Method 3: Try Executable (singular) property as fallback
                if (string.IsNullOrWhiteSpace(executableName) && properties.ContainsKey("Executable"))
                {
                    executableName = properties["Executable"].GetValue(game)?.ToString();
                    System.Diagnostics.Debug.WriteLine($"GameLib.NET: Ubisoft Executable (singular): '{executableName}'");
                }

                // Method 4: Look for common executable patterns in install directory
                if (string.IsNullOrWhiteSpace(executableName))
                {
                    System.Diagnostics.Debug.WriteLine($"GameLib.NET: Trying to find .exe files in Ubisoft install directory");
                    if (Directory.Exists(installDir))
                    {
                        var exeFiles = Directory.GetFiles(installDir, "*.exe", SearchOption.TopDirectoryOnly);
                        if (exeFiles.Length > 0)
                        {
                            executableName = Path.GetFileName(exeFiles[0]);
                            System.Diagnostics.Debug.WriteLine($"GameLib.NET: Found exe file in directory: '{executableName}'");
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(executableName))
                {
                    var fullPath = Path.Combine(installDir, executableName);
                    System.Diagnostics.Debug.WriteLine($"GameLib.NET: Constructed Ubisoft executable path: '{fullPath}'");
                    return fullPath;
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GameLib.NET: Error getting Ubisoft executable path: {ex.Message}");
                return null;
            }
        }

        private GameSource GetGameSourceFromLauncher(string launcherName)
        {
            return launcherName.ToLowerInvariant() switch
            {
                var name when name.Contains("battlenet") || name.Contains("battle.net") || name.Contains("battle net") => GameSource.BattleNet,
                var name when name.Contains("epic") => GameSource.Epic,
                var name when name.Contains("gog") => GameSource.GOG,
                var name when name.Contains("origin") => GameSource.Origin,
                var name when name.Contains("riot") => GameSource.Riot,
                var name when name.Contains("rockstar") => GameSource.Rockstar,
                var name when name.Contains("steam") => GameSource.Steam,
                var name when name.Contains("ubisoft") || name.Contains("uplay") => GameSource.Ubisoft,
                _ => GameSource.Directory
            };
        }

        /// <summary>
        /// Constructs a proper platform-specific launch URL from launcher name and game ID
        /// </summary>
        private string ConstructLauncherUrl(string launcherName, string? gameId)
        {
            if (string.IsNullOrWhiteSpace(gameId))
            {
                // No game ID available - return empty string to skip platform launch
                return string.Empty;
            }

            var lowerLauncherName = launcherName.ToLowerInvariant();

            // Construct platform-specific protocol URLs
            if (lowerLauncherName.Contains("steam"))
            {
                // Steam protocol: steam://rungameid/{appId}
                return $"steam://rungameid/{gameId}";
            }
            else if (lowerLauncherName.Contains("epic"))
            {
                // Epic Games Launcher protocol
                return $"com.epicgames.launcher://apps/{gameId}?action=launch&silent=true";
            }
            else if (lowerLauncherName.Contains("gog"))
            {
                // GOG Galaxy protocol
                return $"goggalaxy://openGameView/{gameId}";
            }
            else if (lowerLauncherName.Contains("origin"))
            {
                // Origin protocol (deprecated, but still supported)
                return $"origin://launchgame/{gameId}";
            }
            else if (lowerLauncherName.Contains("ubisoft") || lowerLauncherName.Contains("uplay"))
            {
                // Ubisoft Connect (formerly Uplay) protocol
                return $"uplay://launch/{gameId}/0";
            }
            else if (lowerLauncherName.Contains("rockstar"))
            {
                // Rockstar Games Launcher protocol
                return $"rockstar://launch/{gameId}";
            }
            else if (lowerLauncherName.Contains("battlenet") || lowerLauncherName.Contains("battle.net") || lowerLauncherName.Contains("battle net"))
            {
                // Battle.net protocol
                return $"battlenet://{gameId}";
            }

            // Unknown launcher - return empty to skip platform launch and use direct executable
            System.Diagnostics.Debug.WriteLine($"GameLib.NET: Unknown launcher '{launcherName}' - no protocol URL constructed");
            return string.Empty;
        }
    }

}