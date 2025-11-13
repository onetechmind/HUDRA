using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;
using HUDRA.Services.AMD;

namespace HUDRA.Services
{
    /// <summary>
    /// Service for controlling AMD GPU features using ADLX SDK.
    /// Falls back to registry-based control if ADLX is not available.
    /// </summary>
    public class AmdAdlxService : IDisposable
    {
        private bool _disposed = false;
        private bool _isAmdGpuPresent = false;
        private bool _isAdlxAvailable = false;
        private IntPtr _adlxContext = IntPtr.Zero;

        // ADLX DLL name (should be in System32 or driver folder)
        private const string ADLX_DLL = "amdxx64.dll"; // AMD Display Library

        // Registry paths for AMD driver settings
        private const string AMD_REGISTRY_PATH = @"Software\AMD\CN";
        private const string AMD_REGISTRY_KEY_RSR_ENABLE = "RSR_Enable";
        private const string AMD_REGISTRY_KEY_RSR_SHARPNESS = "RSR_Sharpness";

        public AmdAdlxService()
        {
            Initialize();
        }

        private void Initialize()
        {
            try
            {
                // Check if AMD GPU is present
                _isAmdGpuPresent = DetectAmdGpu();

                if (_isAmdGpuPresent)
                {
                    System.Diagnostics.Debug.WriteLine("AMD GPU detected");

                    // Try to initialize ADLX
                    _isAdlxAvailable = TryInitializeAdlx();

                    if (_isAdlxAvailable)
                    {
                        System.Diagnostics.Debug.WriteLine("ADLX SDK initialized successfully");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("ADLX SDK not available, will use registry fallback");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("No AMD GPU detected");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing AmdAdlxService: {ex.Message}");
            }
        }

        /// <summary>
        /// Detect if an AMD GPU is present in the system
        /// </summary>
        private bool DetectAmdGpu()
        {
            try
            {
                // Method 1: Check via WMI
                using (var searcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_VideoController"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        var name = obj["Name"]?.ToString() ?? "";
                        var adapterCompatibility = obj["AdapterCompatibility"]?.ToString() ?? "";

                        if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
                            adapterCompatibility.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                            adapterCompatibility.Contains("ATI", StringComparison.OrdinalIgnoreCase))
                        {
                            System.Diagnostics.Debug.WriteLine($"Found AMD GPU: {name}");
                            return true;
                        }
                    }
                }

                // Method 2: Check registry
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000"))
                {
                    if (key != null)
                    {
                        var providerName = key.GetValue("ProviderName")?.ToString() ?? "";
                        if (providerName.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                            providerName.Contains("ATI", StringComparison.OrdinalIgnoreCase))
                        {
                            System.Diagnostics.Debug.WriteLine($"Found AMD GPU via registry: {providerName}");
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error detecting AMD GPU: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Try to initialize ADLX SDK
        /// </summary>
        private bool TryInitializeAdlx()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Attempting to initialize ADLX SDK...");

                // Check if ADLX DLL is available
                if (!AdlxWrapper.IsAdlxDllAvailable())
                {
                    System.Diagnostics.Debug.WriteLine("ADLX DLL not found - will use registry fallback");
                    return false;
                }

                // Test ADLX functionality by checking RSR support
                if (AdlxWrapper.TryHasRSRSupport(out bool supported))
                {
                    System.Diagnostics.Debug.WriteLine($"ADLX initialized successfully. RSR supported: {supported}");
                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("ADLX DLL found but initialization failed - will use registry fallback");
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing ADLX: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Check if AMD GPU is present
        /// </summary>
        public bool IsAmdGpuAvailable()
        {
            return _isAmdGpuPresent;
        }

        /// <summary>
        /// Get current RSR state
        /// </summary>
        public async Task<(bool success, bool enabled, int sharpness)> GetRsrStateAsync()
        {
            if (!_isAmdGpuPresent)
            {
                return (false, false, 0);
            }

            return await Task.Run(() =>
            {
                try
                {
                    if (_isAdlxAvailable)
                    {
                        // Use ADLX to get RSR state
                        return GetRsrStateViaAdlx();
                    }
                    else
                    {
                        // Use registry to get RSR state
                        return GetRsrStateViaRegistry();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error getting RSR state: {ex.Message}");
                    return (false, false, 0);
                }
            });
        }

        /// <summary>
        /// Enable or disable RSR
        /// </summary>
        public async Task<bool> SetRsrEnabledAsync(bool enabled, int sharpness = 80)
        {
            if (!_isAmdGpuPresent)
            {
                System.Diagnostics.Debug.WriteLine("Cannot set RSR: No AMD GPU detected");
                return false;
            }

            return await Task.Run(() =>
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"Setting RSR: enabled={enabled}, sharpness={sharpness}");

                    if (_isAdlxAvailable)
                    {
                        // Use ADLX to set RSR
                        return SetRsrViaAdlx(enabled, sharpness);
                    }
                    else
                    {
                        // Use registry to set RSR
                        return SetRsrViaRegistry(enabled, sharpness);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error setting RSR: {ex.Message}");
                    return false;
                }
            });
        }

        #region ADLX Methods

        private (bool success, bool enabled, int sharpness) GetRsrStateViaAdlx()
        {
            try
            {
                // Get RSR enabled state
                if (!AdlxWrapper.TryGetRSRState(out bool enabled))
                {
                    System.Diagnostics.Debug.WriteLine("ADLX: Failed to get RSR state");
                    return (false, false, 0);
                }

                // Get RSR sharpness
                if (!AdlxWrapper.TryGetRSRSharpness(out int sharpness))
                {
                    System.Diagnostics.Debug.WriteLine("ADLX: Failed to get RSR sharpness, using default");
                    sharpness = 80; // Default if we can't read it
                }

                System.Diagnostics.Debug.WriteLine($"ADLX: RSR enabled={enabled}, sharpness={sharpness}");
                return (true, enabled, sharpness);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ADLX: Error getting RSR state: {ex.Message}");
                return (false, false, 0);
            }
        }

        private bool SetRsrViaAdlx(bool enabled, int sharpness)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"ADLX: Setting RSR enabled={enabled}, sharpness={sharpness}");

                // Set RSR enabled/disabled
                if (!AdlxWrapper.TrySetRSR(enabled, out bool setRsrSuccess))
                {
                    System.Diagnostics.Debug.WriteLine("ADLX: Failed to call SetRSR");
                    return false;
                }

                if (!setRsrSuccess)
                {
                    System.Diagnostics.Debug.WriteLine("ADLX: SetRSR returned false");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"ADLX: Successfully set RSR enabled={enabled}");

                // Set sharpness if RSR was enabled
                if (enabled)
                {
                    if (!AdlxWrapper.TrySetRSRSharpness(sharpness, out bool setSharpnessSuccess))
                    {
                        System.Diagnostics.Debug.WriteLine($"ADLX: Failed to call SetRSRSharpness");
                        // Don't fail the whole operation if sharpness fails
                        return true;
                    }

                    if (!setSharpnessSuccess)
                    {
                        System.Diagnostics.Debug.WriteLine($"ADLX: SetRSRSharpness returned false");
                        // Don't fail the whole operation if sharpness fails
                        return true;
                    }

                    System.Diagnostics.Debug.WriteLine($"ADLX: Successfully set RSR sharpness={sharpness}");
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ADLX: Error setting RSR: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Registry Methods (Fallback)

        private (bool success, bool enabled, int sharpness) GetRsrStateViaRegistry()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(AMD_REGISTRY_PATH))
                {
                    if (key == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Registry key not found: {AMD_REGISTRY_PATH}");
                        return (false, false, 0);
                    }

                    var rsrEnabled = key.GetValue(AMD_REGISTRY_KEY_RSR_ENABLE);
                    var rsrSharpness = key.GetValue(AMD_REGISTRY_KEY_RSR_SHARPNESS);

                    bool enabled = rsrEnabled != null && Convert.ToInt32(rsrEnabled) == 1;
                    int sharpness = rsrSharpness != null ? Convert.ToInt32(rsrSharpness) : 80;

                    System.Diagnostics.Debug.WriteLine($"Registry: RSR enabled={enabled}, sharpness={sharpness}");
                    return (true, enabled, sharpness);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading RSR state from registry: {ex.Message}");
                return (false, false, 0);
            }
        }

        private bool SetRsrViaRegistry(bool enabled, int sharpness)
        {
            try
            {
                // Try HKEY_CURRENT_USER first (user-level settings)
                using (var key = Registry.CurrentUser.CreateSubKey(AMD_REGISTRY_PATH))
                {
                    if (key == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to create/open registry key: {AMD_REGISTRY_PATH}");
                        return false;
                    }

                    key.SetValue(AMD_REGISTRY_KEY_RSR_ENABLE, enabled ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue(AMD_REGISTRY_KEY_RSR_SHARPNESS, sharpness, RegistryValueKind.DWord);

                    System.Diagnostics.Debug.WriteLine($"Registry: Set RSR enabled={enabled}, sharpness={sharpness}");

                    // Note: AMD driver may need to be notified of changes
                    // This could be done via driver-specific mechanisms or system restart
                    NotifyAmdDriverOfChanges();

                    return true;
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Registry access denied: {ex.Message}. App may need elevated permissions.");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting RSR via registry: {ex.Message}");
                return false;
            }
        }

        private void NotifyAmdDriverOfChanges()
        {
            try
            {
                // Method 1: Try to restart AMD Radeon Software service/process
                var amdProcesses = new[] { "RadeonSoftware", "AMDRSServ", "AMD External Events Utility" };

                foreach (var processName in amdProcesses)
                {
                    try
                    {
                        var processes = Process.GetProcessesByName(processName);
                        if (processes.Length > 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"Found AMD process: {processName}");
                            // Don't restart - just log that it's running
                            // Settings should apply on next driver activation
                        }
                    }
                    catch { }
                }

                // Method 2: Broadcast WM_SETTINGCHANGE message
                // This notifies applications that system settings have changed
                BroadcastSystemSettingChange();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error notifying AMD driver: {ex.Message}");
            }
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SendNotifyMessage(IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam);

        private const uint WM_SETTINGCHANGE = 0x001A;
        private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);

        private void BroadcastSystemSettingChange()
        {
            try
            {
                SendNotifyMessage(HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero, "intl");
                System.Diagnostics.Debug.WriteLine("Broadcasted WM_SETTINGCHANGE");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error broadcasting setting change: {ex.Message}");
            }
        }

        #endregion

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            // Cleanup ADLX resources if initialized
            if (_adlxContext != IntPtr.Zero)
            {
                // TODO: Call ADLX cleanup functions
                _adlxContext = IntPtr.Zero;
            }

            System.Diagnostics.Debug.WriteLine("AmdAdlxService disposed");
        }
    }
}
