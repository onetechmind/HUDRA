using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace HUDRA.Services
{
    // Make sure GameInfo class is included
    public class GameInfo
    {
        public string ProcessName { get; set; } = string.Empty;
        public string WindowTitle { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public IntPtr WindowHandle { get; set; }
        public string ExecutablePath { get; set; } = string.Empty;
    }

    public class GameDetectionService : IDisposable
    {
        private readonly DispatcherQueue _dispatcher;
        private readonly Timer _detectionTimer;
        private GameInfo? _currentGame;
        private bool _disposed = false;
        private readonly HashSet<string> _knownGameProcesses = new(StringComparer.OrdinalIgnoreCase);
        private DateTime _lastGameLostTime = DateTime.MinValue;
        private GameInfo? _recentlyLostGame;
        private const int GAME_LOSS_GRACE_PERIOD_MS = 8000;
        private readonly GameLearningService _learningService;
        private readonly HashSet<string> _scannedThisSession = new(StringComparer.OrdinalIgnoreCase);
        // Win32 APIs
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, [Out] char[] lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool IsIconic(IntPtr hWnd);


        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        private const int SW_RESTORE = 9;


        private readonly HashSet<string> _definitelyNotGames = new(StringComparer.OrdinalIgnoreCase)
        {
            // Development
            "devenv", "code", "rider", "pycharm", "intellij", "unity", "unrealeditorcmd",
            "3dsmax", "maya", "blender", "git", "docker", "vmware", "virtualbox",
    
            // Browsers
            "chrome", "firefox", "edge", "msedge", "safari", "opera", "brave",
    
            // Communication
            "discord", "slack", "teams", "zoom", "skype", "whatsapp",
    
            // Media/Productivity
            "spotify", "vlc", "photoshop", "word", "excel", "powerpoint", "obs", "chatgpt",
    
            // Gaming platforms/launchers (not games themselves)
            "steam", "steamwebhelper", "steamservice",
            "epicgameslauncher", "epicwebhelper",
            "battle.net", "battlenet",
            "origin", "originwebhelperservice",
            "gog galaxy", "gog",
            "xbox", "xboxapp", "xbox app", "xboxgamebar", "gamebar", "gamebarftserver", "gamingservicesui",
            "microsoft store", "winstore.app", "ms-windows-store",

            // Steam Tools (not games)
            "lossless scaling", "losslessscaling",
    
            // UWP App Containers (these host other apps)
            "applicationframehost", "wwahostnowindow", "runtimebroker",
    
            // System
            "explorer", "dwm", "taskmgr", "svchost", "hudra"
        };

        public event EventHandler<GameInfo?>? GameDetected;
        public event EventHandler? GameStopped;
        public GameInfo? CurrentGame => _currentGame;

        public GameDetectionService(DispatcherQueue dispatcher)
        {
            _dispatcher = dispatcher;
            _learningService = new GameLearningService();

            _detectionTimer = new Timer(DetectGamesCallback, null,
                TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
            System.Diagnostics.Debug.WriteLine($"Game learning initialized - Known games: {_learningService.InclusionListCount}, Known non-games: {_learningService.ExclusionListCount}");
        }
        private void DetectGamesCallback(object? state)
        {
            if (_disposed) return;

            try
            {
                var detectedGame = DetectActiveGame();
                bool shouldUpdate = false;

                if (_currentGame == null && detectedGame != null)
                {
                    // New game detected
                    shouldUpdate = true;
                    _recentlyLostGame = null; // Clear any recently lost game
                    System.Diagnostics.Debug.WriteLine($"New game detected: {detectedGame.WindowTitle}");
                }
                else if (_currentGame != null && detectedGame == null)
                {
                    // Game might be lost - but check if process still exists first
                    if (IsGameStillRunning(_currentGame))
                    {
                        // Process still running, probably just not in foreground - start grace period
                        _recentlyLostGame = _currentGame;
                        _lastGameLostTime = DateTime.Now;
                        System.Diagnostics.Debug.WriteLine($"Game lost temporarily: {_currentGame.WindowTitle} - starting grace period");
                        // Don't update UI yet - wait for grace period
                    }
                    else
                    {
                        // Process is dead - immediately remove the game
                        shouldUpdate = true;
                        _recentlyLostGame = null;
                        System.Diagnostics.Debug.WriteLine($"Game process ended: {_currentGame.WindowTitle} - removing immediately");
                    }
                }
                else if (_currentGame != null && detectedGame != null &&
                         _currentGame.ProcessId != detectedGame.ProcessId)
                {
                    // Different game detected
                    shouldUpdate = true;
                    _recentlyLostGame = null;
                    System.Diagnostics.Debug.WriteLine($"Game changed: {_currentGame.WindowTitle} -> {detectedGame.WindowTitle}");
                }
                else if (_currentGame == null && detectedGame == null && _recentlyLostGame != null)
                {
                    // Check if grace period has expired
                    var timeSinceLoss = DateTime.Now - _lastGameLostTime;
                    if (timeSinceLoss.TotalMilliseconds > GAME_LOSS_GRACE_PERIOD_MS)
                    {
                        // Grace period expired - game is really gone
                        shouldUpdate = true;
                        System.Diagnostics.Debug.WriteLine($"Grace period expired - game really stopped: {_recentlyLostGame.WindowTitle}");
                        _recentlyLostGame = null;
                    }
                    else
                    {
                        // Still in grace period - check if the same game came back
                        if (IsGameStillRunning(_recentlyLostGame))
                        {
                            System.Diagnostics.Debug.WriteLine($"Game still running during grace period: {_recentlyLostGame.WindowTitle}");
                            // Keep the game active, don't change UI
                            return;
                        }
                        else
                        {
                            // Process died during grace period - immediately remove
                            shouldUpdate = true;
                            System.Diagnostics.Debug.WriteLine($"Game process died during grace period: {_recentlyLostGame.WindowTitle}");
                            _recentlyLostGame = null;
                        }
                    }
                }
                else if (detectedGame != null && _recentlyLostGame != null &&
                         detectedGame.ProcessId == _recentlyLostGame.ProcessId)
                {
                    // Same game came back during grace period!
                    System.Diagnostics.Debug.WriteLine($"Game returned during grace period: {detectedGame.WindowTitle}");
                    _currentGame = detectedGame;
                    _recentlyLostGame = null;
                    return; // No UI update needed
                }

                if (shouldUpdate)
                {
                    _currentGame = detectedGame;

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
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Game detection error: {ex.Message}");

                // If we hit an exception and have a recently lost game, clear it to prevent stuck state
                if (_recentlyLostGame != null)
                {
                    System.Diagnostics.Debug.WriteLine("Clearing recently lost game due to exception");
                    _recentlyLostGame = null;
                    _currentGame = null;

                    _dispatcher.TryEnqueue(() =>
                    {
                        GameStopped?.Invoke(this, EventArgs.Empty);
                    });
                }
            }
        }

        private GameInfo? DetectActiveGame()
        {
            try
            {
                // First, check if our current game is still running
                if (_currentGame != null)
                {
                    try
                    {
                        var existingProcess = Process.GetProcessById(_currentGame.ProcessId);
                        if (existingProcess != null && !existingProcess.HasExited)
                        {
                            return _currentGame;
                        }
                    }
                    catch (ArgumentException)
                    {
                        // Process no longer exists
                    }
                }

                // Look for a new foreground game
                var foregroundWindow = GetForegroundWindow();
                if (foregroundWindow == IntPtr.Zero) return null;

                GetWindowThreadProcessId(foregroundWindow, out uint processId);
                if (processId == 0) return null;

                var process = Process.GetProcessById((int)processId);
                if (process == null) return null;

                var processName = process.ProcessName;

                // Get window title and executable path once (we'll reuse these)
                var windowTitle = GetWindowTitle(foregroundWindow);
                string executablePath = string.Empty;
                try
                {
                    executablePath = process.MainModule?.FileName ?? string.Empty;
                }
                catch { }

                // SMART LEARNING CHECKS:

                // 1. Check inclusion list first (known games) - IMMEDIATE RETURN
                if (_learningService.IsKnownGame(processName))
                {
                    System.Diagnostics.Debug.WriteLine($"Found known game from inclusion list: {processName}");

                    return new GameInfo
                    {
                        ProcessName = processName,
                        WindowTitle = windowTitle,
                        ProcessId = process.Id,
                        WindowHandle = foregroundWindow,
                        ExecutablePath = executablePath
                    };
                }

                // 2. Check exclusion list (known non-games) - SKIP IF ALREADY SCANNED THIS SESSION
                if (_learningService.IsKnownNonGame(processName))
                {
                    if (!_scannedThisSession.Contains(processName))
                    {
                        _scannedThisSession.Add(processName);
                        System.Diagnostics.Debug.WriteLine($"Skipping known non-game (first time this session): {processName}");
                    }
                    return null;
                }

                // 3. NEW PROCESS - Run full detection and learn from result
                if (string.IsNullOrWhiteSpace(windowTitle))
                {
                    // Learn that processes with no window title are not games
                    _learningService.LearnNonGame(processName);
                    return null;
                }

                // Run enhanced game detection
                bool isGame = IsLikelyGame(process, windowTitle);

                // LEARN FROM THE RESULT:
                if (isGame)
                {
                    _learningService.LearnGame(processName);
                    System.Diagnostics.Debug.WriteLine($"Detected and learned new game: {processName}");

                    return new GameInfo
                    {
                        ProcessName = processName,
                        WindowTitle = windowTitle,
                        ProcessId = process.Id,
                        WindowHandle = foregroundWindow,
                        ExecutablePath = executablePath
                    };
                }
                else
                {
                    _learningService.LearnNonGame(processName);
                    System.Diagnostics.Debug.WriteLine($"Detected and learned non-game: {processName}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error detecting game: {ex.Message}");
                return null;
            }
        }



        private bool IsLikelyGame(Process process, string windowTitle)
        {
            // Early exclusion: Is this definitely NOT a game?
            if (_definitelyNotGames.Contains(process.ProcessName))
            {
                System.Diagnostics.Debug.WriteLine($"Excluded by process name: {process.ProcessName}");
                return false;
            }

            // Special check for Xbox app vs Xbox games
            if (IsXboxAppNotGame(process, windowTitle))
            {
                System.Diagnostics.Debug.WriteLine($"Excluded Xbox app: {windowTitle}");
                return false;
            }

            // ONLY METHOD: Check if it's in a game directory
            if (IsInGameDirectory(process))
            {
                System.Diagnostics.Debug.WriteLine($"Game detected by directory: {process.ProcessName}");
                return true;
            }

            System.Diagnostics.Debug.WriteLine($"Not detected as game: {process.ProcessName} (not in game directory)");
            return false;
        }


        private bool IsInGameDirectory(Process process)
        {
            try
            {
                string? executablePath = process.MainModule?.FileName;
                if (string.IsNullOrEmpty(executablePath)) return false;

                var gameDirectories = new[]
                {
                    // Steam
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
                    
                    // Xbox/Microsoft Store games
                    @"\XboxGames\",
                    @"\Xbox Games\",
                    @"\WindowsApps\",
                    @"\Microsoft.Gaming\",
                    @"\Program Files\WindowsApps\",
                    @"\Program Files (x86)\WindowsApps\",
                    
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

                bool inGameDirectory = gameDirectories.Any(dir =>
                    executablePath.Contains(dir, StringComparison.OrdinalIgnoreCase));

                // Additional Xbox-specific detection
                if (!inGameDirectory && IsXboxGame(executablePath))
                {
                    return true;
                }

                return inGameDirectory;
            }
            catch
            {
                return false;
            }
        }

        private bool IsXboxGame(string executablePath)
        {
            try
            {
                // Xbox games often have these characteristics:
                // 1. Located in XboxGames folder (any drive)
                // 2. Have complex publisher/package folder structure
                // 3. Often have "Content" folders
                // 4. May be in WindowsApps (UWP packages)

                var xboxIndicators = new[]
                {
            @"\XboxGames\",
            @"\Xbox Games\",
            @"\WindowsApps\",
            @"\Content\",
            @"\Microsoft.Gaming",
            @"_8wekyb3d8bbwe\", // Common Xbox package suffix
            @"_xbox_",
            @"_ms-"
        };

                return xboxIndicators.Any(indicator =>
                    executablePath.Contains(indicator, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

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
            if (_currentGame?.WindowHandle == null)
            {
                System.Diagnostics.Debug.WriteLine("SwitchToGame: No current game or window handle");
                return false;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"Attempting to switch to: {_currentGame.WindowTitle} (PID: {_currentGame.ProcessId})");

                // Check if the window still exists and is valid
                if (!IsWindow(_currentGame.WindowHandle))
                {
                    System.Diagnostics.Debug.WriteLine("SwitchToGame: Window no longer exists");
                    _currentGame = null;
                    _dispatcher.TryEnqueue(() => GameStopped?.Invoke(this, EventArgs.Empty));
                    return false;
                }

                // Check if the process is still running
                try
                {
                    var process = Process.GetProcessById(_currentGame.ProcessId);
                    if (process.HasExited)
                    {
                        System.Diagnostics.Debug.WriteLine("SwitchToGame: Process has exited");
                        _currentGame = null;
                        _dispatcher.TryEnqueue(() => GameStopped?.Invoke(this, EventArgs.Empty));
                        return false;
                    }
                }
                catch (ArgumentException)
                {
                    System.Diagnostics.Debug.WriteLine("SwitchToGame: Process no longer exists");
                    _currentGame = null;
                    _dispatcher.TryEnqueue(() => GameStopped?.Invoke(this, EventArgs.Empty));
                    return false;
                }

                // Try to restore the window first (works for both minimized and normal windows)
                System.Diagnostics.Debug.WriteLine("SwitchToGame: Attempting to restore/show window");
                ShowWindow(_currentGame.WindowHandle, SW_RESTORE);

                // Small delay to let the window restore
                System.Threading.Thread.Sleep(50);

                // Bring the game window to the foreground
                System.Diagnostics.Debug.WriteLine("SwitchToGame: Setting foreground window");
                bool success = SetForegroundWindow(_currentGame.WindowHandle);

                if (success)
                {
                    System.Diagnostics.Debug.WriteLine("SwitchToGame: Successfully switched to game");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("SwitchToGame: SetForegroundWindow failed, trying alternative approach");

                    // Alternative approach: try showing the window again
                    ShowWindow(_currentGame.WindowHandle, 9); // SW_RESTORE = 9
                    success = SetForegroundWindow(_currentGame.WindowHandle);

                    System.Diagnostics.Debug.WriteLine($"SwitchToGame: Alternative approach result: {success}");
                }

                return success;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SwitchToGame exception: {ex.GetType().Name}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        private bool IsXboxAppNotGame(Process process, string windowTitle)
        {
            // If it's ApplicationFrameHost with "Xbox" title, it's the Xbox app, not a game
            if (process.ProcessName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
            {
                var suspiciousTitles = new[]
                {
            "Xbox", "Xbox Console Companion", "Xbox Game Bar", "Xbox Identity Provider",
            "Microsoft Store", "Store", "Settings", "Calculator", "Mail", "Calendar"
        };

                if (suspiciousTitles.Any(title => windowTitle.Equals(title, StringComparison.OrdinalIgnoreCase)))
                {
                    return true; // This is an app, not a game
                }
            }

            return false;
        }

        private bool IsGameStillRunning(GameInfo gameInfo)
        {
            try
            {
                var process = Process.GetProcessById(gameInfo.ProcessId);
                return process != null && !process.HasExited;
            }
            catch (ArgumentException)
            {
                // Process no longer exists (most common case)
                System.Diagnostics.Debug.WriteLine($"Process {gameInfo.ProcessId} ({gameInfo.ProcessName}) no longer exists");
                return false;
            }
            catch (InvalidOperationException)
            {
                // Process has exited
                System.Diagnostics.Debug.WriteLine($"Process {gameInfo.ProcessId} ({gameInfo.ProcessName}) has exited");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking if game {gameInfo.ProcessName} is running: {ex.Message}");
                return false; // Assume dead on any other error
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _detectionTimer?.Dispose();
            }
        }
    }
}