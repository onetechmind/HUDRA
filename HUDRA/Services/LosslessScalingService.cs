using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using HUDRA.Models;

namespace HUDRA.Services
{
    public class LosslessScalingService : IDisposable
    {
        public event EventHandler<bool>? LosslessScalingStatusChanged;

        private const string SETTINGS_PATH = @"%LOCALAPPDATA%\Lossless Scaling\Settings.xml";
        private const string DEFAULT_HOTKEY = "S";
        private const string DEFAULT_MODIFIERS = "Alt Control";
        private const string PROCESS_NAME = "LosslessScaling";

        // Static caching for installation status (shared across instances)
        private static bool? _cachedInstallationStatus = null;
        private static LosslessScalingDetectionResult? _sharedCachedDetection = null;
        private static readonly object _cacheLock = new object();

        // Instance caching
        private LosslessScalingDetectionResult? _cachedDetection = null;

        private readonly Timer _detectionTimer;
        private bool _isLosslessScalingRunning = false;
        private bool _disposed = false;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const uint KEYEVENTF_KEYUP = 0x0002;

        private readonly Dictionary<string, byte> _keyMap = new()
        {
            { "A", 0x41 }, { "B", 0x42 }, { "C", 0x43 }, { "D", 0x44 }, { "E", 0x45 },
            { "F", 0x46 }, { "G", 0x47 }, { "H", 0x48 }, { "I", 0x49 }, { "J", 0x4A },
            { "K", 0x4B }, { "L", 0x4C }, { "M", 0x4D }, { "N", 0x4E }, { "O", 0x4F },
            { "P", 0x50 }, { "Q", 0x51 }, { "R", 0x52 }, { "S", 0x53 }, { "T", 0x54 },
            { "U", 0x55 }, { "V", 0x56 }, { "W", 0x57 }, { "X", 0x58 }, { "Y", 0x59 },
            { "Z", 0x5A },
            { "0", 0x30 }, { "1", 0x31 }, { "2", 0x32 }, { "3", 0x33 }, { "4", 0x34 },
            { "5", 0x35 }, { "6", 0x36 }, { "7", 0x37 }, { "8", 0x38 }, { "9", 0x39 },
            { "Alt", 0x12 }, { "Control", 0x11 }, { "Shift", 0x10 },
            { "F1", 0x70 }, { "F2", 0x71 }, { "F3", 0x72 }, { "F4", 0x73 },
            { "F5", 0x74 }, { "F6", 0x75 }, { "F7", 0x76 }, { "F8", 0x77 },
            { "F9", 0x78 }, { "F10", 0x79 }, { "F11", 0x7A }, { "F12", 0x7B }
        };

