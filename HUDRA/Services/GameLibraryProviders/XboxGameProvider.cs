using HUDRA.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HUDRA.Services.GameLibraryProviders
{
    public class XboxGameProvider : IGameLibraryProvider
    {
        public string ProviderName => "Xbox PowerShell";
        public GameSource GameSource => GameSource.Xbox;
        public bool IsAvailable { get; private set; } = true;

        public event EventHandler<string>? ScanProgressChanged;

        public XboxGameProvider()
        {
        }

        /// <summary>
        /// Clears any cached data. Xbox provider runs fresh PowerShell each scan,
        /// so this is a no-op but required by IGameLibraryProvider interface.
        /// </summary>
        public void ClearCache()
        {
            // Xbox provider doesn't cache - runs fresh PowerShell script each time
            System.Diagnostics.Debug.WriteLine("Xbox provider: ClearCache called (no-op - no caching)");
        }

        public async Task<Dictionary<string, DetectedGame>> GetGamesAsync()
        {
            var detectedGames = new Dictionary<string, DetectedGame>(StringComparer.OrdinalIgnoreCase);

            try
            {
                ScanProgressChanged?.Invoke(this, "Scanning Xbox/Game Pass games...");

                // Use timeout to prevent hanging
                using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                
                // Run PowerShell as external process to avoid SDK compatibility issues
                var (exitCode, stdout, stderr) = await RunPowerShellProcessAsync(GetXboxGamesScript(), cancellationTokenSource.Token);

                if (exitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
                {
                    // Parse JSON output from PowerShell
                    var gameResults = ParseXboxGameResults(stdout);

                    ScanProgressChanged?.Invoke(this, $"Processing {gameResults.Count} Xbox games...");

                    foreach (var gameResult in gameResults)
                    {
                        try
                        {
                            if (string.IsNullOrWhiteSpace(gameResult.ProcessName) || string.IsNullOrWhiteSpace(gameResult.ExecutablePath))
                                continue;

                            // Verify the executable exists
                            if (!File.Exists(gameResult.ExecutablePath))
                                continue;

                            // Skip if we already have this process
                            if (detectedGames.ContainsKey(gameResult.ProcessName))
                                continue;

                            var detectedGame = new DetectedGame
                            {
                                ProcessName = gameResult.ProcessName,
                                DisplayName = !string.IsNullOrWhiteSpace(gameResult.GameName) ? gameResult.GameName : gameResult.ProcessName,
                                ExecutablePath = gameResult.ExecutablePath,
                                InstallLocation = gameResult.InstallLocation ?? string.Empty,
                                Source = GameSource.Xbox,
                                LauncherInfo = "Xbox/Game Pass",
                                PackageInfo = gameResult.PackageFullName ?? string.Empty,
                                LastDetected = DateTime.Now,
                                AlternativeExecutables = gameResult.AlternativeExecutables ?? new List<string>()
                            };

                            detectedGames[gameResult.ProcessName] = detectedGame;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error processing Xbox game result: {ex.Message}");
                        }
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Xbox provider: PowerShell process failed. Exit code: {exitCode}");
                    if (!string.IsNullOrWhiteSpace(stderr))
                    {
                        System.Diagnostics.Debug.WriteLine($"Xbox provider: PowerShell errors: {stderr}");
                    }
                    
                    // Don't disable provider for process failures - might be temporary
                    ScanProgressChanged?.Invoke(this, "PowerShell process error - retrying next scan");
                }

                ScanProgressChanged?.Invoke(this, $"Xbox scan complete - {detectedGames.Count} games found");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Xbox provider: Top-level error - {ex.Message}");
                ScanProgressChanged?.Invoke(this, "Xbox scan failed");
                // Don't disable provider permanently for top-level errors
            }

            return detectedGames;
        }

        private async Task<(int exitCode, string stdout, string stderr)> RunPowerShellProcessAsync(string script, CancellationToken cancellationToken)
        {
            // Try PowerShell Core first, then Windows PowerShell
            var powerShellExecutables = new[] { "pwsh.exe", "powershell.exe" };
            
            foreach (var psExe in powerShellExecutables)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = psExe,
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                    
                    var stdout = new StringBuilder();
                    var stderr = new StringBuilder();
                    
                    var tcs = new TaskCompletionSource<(int, string, string)>();
                    
                    process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                    process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
                    
                    process.Exited += (_, _) =>
                    {
                        tcs.TrySetResult((process.ExitCode, stdout.ToString().TrimEnd(), stderr.ToString().TrimEnd()));
                    };

                    if (!process.Start())
                    {
                        System.Diagnostics.Debug.WriteLine($"Xbox provider: Failed to start {psExe}");
                        continue; // Try next executable
                    }

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    using (cancellationToken.Register(() =>
                    {
                        try
                        {
                            if (!process.HasExited)
                                process.Kill();
                        }
                        catch { }
                    }))
                    {
                        return await tcs.Task;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Xbox provider: Failed to execute {psExe}: {ex.Message}");
                    continue; // Try next executable
                }
            }

            // If we get here, neither PowerShell executable worked
            return (-1, string.Empty, "No PowerShell executable found or all attempts failed");
        }

        private string GetXboxGamesScript()
        {
            return @"
                try {
                    $games = @()
                    
                    # Get all AppX packages first
                    $allPackages = Get-AppxPackage
                    
                    # Filter packages for games
                    $nonFramework = $allPackages | Where-Object { !$_.IsFramework }
                    $removablePackages = $nonFramework | Where-Object { !$_.NonRemovable }
                    $storePackages = $removablePackages | Where-Object { $_.SignatureKind -eq 'Store' }
                    
                    # Check for MicrosoftGame.config
                    $gamePackages = @()
                    foreach ($package in $storePackages) {
                        $configPath = Join-Path $package.InstallLocation 'MicrosoftGame.config'
                        if (Test-Path $configPath) {
                            $gamePackages += $package
                        }
                    }
                    
                    # Process each game package
                    foreach ($package in $gamePackages) {
                        try {
                            $actualLocation = if ((Get-Item $package.InstallLocation).LinkType -eq 'Junction') {
                                (Get-Item $package.InstallLocation).Target
                            } else {
                                $package.InstallLocation
                            }
                            
                            $configPath = Join-Path $actualLocation 'MicrosoftGame.config'
                            $config = [xml](Get-Content $configPath)
                            $exeName = [System.IO.Path]::GetFileNameWithoutExtension($config.Game.ExecutableList.Executable.Name)

                            # Extract display name from the parent folder name (go up one level from Content)
                            $folderName = Split-Path -Leaf (Split-Path -Parent $actualLocation)

                            # Try to resolve symlink/junction to get actual folder name
                            # Xbox uses symlinks for games with special characters (e.g., commas)
                            $realFolderName = $folderName
                            try {
                                $parentPath = Split-Path -Parent $actualLocation
                                $item = Get-Item $parentPath -ErrorAction SilentlyContinue

                                if ($item.LinkType -eq 'Junction' -or $item.LinkType -eq 'SymbolicLink') {
                                    $targetPath = $item.Target
                                    if (![string]::IsNullOrWhiteSpace($targetPath)) {
                                        $realFolderName = Split-Path -Leaf $targetPath
                                    }
                                }
                            } catch {
                                # Symlink resolution failed - stick with original folder name
                            }

                            # Check if folder name is a GUID pattern (fallback to exe name if so)
                            $isGuid = $realFolderName -match '^[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$'

                            # Scan for all .exe files up to 5 levels deep in the game folder
                            # This helps detect games where the actual running exe differs from MicrosoftGame.config
                            $allExeNames = @()
                            try {
                                # Get all .exe files in root and up to 5 levels of subfolders
                                $exeFiles = Get-ChildItem -Path $actualLocation -Filter '*.exe' -Recurse -Depth 5 -ErrorAction SilentlyContinue
                                $allExeNames = $exeFiles | ForEach-Object { [System.IO.Path]::GetFileNameWithoutExtension($_.Name) } | Select-Object -Unique
                            } catch {
                                # If enumeration fails, just use the main exe from config
                                $allExeNames = @($exeName)
                            }

                            # Prioritize real folder name, fall back to exe name for GUIDs
                            # This ensures:
                            # - ""Little Kitty, Big City"" uses folder name (not GUID)
                            # - ""Metaphor ReFantazio"" uses folder name (not exe ""METAPHOR"")
                            $displayName = if (!$isGuid -and ![string]::IsNullOrWhiteSpace($realFolderName)) {
                                $realFolderName
                            } elseif (![string]::IsNullOrWhiteSpace($exeName)) {
                                $exeName
                            } elseif ($allExeNames.Count -gt 0) {
                                $allExeNames[0]
                            } else {
                                $package.Name
                            }

                            $gameObj = @{
                                ProcessName = $package.Name
                                GameName = $displayName
                                InstallLocation = $actualLocation
                                PackageFullName = $package.PackageFullName
                                ExecutablePath = Join-Path $actualLocation $config.Game.ExecutableList.Executable.Name
                                ExecutableName = $exeName
                                AlternativeExecutables = $allExeNames
                            }
                            
                            $games += $gameObj
                        } catch {
                            Write-Error ('Error processing Xbox package ' + $package.Name + ': ' + $_.Exception.Message)
                        }
                    }
                    
                    # Output result
                    if ($games.Count -gt 0) {
                        Write-Error ('Found ' + $games.Count + ' Xbox games')
                        $games | ConvertTo-Json -Depth 2
                    } else {
                        Write-Error ('No Xbox games found')
                        '[]'
                    }
                    
                } catch {
                    Write-Error ('Xbox scan error: ' + $_.Exception.Message)
                }
            ";
        }

        private List<XboxGameResult> ParseXboxGameResults(string jsonOutput)
        {
            var results = new List<XboxGameResult>();
            
            try
            {
                if (string.IsNullOrWhiteSpace(jsonOutput))
                    return results;

                // Handle both single object and array outputs
                if (jsonOutput.TrimStart().StartsWith("["))
                {
                    // Array of games
                    var gamesArray = JsonSerializer.Deserialize<XboxGameResult[]>(jsonOutput, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    
                    if (gamesArray != null)
                        results.AddRange(gamesArray);
                }
                else if (jsonOutput.TrimStart().StartsWith("{"))
                {
                    // Single game object
                    var game = JsonSerializer.Deserialize<XboxGameResult>(jsonOutput, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    
                    if (game != null)
                        results.Add(game);
                }
                else
                {
                    var preview = jsonOutput.Length > 100 ? jsonOutput.Substring(0, 100) + "..." : jsonOutput;
                    System.Diagnostics.Debug.WriteLine($"Xbox provider: Unexpected JSON format. Preview: {preview}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Xbox provider: JSON parsing error - {ex.Message}");
            }
            
            return results;
        }

        private class XboxGameResult
        {
            public string ProcessName { get; set; } = string.Empty;
            public string GameName { get; set; } = string.Empty;
            public string InstallLocation { get; set; } = string.Empty;
            public string PackageFullName { get; set; } = string.Empty;
            public string ExecutablePath { get; set; } = string.Empty;
            public List<string> AlternativeExecutables { get; set; } = new List<string>();
        }
    }
}