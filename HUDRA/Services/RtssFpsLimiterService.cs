using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HUDRA.Services
{
    public class RtssDetectionResult
    {
        public bool IsInstalled { get; set; }
        public string InstallPath { get; set; } = "";
        public string Version { get; set; } = "";
        public bool IsRunning { get; set; }
    }

    public class RtssFpsLimiterService
    {
        private RtssDetectionResult? _cachedDetection = null;
        private int _currentFpsLimit = 0;

        public async Task<RtssDetectionResult> DetectRtssInstallationAsync(bool forceRefresh = false)
        {
            if (_cachedDetection != null && !forceRefresh)
            {
                // Always refresh the running status even with cached data
                _cachedDetection.IsRunning = IsRtssProcessRunning();
                return _cachedDetection;
            }

            return await Task.Run(() =>
            {
                var result = new RtssDetectionResult();

                try
                {
                    // Method 1: Check registry for RTSS installation
                    string? installPath = GetRtssInstallPathFromRegistry();

                    if (string.IsNullOrEmpty(installPath))
                    {
                        // Method 2: Check common installation paths
                        installPath = CheckCommonRtssPaths();
                    }

                    if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
                    {
                        // Verify RTSS executable exists
                        string rtssExePath = Path.Combine(installPath, "RTSS.exe");
                        if (File.Exists(rtssExePath))
                        {
                            result.IsInstalled = true;
                            result.InstallPath = installPath;
                            result.Version = GetRtssVersion(rtssExePath);
                            result.IsRunning = IsRtssProcessRunning();

                            System.Diagnostics.Debug.WriteLine($"RTSS detected: {installPath}, Version: {result.Version}, Running: {result.IsRunning}");
                        }
                    }

                    _cachedDetection = result;
                    return result;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"RTSS detection failed: {ex.Message}");
                    _cachedDetection = result;
                    return result;
                }
            });
        }

        public async Task<bool> SetGlobalFpsLimitAsync(int fps)
        {
            if (fps <= 0 || fps > 1000)
                return false;

            var detection = await DetectRtssInstallationAsync();
            if (!detection.IsInstalled)
                return false;

            // If RTSS is installed but not running, try to start it
            if (!detection.IsRunning)
            {
                System.Diagnostics.Debug.WriteLine("RTSS is installed but not running - attempting to start it");
                var startSuccess = await TryStartRtssAsync(detection.InstallPath);
                if (!startSuccess)
                {
                    System.Diagnostics.Debug.WriteLine("Failed to start RTSS automatically");
                    return false;
                }

                // Wait a moment for RTSS to initialize
                await Task.Delay(2000);

                // Update detection status
                _cachedDetection = null; // Force refresh
                detection = await DetectRtssInstallationAsync();
            }

            // Use RTSSHooks64.dll method only
            bool success = await SetTargetFpsViaRtssHooks(fps);
            if (success)
            {
                _currentFpsLimit = fps;
                System.Diagnostics.Debug.WriteLine($"✅ Set global FPS limit to {fps} via RTSSHooks64");
                return true;
            }

            System.Diagnostics.Debug.WriteLine($"❌ Failed to set FPS limit via RTSSHooks64: {fps}");
            return false;
        }

        public async Task<bool> DisableGlobalFpsLimitAsync()
        {
            var detection = await DetectRtssInstallationAsync();
            if (!detection.IsInstalled)
                return false;

            // Use RTSSHooks64.dll method only - set to 0 to disable
            bool success = await SetTargetFpsViaRtssHooks(0);
            if (success)
            {
                _currentFpsLimit = 0;
                System.Diagnostics.Debug.WriteLine("✅ Disabled global FPS limit via RTSSHooks64");
                return true;
            }

            System.Diagnostics.Debug.WriteLine("❌ Failed to disable FPS limit via RTSSHooks64");
            return false;
        }

        public int GetCurrentFpsLimit()
        {
            return _currentFpsLimit;
        }

        public List<int> CalculateFpsOptionsFromRefreshRate(int refreshRate)
        {
            var options = new List<int>();

            // Add "Unlimited" option as 0 - this will be first in the list
            options.Add(0);

            var quarter = (int)(refreshRate * 0.25);
            var half = (int)(refreshRate * 0.5);
            var threeQuarter = (int)(refreshRate * 0.75);
            var full = refreshRate;

            var magicNumber = 45;

            var fpsOptions = new[] { quarter, half, threeQuarter, full, magicNumber };
            var validFpsOptions = fpsOptions.Where(x => x > 0).Distinct().OrderBy(x => x);
            
            options.AddRange(validFpsOptions);
            return options;
        }

        #region Private Methods

        private string? GetRtssInstallPathFromRegistry()
        {
            try
            {
                // Check RTSS registry key
                using var rtssKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Unwinder\RTSS", false);
                if (rtssKey != null)
                {
                    var installPath = rtssKey.GetValue("InstallPath") as string;
                    if (!string.IsNullOrEmpty(installPath))
                        return installPath;
                }

                // Check MSI Afterburner registry (often includes RTSS)
                using var afterburnerKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Unwinder\MSI Afterburner", false);
                if (afterburnerKey != null)
                {
                    var installPath = afterburnerKey.GetValue("InstallPath") as string;
                    if (!string.IsNullOrEmpty(installPath))
                    {
                        // Check if RTSS is bundled with Afterburner
                        string rtssPath = Path.Combine(installPath, "RTSS.exe");
                        if (File.Exists(rtssPath))
                            return installPath;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Registry RTSS detection failed: {ex.Message}");
            }

            return null;
        }

        private string? CheckCommonRtssPaths()
        {
            var commonPaths = new[]
            {
                @"C:\Program Files (x86)\RivaTuner Statistics Server",
                @"C:\Program Files\RivaTuner Statistics Server",
                @"C:\Program Files (x86)\MSI Afterburner",
                @"C:\Program Files\MSI Afterburner",
                Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\RivaTuner Statistics Server"),
                Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\RivaTuner Statistics Server"),
                Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\MSI Afterburner"),
                Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\MSI Afterburner")
            };

            foreach (string path in commonPaths)
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        string rtssExe = Path.Combine(path, "RTSS.exe");
                        if (File.Exists(rtssExe))
                            return path;
                    }
                }
                catch
                {
                    // Continue checking other paths
                }
            }

            return null;
        }

        private string GetRtssVersion(string rtssExePath)
        {
            try
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(rtssExePath);
                return versionInfo.FileVersion ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        private bool IsRtssProcessRunning()
        {
            try
            {
                var processes = Process.GetProcessesByName("RTSS");
                if (processes.Length > 0)
                {
                    foreach (var proc in processes)
                        proc.Dispose();
                    return true;
                }

                // Also check for RTSSHooksLoader64
                processes = Process.GetProcessesByName("RTSSHooksLoader64");
                if (processes.Length > 0)
                {
                    foreach (var proc in processes)
                        proc.Dispose();
                    return true;
                }
            }
            catch
            {
                // Process access might fail, assume not running
            }

            return false;
        }

        private async Task<bool> TryStartRtssAsync(string rtssInstallPath)
        {
            try
            {
                string rtssExePath = Path.Combine(rtssInstallPath, "RTSS.exe");
                if (!File.Exists(rtssExePath))
                {
                    System.Diagnostics.Debug.WriteLine($"RTSS executable not found at: {rtssExePath}");
                    return false;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = rtssExePath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                System.Diagnostics.Debug.WriteLine($"Starting RTSS: {rtssExePath}");
                using var process = Process.Start(startInfo);

                if (process != null)
                {
                    // Don't wait for exit since RTSS runs as a background service
                    System.Diagnostics.Debug.WriteLine("RTSS process started successfully");
                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Failed to start RTSS process");
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception starting RTSS: {ex.Message}");
                return false;
            }
        }

        // RTSSHooks64.dll P/Invoke declarations based on Handheld Companion
        [DllImport("RTSSHooks64.dll", CharSet = CharSet.Ansi)]
        private static extern void LoadProfile(string profile = "");

        [DllImport("RTSSHooks64.dll", CharSet = CharSet.Ansi)]
        private static extern void SaveProfile(string profile = "");

        [DllImport("RTSSHooks64.dll", CharSet = CharSet.Ansi)]
        private static extern bool GetProfileProperty(string propertyName, IntPtr value, uint size);

        [DllImport("RTSSHooks64.dll", CharSet = CharSet.Ansi)]
        private static extern bool SetProfileProperty(string propertyName, IntPtr value, uint size);

        [DllImport("RTSSHooks64.dll", CharSet = CharSet.Ansi)]
        private static extern void UpdateProfiles();

        private bool ProfileLoaded = false;

        private async Task<bool> SetTargetFpsViaRtssHooks(int fps)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    // Verify RTSS is running before attempting RTSSHooks64 access
                    var rtssProcesses = Process.GetProcessesByName("RTSS");
                    if (rtssProcesses.Length == 0)
                    {
                        System.Diagnostics.Debug.WriteLine("RTSS process not found - cannot use RTSSHooks64");
                        return false;
                    }

                    System.Diagnostics.Debug.WriteLine($"Using RTSSHooks64.dll to set FPS limit to {fps}");

                    // Ensure Global profile is loaded
                    LoadProfile();
                    ProfileLoaded = true;

                    // Set Framerate Limit using RTSSHooks64
                    if (SetProfileProperty("FramerateLimit", fps))
                    {
                        //Save and reload profile
                        SaveProfile();
                        UpdateProfiles();

                        // Verify the setting was actually applied
                        await Task.Delay(1000); // Small delay to let RTSS process the change
                        int actualLimit = GetCurrentRtssFpsLimit();

                        if (actualLimit == fps)
                        {
                            System.Diagnostics.Debug.WriteLine($"✅ Successfully set and verified FPS limit to {fps} via RTSSHooks64");
                            return true;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"⚠️ FPS limit set but verification failed: expected {fps}, got {actualLimit}");
                            return false; // Consider this a failure so we retry
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ SetProfileProperty failed for FPS limit {fps}");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Exception using RTSSHooks64: {ex.Message}");
                    return false;
                }
            });
        }

        // Helper method to set profile property with proper marshaling
        private bool SetProfileProperty<T>(string propertyName, T value)
        {
            var bytes = new byte[Marshal.SizeOf<T>()];
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                Marshal.StructureToPtr(value, handle.AddrOfPinnedObject(), false);
                return SetProfileProperty(propertyName, handle.AddrOfPinnedObject(), (uint)bytes.Length);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ SetProfileProperty marshaling failed: {ex.Message}");
                return false;
            }
            finally
            {
                handle.Free();
            }
        }

        // Helper method to get profile property with proper marshaling
        private bool GetProfileProperty<T>(string propertyName, out T value)
        {
            var bytes = new byte[Marshal.SizeOf<T>()];
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            value = default;
            try
            {
                if (!GetProfileProperty(propertyName, handle.AddrOfPinnedObject(), (uint)bytes.Length))
                    return false;

                value = Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ GetProfileProperty marshaling failed: {ex.Message}");
                return false;
            }
            finally
            {
                handle.Free();
            }
        }

        // Method to verify if FPS limit was actually applied
        private int GetCurrentRtssFpsLimit()
        {
            try
            {
                // Ensure Global profile is loaded
                if (!ProfileLoaded)
                {
                    LoadProfile();
                    ProfileLoaded = true;
                }

                if (GetProfileProperty("FramerateLimit", out int fpsLimit))
                {
                    return fpsLimit;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to get current RTSS FPS limit: {ex.Message}");
            }

            return 0;
        }

        public async Task<bool> StartRtssIfNeededAsync()
        {
            var detection = await DetectRtssInstallationAsync();
            if (!detection.IsInstalled)
                return false;

            if (!detection.IsRunning)
            {
                System.Diagnostics.Debug.WriteLine("Starting RTSS on HUDRA launch");
                var started = await TryStartRtssAsync(detection.InstallPath);
                if (started)
                {
                    // Wait for RTSS to initialize
                    await Task.Delay(2000);
                }
                return started;
            }
            else
            {
                // RTSS already running
                System.Diagnostics.Debug.WriteLine("RTSS already running");
            }

            return true; // Already running
        }

        #endregion
    }
}