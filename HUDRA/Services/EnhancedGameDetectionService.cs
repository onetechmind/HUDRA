using HUDRA.Models;
using HUDRA.Services.GameLibraryProviders;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace HUDRA.Services
{
    public class EnhancedGameDetectionService : IDisposable
    {
        private readonly DispatcherQueue _dispatcher;
        private readonly List<IGameLibraryProvider> _providers;
        private Timer? _refreshTimer;
        private readonly Timer _detectionTimer;
        private readonly EnhancedGameDatabase _gameDatabase;
        private readonly SteamGridDbArtworkService? _artworkService;

        private Dictionary<string, DetectedGame> _cachedGames = new(StringComparer.OrdinalIgnoreCase);
        private GameInfo? _currentGame;
        private bool _disposed = false;
        private bool _isDatabaseReady = false;
        private bool _isScanning = false;
        private bool _isMonitoringActiveGame = false; // Flag to pause expensive process scanning when game is running
        

        // Enhanced scanning properties and events
        public event EventHandler<string>? ScanProgressChanged;
        public event EventHandler<bool>? ScanningStateChanged;
        public event EventHandler? DatabaseReady;

        // Maintain compatibility with existing GameDetectionService events
        public event EventHandler<GameInfo?>? GameDetected;
        public event EventHandler? GameStopped;
        
        public GameInfo? CurrentGame => _currentGame;
        public bool IsGameDatabaseReady => _isDatabaseReady;
        public bool IsScanning => _isScanning;
        public int GameDatabaseCount => _cachedGames.Count;
        public DatabaseStats DatabaseStats => _gameDatabase?.GetDatabaseStats() ?? new DatabaseStats();
        public bool IsEnhancedScanningActive => IsEnhancedScanningEnabled();

        public EnhancedGameDetectionService(DispatcherQueue dispatcher)
        {
            _dispatcher = dispatcher;

            // Initialize database
            _gameDatabase = new EnhancedGameDatabase();

            // Initialize SteamGridDB artwork service
            try
            {
                _artworkService = new SteamGridDbArtworkService("89b83ee6250e718cb40766bde7bcdf1d");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EnhancedGameDetection: Failed to initialize artwork service: {ex.Message}");
                _artworkService = null;
            }

            // Create providers
            _providers = new List<IGameLibraryProvider>
            {
                new GameLibNetProvider(),
                new XboxGameProvider()
            };

            // Subscribe to provider progress events
            foreach (var provider in _providers)
            {
                provider.ScanProgressChanged += OnProviderProgressChanged;
            }


            // Initialize detection system
            InitializeDetection();

            // Start main detection timer (5-second polling for efficiency)
            _detectionTimer = new Timer(DetectGamesCallback, null,
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
        }

        private void OnProviderProgressChanged(object? sender, string progress)
        {
            _dispatcher.TryEnqueue(() => ScanProgressChanged?.Invoke(this, progress));
        }

        private async Task BuildGameDatabaseAsync()
        {
            if (_isScanning) return;

            try
            {
                _isScanning = true;
                _dispatcher.TryEnqueue(() => ScanningStateChanged?.Invoke(this, true));
                
                _dispatcher.TryEnqueue(() => ScanProgressChanged?.Invoke(this, "Loading existing game database..."));

                // Load existing games from persistent database (async to avoid blocking UI)
                var allExistingGames = await _gameDatabase.GetAllGamesAsync();
                var existingGames = allExistingGames.ToDictionary(g => g.ProcessName, StringComparer.OrdinalIgnoreCase);
                var newGames = new Dictionary<string, DetectedGame>(StringComparer.OrdinalIgnoreCase);

                _dispatcher.TryEnqueue(() => ScanProgressChanged?.Invoke(this, $"Found {existingGames.Count} existing games, scanning for new games..."));

                // Scan providers for new games - each provider runs independently
                var availableProviders = _providers.Where(p => p.IsAvailable).ToList();
                
                var providerTasks = availableProviders.Select(async provider =>
                {
                    try
                    {
                        var providerGames = await provider.GetGamesAsync();
                        return (Provider: provider, Games: providerGames, Success: true);
                    }
                    catch (Exception ex)
                    {
                        // Provider scan failed - continue with other providers
                        return (Provider: provider, Games: new Dictionary<string, DetectedGame>(), Success: false);
                    }
                }).ToArray();

                var providerResults = await Task.WhenAll(providerTasks);

                // Collect all currently found games from providers
                var currentlyFoundGames = new Dictionary<string, DetectedGame>(StringComparer.OrdinalIgnoreCase);

                foreach (var result in providerResults)
                {
                    if (result.Success)
                    {
                        foreach (var kvp in result.Games)
                        {
                            if (!currentlyFoundGames.ContainsKey(kvp.Key))
                            {
                                currentlyFoundGames[kvp.Key] = kvp.Value;
                            }

                            if (!existingGames.ContainsKey(kvp.Key) && !newGames.ContainsKey(kvp.Key))
                            {
                                newGames[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                    else
                    {
                        // Mark provider as unavailable if it failed
                        result.Provider.GetType().GetProperty("IsAvailable")?.SetValue(result.Provider, false);
                    }
                }

                // Validate manual games - check if exe files still exist
                var manualGamesToRemove = existingGames.Values
                    .Where(g => g.Source == GameSource.Manual &&
                                !string.IsNullOrEmpty(g.ExecutablePath) &&
                                !System.IO.File.Exists(g.ExecutablePath))
                    .ToList();

                if (manualGamesToRemove.Any())
                {
                    _dispatcher.TryEnqueue(() => ScanProgressChanged?.Invoke(this, $"Removing {manualGamesToRemove.Count} manual games with missing executables..."));

                    foreach (var game in manualGamesToRemove)
                    {
                        System.Diagnostics.Debug.WriteLine($"Enhanced: Removing manual game with missing exe - Name: {game.DisplayName}, Path: {game.ExecutablePath}");
                        _gameDatabase.DeleteGame(game.ProcessName);
                    }
                }

                // Remove games from database that are no longer found (except Manual and Unknown sources)
                var gamesToRemove = existingGames.Values
                    .Where(g => !currentlyFoundGames.ContainsKey(g.ProcessName) &&
                                g.Source != GameSource.Manual &&
                                g.Source != GameSource.Unknown)
                    .ToList();

                // Combine both removal lists for reporting
                var totalGamesToRemove = gamesToRemove.Concat(manualGamesToRemove).ToList();

                if (gamesToRemove.Any())
                {
                    _dispatcher.TryEnqueue(() => ScanProgressChanged?.Invoke(this, $"Removing {gamesToRemove.Count} uninstalled games from database..."));

                    foreach (var game in gamesToRemove)
                    {
                        System.Diagnostics.Debug.WriteLine($"Enhanced: Removing uninstalled game from DB - Name: {game.DisplayName}, ProcessName: {game.ProcessName}");
                        _gameDatabase.DeleteGame(game.ProcessName);
                    }
                }

                // Save new games to database and add to learning inclusion list
                if (newGames.Any())
                {
                    _dispatcher.TryEnqueue(() => ScanProgressChanged?.Invoke(this, $"Saving {newGames.Count} new games to database..."));

                    foreach (var game in newGames.Values)
                    {
                        System.Diagnostics.Debug.WriteLine($"Enhanced: Saving game to DB - Name: {game.DisplayName}, ProcessName: {game.ProcessName}, ExecutablePath: {game.ExecutablePath}");
                        _gameDatabase.SaveGame(game);
                    }
                }

                // Update LastDetected timestamp for existing games that are still found
                var existingGamesToUpdate = existingGames.Values
                    .Where(g => currentlyFoundGames.ContainsKey(g.ProcessName))
                    .ToList();

                if (existingGamesToUpdate.Any())
                {
                    _dispatcher.TryEnqueue(() => ScanProgressChanged?.Invoke(this, $"Updating {existingGamesToUpdate.Count} existing games..."));

                    foreach (var game in existingGamesToUpdate)
                    {
                        game.LastDetected = DateTime.Now;
                        _gameDatabase.SaveGame(game);
                    }
                }

                // Build in-memory cache from all games (existing + new)
                var allGames = await _gameDatabase.GetAllGamesAsync();
                _cachedGames = allGames.ToDictionary(g => g.ProcessName, StringComparer.OrdinalIgnoreCase);

                _isDatabaseReady = true;

                // Download artwork for games that don't have it yet
                if (_artworkService != null && _cachedGames.Any())
                {
                    var gamesNeedingArtwork = _cachedGames.Values.Where(g => string.IsNullOrEmpty(g.ArtworkPath)).ToList();
                    if (gamesNeedingArtwork.Any())
                    {
                        await _artworkService.DownloadArtworkForGamesAsync(
                            gamesNeedingArtwork,
                            _gameDatabase,
                            progress => _dispatcher.TryEnqueue(() => ScanProgressChanged?.Invoke(this, progress))
                        );

                        // Reload cache after artwork update
                        allGames = await _gameDatabase.GetAllGamesAsync();
                        _cachedGames = allGames.ToDictionary(g => g.ProcessName, StringComparer.OrdinalIgnoreCase);
                    }
                }

                _dispatcher.TryEnqueue(() =>
                {
                    var statusParts = new List<string> { $"{_cachedGames.Count} games in database" };
                    if (newGames.Any()) statusParts.Add($"{newGames.Count} new");
                    if (totalGamesToRemove.Any()) statusParts.Add($"{totalGamesToRemove.Count} removed");

                    ScanProgressChanged?.Invoke(this, $"Scan complete - {string.Join(", ", statusParts)}");
                    DatabaseReady?.Invoke(this, EventArgs.Empty);
                });

            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error building game database: {ex.Message}");
                _dispatcher.TryEnqueue(() => ScanProgressChanged?.Invoke(this, "Database build failed"));
            }
            finally
            {
                _isScanning = false;
                _dispatcher.TryEnqueue(() => ScanningStateChanged?.Invoke(this, false));
            }
        }

        private async Task RefreshGameDatabaseAsync()
        {
            if (!IsEnhancedScanningEnabled())
                return;

            await BuildGameDatabaseAsync();
        }

        private async Task ResetDatabaseAsync()
        {
            if (!IsEnhancedScanningEnabled())
                return;

            // Clear the database
            _gameDatabase.ClearDatabase();

            // Clear in-memory cache
            _cachedGames.Clear();
            _isDatabaseReady = false;

            // Rebuild from scratch
            await BuildGameDatabaseAsync();
        }

        /// <summary>
        /// Initialize the detection system based on current settings
        /// </summary>
        private void InitializeDetection()
        {
            bool enhancedEnabled = IsEnhancedScanningEnabled();
            

            if (enhancedEnabled)
            {
                StartEnhancedDetection();
            }
            else
            {
                _isDatabaseReady = true; // Mark as "ready" so UI doesn't wait
                _dispatcher.TryEnqueue(() => DatabaseReady?.Invoke(this, EventArgs.Empty));
            }
        }

        /// <summary>
        /// Start enhanced detection mode (database-driven with directory fallback)
        /// </summary>
        private void StartEnhancedDetection()
        {
            
            // Build database
            Task.Run(async () => await BuildGameDatabaseAsync());

            // Setup periodic refresh
            int refreshIntervalMinutes = SettingsService.GetGameDatabaseRefreshInterval();
            _refreshTimer = new Timer(async _ => await RefreshGameDatabaseAsync(), null,
                TimeSpan.FromMinutes(refreshIntervalMinutes), TimeSpan.FromMinutes(refreshIntervalMinutes));
        }


        /// <summary>
        /// Centralized settings check for enhanced scanning
        /// </summary>
        private bool IsEnhancedScanningEnabled()
        {
            return SettingsService.IsEnhancedLibraryScanningEnabled();
        }

        /// <summary>
        /// Public method to check if enhanced scanning is enabled (for UI binding)
        /// </summary>
        public bool IsEnhancedScanningConfigured()
        {
            return IsEnhancedScanningEnabled();
        }

        /// <summary>
        /// Call this when settings change to update detection behavior
        /// </summary>
        public void OnSettingsChanged()
        {
            bool enhancedEnabled = IsEnhancedScanningEnabled();
            bool wasEnhanced = _refreshTimer != null; // Was enhanced mode active before?
            
            if (enhancedEnabled == wasEnhanced)
            {
                System.Diagnostics.Debug.WriteLine($"Enhanced: Settings changed but enhanced scanning remains {enhancedEnabled}, no action needed");
                return;
            }
            
            System.Diagnostics.Debug.WriteLine($"Enhanced: Settings changed, enhanced scanning: {wasEnhanced} -> {enhancedEnabled}");
            
            // Clean up current state
            CleanupDetection();
            
            // Reinitialize with new settings
            InitializeDetection();
        }

        /// <summary>
        /// Clean up resources from current detection state
        /// </summary>
        private void CleanupDetection()
        {
            _refreshTimer?.Dispose();
            _refreshTimer = null;
            
            // Reset database state when disabling enhanced scanning
            _isDatabaseReady = false;
            _cachedGames.Clear();
        }

        private void DetectGamesCallback(object? state)
        {
            if (_disposed) return;

            try
            {
                // Optimization: If we're monitoring an active game, just check if it's still running
                // This avoids expensive process scanning while a game is active
                GameInfo? detectedGame;
                if (_isMonitoringActiveGame && _currentGame != null)
                {
                    detectedGame = IsCurrentGameStillRunning();
                }
                else
                {
                    // No active game - do full process scan to find one
                    detectedGame = DetectActiveGame();
                }
                
                // Enhanced game change logic with window handle refresh
                bool gameChanged = false;
                bool handleRefreshed = false;

                if (_currentGame == null && detectedGame != null)
                {
                    gameChanged = true;
                    System.Diagnostics.Debug.WriteLine($"Enhanced: New game detected: {detectedGame.WindowTitle}");
                }
                else if (_currentGame != null && detectedGame == null)
                {
                    gameChanged = true;
                    System.Diagnostics.Debug.WriteLine($"Enhanced: Game stopped: {_currentGame.WindowTitle}");
                }
                else if (_currentGame != null && detectedGame != null &&
                         _currentGame.ProcessId != detectedGame.ProcessId)
                {
                    gameChanged = true;
                    System.Diagnostics.Debug.WriteLine($"Enhanced: Game changed: {_currentGame.WindowTitle} -> {detectedGame.WindowTitle}");
                }
                else if (_currentGame != null && detectedGame != null &&
                         _currentGame.ProcessId == detectedGame.ProcessId)
                {
                    // Same game still running - always refresh properties including window handle
                    if (_currentGame.WindowHandle != detectedGame.WindowHandle)
                    {
                        handleRefreshed = true;
                    }
                    
                    // Always update to get fresh window handle and other properties
                    _currentGame = detectedGame;
                }

                if (gameChanged)
                {
                    _currentGame = detectedGame;

                    // Update monitoring state
                    if (_currentGame != null)
                    {
                        // Game started - pause expensive process scanning
                        _isMonitoringActiveGame = true;
                        System.Diagnostics.Debug.WriteLine("Enhanced: Pausing process scanning - monitoring active game");
                    }
                    else
                    {
                        // Game stopped - resume process scanning
                        _isMonitoringActiveGame = false;
                        System.Diagnostics.Debug.WriteLine("Enhanced: Resuming process scanning - no active game");
                    }

                    _dispatcher.TryEnqueue(() =>
                    {
                        if (_currentGame != null)
                        {
                            GameDetected?.Invoke(this, _currentGame);
                        }
                        else
                        {
                            GameStopped?.Invoke(this, EventArgs.Empty);
                        }
                    });
                }
                else if (handleRefreshed)
                {
                    // Window handle was refreshed - notify UI with updated GameInfo
                    _dispatcher.TryEnqueue(() =>
                    {
                        if (_currentGame != null)
                        {
                            GameDetected?.Invoke(this, _currentGame);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Enhanced game detection error: {ex.Message}");
            }
        }

        private GameInfo? DetectActiveGame()
        {
            // Only detect games if enhanced scanning is enabled
            if (!IsEnhancedScanningEnabled())
            {
                return null; // No detection when disabled
            }

            // Enhanced detection (database + directory fallback)
            return DetectEnhancedGame();
        }

        /// <summary>
        /// Lightweight check if the current game is still running.
        /// Used when monitoring an active game to avoid expensive process scanning.
        /// Only checks the specific process ID rather than scanning all processes.
        /// </summary>
        private GameInfo? IsCurrentGameStillRunning()
        {
            if (_currentGame == null)
                return null;

            try
            {
                // Try to get the process by ID (lightweight operation)
                var process = Process.GetProcessById(_currentGame.ProcessId);

                // Check if process has exited
                if (process.HasExited)
                {
                    process.Dispose();
                    return null; // Game stopped
                }

                // Game still running - get fresh window handle
                var windowHandle = GetValidWindowHandle(process);

                // Return updated GameInfo with refreshed window handle
                var updatedGameInfo = new GameInfo
                {
                    ProcessName = _currentGame.ProcessName,
                    WindowTitle = _currentGame.WindowTitle,
                    ProcessId = _currentGame.ProcessId,
                    WindowHandle = windowHandle,
                    ExecutablePath = _currentGame.ExecutablePath
                };

                process.Dispose();
                return updatedGameInfo;
            }
            catch (ArgumentException)
            {
                // Process no longer exists
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Enhanced: Error checking if game still running: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Enhanced detection: Uses database-driven detection only (GameLib.NET + XboxGameProvider)
        /// </summary>
        private GameInfo? DetectEnhancedGame()
        {
            try
            {
                // Only use database-driven detection (GameLib.NET + XboxGameProvider)
                // No directory-based fallback to avoid false positives
                if (_isDatabaseReady)
                {
                    var databaseGame = GetRunningGameFromDatabase();
                    if (databaseGame != null)
                    {
                        return databaseGame;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Enhanced: Error in DetectEnhancedGame: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Optimized: Scan running processes and match executable paths against database
        /// Uses smart filtering to avoid system processes and reduce exceptions
        /// </summary>
        private GameInfo? GetRunningGameFromDatabase()
        {
            if (!_isDatabaseReady)
                return null;

            try
            {
                // Track last Xbox game match as fallback if no process has valid window handle
                DetectedGame? lastXboxMatch = null;
                Process? lastXboxProcess = null;

                // Get all processes and filter out system processes more aggressively
                var allProcesses = Process.GetProcesses();
                var candidateProcesses = allProcesses.Where(p =>
                    ShouldScanProcess(p)).ToList();

                foreach (var process in candidateProcesses)
                {
                    try
                    {
                        // Get the executable path of this process
                        string? processExePath = null;
                        try
                        {
                            processExePath = process.MainModule?.FileName;
                        }
                        catch (System.ComponentModel.Win32Exception)
                        {
                            // Access denied - skip silently (common for system processes)
                            continue;
                        }
                        catch
                        {
                            // Other errors - skip silently
                            continue;
                        }
                        
                        if (string.IsNullOrEmpty(processExePath))
                            continue;

                        // Check if this executable path matches any game in our database (exact path match)
                        var matchingGame = _cachedGames.Values.FirstOrDefault(dbGame =>
                            string.Equals(dbGame.ExecutablePath, processExePath, StringComparison.OrdinalIgnoreCase));

                        // If no exact path match, try Xbox fallback matching by executable name
                        // This handles Game Pass games where path junctions can cause mismatches
                        if (matchingGame == null)
                        {
                            matchingGame = TryMatchXboxGameByExecutableName(process.ProcessName, processExePath);
                        }

                        if (matchingGame != null)
                        {
                            // Get a valid window handle for the game
                            var windowHandle = GetValidWindowHandle(process);

                            // For Xbox games, if we don't get a valid window handle, keep looking
                            // Game Pass games often have multiple processes with the same exe name
                            if (windowHandle == IntPtr.Zero && matchingGame.Source == GameSource.Xbox)
                            {
                                System.Diagnostics.Debug.WriteLine($"Enhanced: Xbox game '{matchingGame.DisplayName}' matched but no valid window handle (PID: {process.Id}). Saving as fallback and continuing search...");

                                // Save this as fallback in case we don't find any process with valid handle
                                if (lastXboxMatch == null)
                                {
                                    lastXboxMatch = matchingGame;
                                    lastXboxProcess = process;
                                }

                                continue; // Keep searching for other processes with same name
                            }

                            // Found a running game that matches our database!
                            var gameInfo = new GameInfo
                            {
                                ProcessName = matchingGame.ProcessName,
                                WindowTitle = matchingGame.DisplayName,
                                ProcessId = process.Id,
                                WindowHandle = windowHandle,
                                ExecutablePath = matchingGame.ExecutablePath
                            };


                            // Validate window handle is still accessible
                            if (windowHandle != IntPtr.Zero && !IsWindow(windowHandle))
                            {
                                System.Diagnostics.Debug.WriteLine($"Enhanced: Warning - Window handle {windowHandle} is no longer valid!");
                            }

                            System.Diagnostics.Debug.WriteLine($"Enhanced: Successfully detected game '{gameInfo.WindowTitle}' with valid window handle: {windowHandle}");
                            return gameInfo;
                        }
                    }
                    catch (Exception)
                    {
                        // Skip processes that can't be accessed - no logging to reduce noise
                        continue;
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }

                // If we found an Xbox game match but none had valid window handles, return the fallback
                // This handles cases where all processes for a Game Pass game lack proper window handles
                if (lastXboxMatch != null && lastXboxProcess != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Enhanced: No Xbox processes with valid handles found. Returning fallback match for '{lastXboxMatch.DisplayName}' (PID: {lastXboxProcess.Id})");

                    return new GameInfo
                    {
                        ProcessName = lastXboxMatch.ProcessName,
                        WindowTitle = lastXboxMatch.DisplayName,
                        ProcessId = lastXboxProcess.Id,
                        WindowHandle = IntPtr.Zero, // No valid handle available
                        ExecutablePath = lastXboxMatch.ExecutablePath
                    };
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Enhanced: Error in GetRunningGameFromDatabase: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Try to match a running process against Xbox games in the database by executable name.
        /// This fallback is used when exact path matching fails (common with Game Pass due to junctions).
        /// Checks the AlternativeExecutables list which contains all .exe files found during scan (up to 5 levels deep).
        /// This handles edge cases like Expedition 33 where MicrosoftGame.config lists "SandFall.exe"
        /// but the actual running process is "SandFall-WinGDK-Shipping.exe".
        /// Only matches against games already in the database - does NOT add new games.
        /// </summary>
        private DetectedGame? TryMatchXboxGameByExecutableName(string processName, string processExePath)
        {
            try
            {
                // Get the executable filename from the running process
                string processExeName = Path.GetFileNameWithoutExtension(processExePath);

                if (string.IsNullOrEmpty(processExeName))
                    return null;

                // Only check Xbox games in the database
                var xboxGames = _cachedGames.Values.Where(g => g.Source == GameSource.Xbox).ToList();

                if (!xboxGames.Any())
                    return null;

                //System.Diagnostics.Debug.WriteLine($"Enhanced: Xbox fallback - Looking for match for process '{processName}' (exe: '{processExeName}')");

                // Check each Xbox game's alternative executables list
                // This list was populated during scan by enumerating all .exe files up to 5 levels deep
                foreach (var xboxGame in xboxGames)
                {
                    // First try exact path match
                    string dbExeName = Path.GetFileNameWithoutExtension(xboxGame.ExecutablePath);
                    if (string.Equals(dbExeName, processExeName, StringComparison.OrdinalIgnoreCase))
                    {
                        //System.Diagnostics.Debug.WriteLine($"Enhanced: Xbox fallback EXACT MATCH (main exe)! Found '{xboxGame.DisplayName}'");
                        //System.Diagnostics.Debug.WriteLine($"  Process exe: {processExeName}");
                        //System.Diagnostics.Debug.WriteLine($"  DB exe: {dbExeName}");
                        return xboxGame;
                    }

                    // Check alternative executables list
                    if (xboxGame.AlternativeExecutables != null && xboxGame.AlternativeExecutables.Any())
                    {
                        foreach (var altExe in xboxGame.AlternativeExecutables)
                        {
                            if (string.Equals(altExe, processExeName, StringComparison.OrdinalIgnoreCase))
                            {
                                System.Diagnostics.Debug.WriteLine($"Enhanced: Xbox fallback ALTERNATIVE MATCH! Found '{xboxGame.DisplayName}'");
                                //System.Diagnostics.Debug.WriteLine($"  Process exe: {processExeName}");
                                //System.Diagnostics.Debug.WriteLine($"  Matched alternative: {altExe}");
                                //System.Diagnostics.Debug.WriteLine($"  Total alternatives for this game: {xboxGame.AlternativeExecutables.Count}");
                                return xboxGame;
                            }
                        }
                    }
                }

                //System.Diagnostics.Debug.WriteLine($"Enhanced: Xbox fallback - No match found for '{processExeName}'");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Enhanced: Error in Xbox fallback matching: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get a valid window handle for the process, trying multiple methods
        /// </summary>
        private IntPtr GetValidWindowHandle(Process process)
        {
            try
            {
                // Method 1: Try the main window handle first
                var mainHandle = process.MainWindowHandle;
                if (mainHandle != IntPtr.Zero && IsWindow(mainHandle))
                {
                    return mainHandle;
                }
                
                // Method 2: For UWP/Store apps, try to find window by process ID
                var foundHandle = FindMainWindowByProcessId(process.Id);
                if (foundHandle != IntPtr.Zero && IsWindow(foundHandle))
                {
                    return foundHandle;
                }
                
                // Method 3: Wait a moment and try main window handle again (for slow-starting games)
                System.Threading.Thread.Sleep(100);
                process.Refresh();
                mainHandle = process.MainWindowHandle;
                if (mainHandle != IntPtr.Zero && IsWindow(mainHandle))
                {
                    return mainHandle;
                }
                
                return IntPtr.Zero;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }
        
        /// <summary>
        /// Find the main window handle by enumerating windows for a process ID
        /// </summary>
        private IntPtr FindMainWindowByProcessId(int processId)
        {
            IntPtr bestHandle = IntPtr.Zero;
            
            EnumWindows((hWnd, lParam) =>
            {
                GetWindowThreadProcessId(hWnd, out uint windowProcessId);
                if (windowProcessId == processId)
                {
                    // Prefer visible windows
                    if (IsWindowVisible(hWnd))
                    {
                        bestHandle = hWnd;
                        return false; // Stop enumeration
                    }
                    // Keep any window as fallback
                    else if (bestHandle == IntPtr.Zero)
                    {
                        bestHandle = hWnd;
                    }
                }
                return true; // Continue enumeration
            }, IntPtr.Zero);
            
            return bestHandle;
        }
        
        /// <summary>
        /// Comprehensive check if we should scan this process
        /// </summary>
        private bool ShouldScanProcess(Process process)
        {
            try
            {
                // Skip if process has exited
                try
                {
                    if (process.HasExited)
                        return false;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Access denied - skip silently
                    return false;
                }
                    
                // Skip system processes by name
                if (IsSystemProcess(process.ProcessName))
                    return false;
                    
                // Skip current process
                if (process.Id == Environment.ProcessId)
                    return false;
                    
                // Skip processes we can't access (do a quick check)
                try
                {
                    _ = process.ProcessName; // This will throw if we can't access
                }
                catch
                {
                    return false;
                }
                
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// Check if a process is a system process that we should skip
        /// </summary>
        private bool IsSystemProcess(string processName)
        {
            var systemProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // Core Windows processes
                "Idle", "System", "Registry", "smss", "csrss", "wininit", "services", 
                "lsass", "winlogon", "fontdrvhost", "dwm", "svchost", "dllhost",
                "WmiPrvSE", "spoolsv", "SearchIndexer", "taskhost", "explorer",
                "RuntimeBroker", "ApplicationFrameHost", "ShellExperienceHost",
                "StartMenuExperienceHost", "SearchHost", "SecurityHealthSystray",
                "ctfmon", "taskhostw", "winstore.app", "PhoneExperienceHost",
                "sihost", "backgroundTaskHost", "PickerHost", "LockApp",
                "UserOOBEBroker", "SettingSyncHost", "PresentationFontCache",
                "MsMpEng", "NisSrv", "SearchFilterHost", "SearchProtocolHost",
                "audiodg", "conhost", "LogonUI", "userinit", "Secure System",
                "Memory Compression", "nvcontainer", "NVDisplay.Container",
                
                // Additional Windows services and system processes
                "csrss", "lsm", "lsass", "wininit", "winlogon", "services", "spoolsv",
                "msdtc", "Ati2evxx", "Ati2evxx", "CCC", "stacsv", "mdm", "alg",
                "wscntfy", "wuauclt", "cidaemon", "cidaemon", "cftmon", "imapi",
                "dfsr", "msiexec", "notepad", "calc", "mspaint", "write", "wordpad",
                "taskmgr", "perfmon", "dxdiag", "msconfig", "regedit", "cmd",
                "powershell", "PowerShell_ISE", "wt", "WindowsTerminal",
                
                // Security and antivirus
                "MsMpEng", "NisSrv", "SecurityHealthService", "SecurityHealthSystray",
                "WindowsSecurityService", "MpCmdRun", "MpSigStub",
                
                // Windows Update and maintenance
                "TiWorker", "TrustedInstaller", "wuauclt", "UsoClient", "UpdateOrchestrator",
                "SIHClient", "CompatTelRunner", "DismHost",
                
                // Hardware and drivers
                "nvcontainer", "nvdisplay.container", "nvidia web helper", "nvbackend",
                "RtkAudioService", "RtkAudUService64", "igfxpers", "igfxtray",
                "TeamViewer_Service", "TeamViewer", "tv_w32", "tv_x64",
                
                // Common utilities that aren't games
                "notepad++", "chrome", "firefox", "edge", "iexplore", "opera",
                "winrar", "7z", "7zfm", "AcroRd32", "Acrobat", "OUTLOOK", "WINWORD",
                "EXCEL", "POWERPNT", "devenv", "Code", "atom", "sublime_text"
            };
            
            return systemProcesses.Contains(processName);
        }
        
        // Win32 API declarations for window handling
        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);
        
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);
        
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);
        
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        
        private const int SW_RESTORE = 9;
        
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        /// <summary>
        /// Scans all running processes to find any known games, prioritizing foreground game
        /// </summary>
        public async Task<GameInfo?> GetRunningGameAsync()
        {
            if (!_isDatabaseReady)
                return null;

            try
            {
                GameInfo? foregroundGame = null;
                var runningGames = new List<GameInfo>();

                // First try to get foreground game (priority)
                var foregroundProcess = GetForegroundProcess();
                if (foregroundProcess != null)
                {
                    foregroundGame = await GetGameByProcessAsync(foregroundProcess.ProcessName);
                }

                // Scan all running processes for known games
                foreach (var cachedGame in _cachedGames.Values)
                {
                    try
                    {
                        var processes = Process.GetProcessesByName(cachedGame.ProcessName);
                        foreach (var process in processes)
                        {
                            if (!process.HasExited)
                            {
                                var gameInfo = new GameInfo
                                {
                                    ProcessName = cachedGame.ProcessName,
                                    WindowTitle = cachedGame.DisplayName,
                                    ProcessId = process.Id,
                                    WindowHandle = process.MainWindowHandle,
                                    ExecutablePath = cachedGame.ExecutablePath
                                };

                                // If this is the foreground game, prioritize it
                                if (foregroundProcess != null && process.Id == foregroundProcess.Id)
                                {
                                    return gameInfo;
                                }

                                runningGames.Add(gameInfo);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Skip processes that can't be accessed
                        System.Diagnostics.Debug.WriteLine($"Error checking process {cachedGame.ProcessName}: {ex.Message}");
                    }
                }

                // Return foreground game if found, otherwise first running game
                return foregroundGame ?? runningGames.FirstOrDefault();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GetRunningGameAsync: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get game by process name from database (kept for backward compatibility with async method)
        /// </summary>
        private GameInfo? GetGameByProcess(string? processName = null)
        {
            if (!_isDatabaseReady)
                return null;

            try
            {
                // If no process name provided, detect the current foreground process
                if (string.IsNullOrEmpty(processName))
                {
                    var foregroundProcess = GetForegroundProcess();
                    if (foregroundProcess == null)
                        return null;
                    processName = foregroundProcess.ProcessName;
                }

                // Fast cached database lookup by ProcessName (fallback method)
                if (_cachedGames.TryGetValue(processName, out var detectedGame))
                {
                    // Convert DetectedGame to GameInfo for compatibility
                    var process = Process.GetProcessesByName(processName).FirstOrDefault();
                    if (process != null)
                    {
                        try
                        {
                            if (process.HasExited)
                                process = null;
                        }
                        catch (System.ComponentModel.Win32Exception)
                        {
                            // Access denied - skip
                            process = null;
                        }
                    }
                    
                    if (process != null)
                    {
                        return new GameInfo
                        {
                            ProcessName = detectedGame.ProcessName,
                            WindowTitle = detectedGame.DisplayName,
                            ProcessId = process.Id,
                            WindowHandle = process.MainWindowHandle,
                            ExecutablePath = detectedGame.ExecutablePath
                        };
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GetGameByProcess: {ex.Message}");
                return null;
            }
        }

        public async Task<GameInfo?> GetGameByProcessAsync(string? processName = null)
        {
            if (!_isDatabaseReady)
                return null;

            try
            {
                // If no process name provided, detect the current foreground process
                if (string.IsNullOrEmpty(processName))
                {
                    var foregroundProcess = GetForegroundProcess();
                    if (foregroundProcess == null)
                        return null;
                    processName = foregroundProcess.ProcessName;
                }

                // Fast cached database lookup
                if (_cachedGames.TryGetValue(processName, out var detectedGame))
                {
                    // Convert DetectedGame to GameInfo for compatibility
                    var process = Process.GetProcessesByName(processName).FirstOrDefault();
                    if (process != null)
                    {
                        try
                        {
                            if (process.HasExited)
                                process = null;
                        }
                        catch (System.ComponentModel.Win32Exception)
                        {
                            // Access denied - skip
                            process = null;
                        }
                    }
                    
                    if (process != null)
                    {
                        return new GameInfo
                        {
                            ProcessName = detectedGame.ProcessName,
                            WindowTitle = detectedGame.DisplayName,
                            ProcessId = process.Id,
                            WindowHandle = process.MainWindowHandle,
                            ExecutablePath = detectedGame.ExecutablePath
                        };
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GetGameByProcessAsync: {ex.Message}");
                return null;
            }
        }

        private Process? GetForegroundProcess()
        {
            try
            {
                // Use the same Win32 API logic as the original GameDetectionService
                var foregroundWindow = GetForegroundWindow();
                if (foregroundWindow == IntPtr.Zero) return null;

                GetWindowThreadProcessId(foregroundWindow, out uint processId);
                if (processId == 0) return null;

                return Process.GetProcessById((int)processId);
            }
            catch
            {
                return null;
            }
        }


        // Win32 API imports (same as original GameDetectionService)
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetForegroundWindow();


        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, [System.Runtime.InteropServices.Out] char[] lpString, int nMaxCount);

        private string GetWindowTitle(IntPtr windowHandle)
        {
            try
            {
                const int maxLength = 256;
                var titleBuilder = new char[maxLength];
                int length = GetWindowText(windowHandle, titleBuilder, maxLength);
                return new string(titleBuilder, 0, length);
            }
            catch
            {
                return string.Empty;
            }
        }


        public bool SwitchToGame()
        {
            if (_currentGame?.WindowHandle == null || _currentGame.WindowHandle == IntPtr.Zero)
            {
                System.Diagnostics.Debug.WriteLine("Enhanced: SwitchToGame: No current game or window handle");
                return false;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"Enhanced: Attempting to switch to: {_currentGame.WindowTitle} (PID: {_currentGame.ProcessId}, Handle: {_currentGame.WindowHandle})");

                // Check if the window still exists and is valid
                if (!IsWindow(_currentGame.WindowHandle))
                {
                        return false;
                }

                // Check if the process is still running
                try
                {
                    var process = Process.GetProcessById(_currentGame.ProcessId);
                    if (process.HasExited)
                    {
                        return false;
                    }
                }
                catch (ArgumentException)
                {
                    return false;
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Process access denied - assume it's still running
                    return false;
                }

                // Try to restore the window first (works for both minimized and normal windows)
                ShowWindow(_currentGame.WindowHandle, SW_RESTORE);

                // Small delay to let the window restore
                System.Threading.Thread.Sleep(50);

                // Bring the game window to the foreground
                bool success = SetForegroundWindow(_currentGame.WindowHandle);

                if (!success)
                {
                    // Alternative approach: try showing the window again
                    ShowWindow(_currentGame.WindowHandle, SW_RESTORE);
                    success = SetForegroundWindow(_currentGame.WindowHandle);
                }

                return success;
            }
            catch (Exception ex)
            {
                // SwitchToGame failed
                return false;
            }
        }

        /// <summary>
        /// Get all games from the database (public method for UI consumption)
        /// </summary>
        public async Task<IEnumerable<DetectedGame>> GetAllGamesAsync()
        {
            if (!_isDatabaseReady || _gameDatabase == null)
                return Enumerable.Empty<DetectedGame>();

            return await _gameDatabase.GetAllGamesAsync();
        }

        public async Task<int> ForceXboxGameRescanAsync()
        {
            try
            {
                
                // Clear existing Xbox games from database
                var deletedCount = _gameDatabase.ClearXboxGames();
                
                // Clear Xbox games from in-memory cache
                var xboxGamesToRemove = _cachedGames.Where(kvp => kvp.Value.Source == GameSource.Xbox).Select(kvp => kvp.Key).ToList();
                foreach (var gameKey in xboxGamesToRemove)
                {
                    _cachedGames.Remove(gameKey);
                }
                
                // Re-scan Xbox games only
                var xboxProvider = _providers.OfType<XboxGameProvider>().FirstOrDefault();
                if (xboxProvider != null && xboxProvider.IsAvailable)
                {
                    var newXboxGames = await xboxProvider.GetGamesAsync();
                    
                    // Save new Xbox games to database
                    foreach (var game in newXboxGames.Values)
                    {
                        _gameDatabase.SaveGame(game);
                        _cachedGames[game.ProcessName] = game;
                    }
                    
                    return newXboxGames.Count;
                }
                
                return 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during Xbox game re-scan: {ex.Message}");
                return 0;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;

                try
                {
                    _refreshTimer?.Dispose();
                    _detectionTimer?.Dispose();
                    _gameDatabase?.Dispose();
                    _artworkService?.Dispose();

                    foreach (var provider in _providers)
                    {
                        provider.ScanProgressChanged -= OnProviderProgressChanged;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Enhanced: Error during disposal: {ex.Message}");
                }
            }
        }
    }

}