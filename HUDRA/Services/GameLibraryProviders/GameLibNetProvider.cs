using HUDRA.Models;
using System;
using System.Collections.Generic;
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

        public event EventHandler<string>? ScanProgressChanged;

        public async Task<Dictionary<string, DetectedGame>> GetGamesAsync()
        {
            var detectedGames = new Dictionary<string, DetectedGame>(StringComparer.OrdinalIgnoreCase);

            try
            {
                ScanProgressChanged?.Invoke(this, "Initializing GameLib.NET...");
                System.Diagnostics.Debug.WriteLine("GameLib.NET: Starting scan...");

                var launcherManager = new LauncherManager();
                var launchers = launcherManager.GetLaunchers();

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

                                // Try to get executable path (Steam has special handling)
                                string? executablePath = null;
                                
                                // For Steam games, construct path from InstallDir + executable info
                                if (launcherName.ToLowerInvariant().Contains("steam"))
                                {
                                    executablePath = GetSteamExecutablePath(game, properties);
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

                                var detectedGame = new DetectedGame
                                {
                                    ProcessName = processName,
                                    DisplayName = !string.IsNullOrWhiteSpace(gameName) ? gameName : processName,
                                    ExecutablePath = executablePath,
                                    InstallLocation = !string.IsNullOrWhiteSpace(installDir) ? installDir : Path.GetDirectoryName(executablePath) ?? string.Empty,
                                    Source = gameSource,
                                    LauncherInfo = launcherName,
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

        private GameSource GetGameSourceFromLauncher(string launcherName)
        {
            return launcherName.ToLowerInvariant() switch
            {
                var name when name.Contains("steam") => GameSource.Steam,
                var name when name.Contains("epic") => GameSource.Epic,
                var name when name.Contains("origin") => GameSource.Origin,
                var name when name.Contains("gog") => GameSource.GOG,
                var name when name.Contains("ubisoft") || name.Contains("uplay") => GameSource.Ubisoft,
                _ => GameSource.Directory
            };
        }
    }
}