        public LosslessScalingService()
        {
            _detectionTimer = new Timer(DetectionCallback, null,
                TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
        }

        public bool IsLosslessScalingRunning()
        {
            try
            {
                var processes = Process.GetProcessesByName(PROCESS_NAME);
                return processes.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public (string hotkey, string modifiers) ParseHotkeyFromSettings()
        {
            try
            {
                string settingsPath = Environment.ExpandEnvironmentVariables(SETTINGS_PATH);
                if (!File.Exists(settingsPath))
                    return (DEFAULT_HOTKEY, DEFAULT_MODIFIERS);

                var doc = new XmlDocument();
                doc.Load(settingsPath);

                string hotkey = doc.SelectSingleNode("//Hotkey")?.InnerText ?? DEFAULT_HOTKEY;
                string modifiers = doc.SelectSingleNode("//HotkeyModifierKeys")?.InnerText ?? DEFAULT_MODIFIERS;

                return (hotkey, modifiers);
            }
            catch
            {
                return (DEFAULT_HOTKEY, DEFAULT_MODIFIERS);
            }
        }

        public async Task<bool> ExecuteHotkeyAsync(string hotkey, string modifiers)
        {
            try
            {
                var modifierKeys = ParseModifiers(modifiers);
                var mainKey = _keyMap.TryGetValue(hotkey.ToUpper(), out byte key) ? key : (byte)0x53;

                foreach (var mod in modifierKeys)
                    keybd_event(mod, 0, 0, UIntPtr.Zero);

                keybd_event(mainKey, 0, 0, UIntPtr.Zero);

                await Task.Delay(50);

                keybd_event(mainKey, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

                for (int i = modifierKeys.Count - 1; i >= 0; i--)
                    keybd_event(modifierKeys[i], 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private List<byte> ParseModifiers(string modifiers)
        {
            var keys = new List<byte>();
            if (string.IsNullOrEmpty(modifiers))
                return keys;

            var parts = modifiers.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (_keyMap.TryGetValue(part, out byte key))
                    keys.Add(key);
            }

            return keys;
        }

        private void DetectionCallback(object? state)
        {
            if (_disposed)
                return;

            try
            {
                bool isRunning = IsLosslessScalingRunning();
                if (isRunning != _isLosslessScalingRunning)
                {
                    _isLosslessScalingRunning = isRunning;
                    LosslessScalingStatusChanged?.Invoke(this, isRunning);
                }
            }
            catch
            {
                // Ignore detection errors
            }
        }

        #region Installation Detection

        /// <summary>
        /// Detects Lossless Scaling installation with caching support.
        /// </summary>
        public async Task<LosslessScalingDetectionResult> DetectInstallationAsync(bool forceRefresh = false)
        {
            if (_cachedDetection != null && !forceRefresh)
            {
                // Always refresh running status even with cached data
                _cachedDetection.IsRunning = IsLosslessScalingRunning();
                return _cachedDetection;
            }

            return await Task.Run(() =>
            {
                var result = new LosslessScalingDetectionResult();

                try
                {
                    // Method 1: Check Steam library paths (most common installation)
                    string? installPath = FindInSteamLibraries();

                    // Method 2: Check common installation paths
                    if (string.IsNullOrEmpty(installPath))
                    {
                        installPath = CheckCommonPaths();
                    }

                    // Method 3: Get path from running process
                    if (string.IsNullOrEmpty(installPath))
                    {
                        installPath = GetPathFromRunningProcess();
                    }

                    if (!string.IsNullOrEmpty(installPath) && File.Exists(installPath))
                    {
                        result.IsInstalled = true;
                        result.InstallPath = installPath;
                        result.Version = GetVersion(installPath);
                        result.IsRunning = IsLosslessScalingRunning();
                        result.HasSettingsFile = CheckSettingsFileExists();

                        System.Diagnostics.Debug.WriteLine($"LS detected: {installPath}, Version: {result.Version}, Running: {result.IsRunning}");
                    }

                    _cachedDetection = result;

                    // Update static cache
                    lock (_cacheLock)
                    {
                        _sharedCachedDetection = result;
                        _cachedInstallationStatus = result.IsInstalled;
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"LS detection failed: {ex.Message}");
                    _cachedDetection = result;
                    return result;
                }
            });
        }

        /// <summary>
        /// Quick refresh that only checks process status, not full detection.
        /// </summary>
        public async Task<LosslessScalingDetectionResult> SmartRefreshStatusAsync()
        {
            if (_cachedDetection == null)
            {
                return await DetectInstallationAsync(forceRefresh: true);
            }

            bool isCurrentlyRunning = IsLosslessScalingRunning();

            // If running status hasn't changed, return cached result
            if (_cachedDetection.IsRunning == isCurrentlyRunning)
            {
                return _cachedDetection;
            }

            // Running status changed - validate installation still exists if process stopped
            if (!isCurrentlyRunning && _cachedDetection.IsInstalled)
            {
                if (File.Exists(_cachedDetection.InstallPath))
                {
                    _cachedDetection.IsRunning = false;
                    System.Diagnostics.Debug.WriteLine("LS installation exists but process stopped");
                    return _cachedDetection;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("LS installation no longer exists, forcing full refresh");
                    return await DetectInstallationAsync(forceRefresh: true);
                }
            }

            // Process started when it wasn't running before
            _cachedDetection.IsRunning = isCurrentlyRunning;
            System.Diagnostics.Debug.WriteLine($"LS process status changed to: {isCurrentlyRunning}");
            return _cachedDetection;
        }

        /// <summary>
        /// Static method to preload installation status at app startup.
        /// </summary>
        public static async Task PreloadInstallationStatusAsync()
        {
            if (_cachedInstallationStatus.HasValue)
                return; // Already cached

            try
            {
                System.Diagnostics.Debug.WriteLine("Preloading Lossless Scaling installation status...");

                // Create temporary service instance for detection
                var tempService = new LosslessScalingService();
                var detection = await tempService.DetectInstallationAsync(forceRefresh: true);
                tempService.Dispose();

                lock (_cacheLock)
                {
                    _cachedInstallationStatus = detection.IsInstalled;
                    _sharedCachedDetection = detection;
                }

                System.Diagnostics.Debug.WriteLine($"LS installation status cached: {_cachedInstallationStatus}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to preload LS installation status: {ex.Message}");
                _cachedInstallationStatus = false; // Default to not installed on error
            }
        }

        /// <summary>
        /// Gets the cached installation status (instant, no async).
        /// </summary>
        public static bool GetCachedInstallationStatus()
        {
            return _cachedInstallationStatus ?? false;
        }

        /// <summary>
        /// Gets the cached detection result if available.
        /// </summary>
        public static LosslessScalingDetectionResult? GetCachedDetectionResult()
        {
            lock (_cacheLock)
            {
                return _sharedCachedDetection;
            }
        }

        /// <summary>
        /// Starts Lossless Scaling if needed (for auto-start feature).
        /// </summary>
        public async Task<bool> StartLosslessScalingIfNeededAsync()
        {
            var detection = await DetectInstallationAsync();
            if (!detection.IsInstalled)
                return false;

            if (!detection.IsRunning)
            {
                System.Diagnostics.Debug.WriteLine("Starting Lossless Scaling on HUDRA launch");

                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = detection.InstallPath,
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(detection.InstallPath)
                    };

                    var process = Process.Start(startInfo);
                    if (process == null)
                        return false;

                    // Wait for LS to initialize
                    await Task.Delay(2000);

                    // Update cached status
                    _cachedDetection = null; // Force refresh
                    var refreshed = await DetectInstallationAsync();

                    return refreshed.IsRunning;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to start LS: {ex.Message}");
                    return false;
                }
            }

            System.Diagnostics.Debug.WriteLine("Lossless Scaling already running");
            return true;
        }

        #endregion

        #region Detection Helpers

        private string? FindInSteamLibraries()
        {
            var steamPaths = new List<string>
            {
                @"C:\Program Files (x86)\Steam",
                @"C:\Program Files\Steam",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            };

            // Parse libraryfolders.vdf for additional library locations
            try
            {
                string libraryFoldersPath = @"C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf";
                if (File.Exists(libraryFoldersPath))
                {
                    var content = File.ReadAllText(libraryFoldersPath);
                    var matches = Regex.Matches(content, @"""path""\s+""([^""]+)""");
                    foreach (Match match in matches)
                    {
                        steamPaths.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
                    }
                }
            }
            catch
            {
                // Continue with default paths
            }

            foreach (var steamPath in steamPaths.Distinct())
            {
                string lsPath = Path.Combine(steamPath, "steamapps", "common", "Lossless Scaling", "LosslessScaling.exe");
                if (File.Exists(lsPath))
                {
                    return lsPath;
                }
            }

            return null;
        }

        private string? CheckCommonPaths()
        {
            var commonPaths = new[]
            {
                @"C:\Program Files\Lossless Scaling\LosslessScaling.exe",
                @"C:\Program Files (x86)\Lossless Scaling\LosslessScaling.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "Lossless Scaling", "LosslessScaling.exe")
            };

            return commonPaths.FirstOrDefault(File.Exists);
        }

        private string? GetPathFromRunningProcess()
        {
            try
            {
                var processes = Process.GetProcessesByName(PROCESS_NAME);
                if (processes.Length > 0)
                {
                    string? path = processes[0].MainModule?.FileName;
                    foreach (var p in processes)
                        p.Dispose();
                    return path;
                }
            }
            catch
            {
                // Process access may fail without admin
            }
            return null;
        }

        private string GetVersion(string exePath)
        {
            try
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(exePath);
                return versionInfo.FileVersion ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        private bool CheckSettingsFileExists()
        {
            string settingsPath = Environment.ExpandEnvironmentVariables(SETTINGS_PATH);
            return File.Exists(settingsPath);
        }

        #endregion

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _detectionTimer?.Dispose();
        }
    }
}