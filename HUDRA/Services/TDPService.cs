using System;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Diagnostics;
using HUDRA.Models;

namespace HUDRA.Services
{
    public class TDPService : IDisposable
    {
        private IntPtr _ryzenAdjHandle = IntPtr.Zero;
        private IntPtr _libHandle = IntPtr.Zero;
        private bool _useDllMode = false;
        private bool _disposed = false;
        private string _initializationStatus = "Not initialized";

        // Dynamic function delegates
        private delegate IntPtr InitRyzenAdjDelegate();
        private delegate int SetStapmLimitDelegate(IntPtr ry, uint value);
        private delegate int SetFastLimitDelegate(IntPtr ry, uint value);
        private delegate int SetSlowLimitDelegate(IntPtr ry, uint value);
        private delegate int RefreshTableDelegate(IntPtr ry);
        private delegate float GetStapmLimitDelegate(IntPtr ry);
        private delegate float GetStapmValueDelegate(IntPtr ry);

        private InitRyzenAdjDelegate? _initRyzenAdj;
        private SetStapmLimitDelegate? _setStapmLimit;
        private SetFastLimitDelegate? _setFastLimit;
        private SetSlowLimitDelegate? _setSlowLimit;
        private RefreshTableDelegate? _refreshTable;
        private GetStapmLimitDelegate? _getStapmLimit;
        private GetStapmValueDelegate? _getStapmValue;

        public string InitializationStatus
        {
            get
            {
                var device = HardwareDetectionService.GetDetectedDevice();
                if (device.IsLenovo && device.SupportsLenovoWmi)
                    return "Lenovo WMI Mode";
                return _initializationStatus;
            }
        }
        public bool IsDllMode => _useDllMode;

        // Lenovo WMI Capability IDs for CPU power limits (from HandheldCompanion)
        private const int CAP_CPU_SHORT_TERM_POWER_LIMIT = 0x0101FF00;  // SPL / STAPM
        private const int CAP_CPU_LONG_TERM_POWER_LIMIT = 0x0102FF00;   // Slow limit
        private const int CAP_CPU_PEAK_POWER_LIMIT = 0x0103FF00;        // Fast limit
        private const int CAP_APU_SPPT_POWER_LIMIT = 0x0105FF00;        // APU sPPT

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        public TDPService()
        {
            // Initialize ryzenadj for reading TDP (needed for drift detection)
            // Note: For Lenovo devices, SetTdp() routes to WMI, but GetCurrentTdp() still uses ryzenadj
            InitializeDllMode();
        }

        private void InitializeDllMode()
        {
            try
            {
                DebugLogger.Log("Starting RyzenAdj initialization...", "RYZENADJ");

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                DebugLogger.Log($"Base directory: {baseDir}", "RYZENADJ");

                string[] possiblePaths = {
                    Path.Combine(baseDir, "Tools", "ryzenadj", "libryzenadj.dll"),
                    Path.Combine(baseDir, "libryzenadj.dll"),
                    Path.Combine(baseDir, "Tools", "libryzenadj.dll")
                };

                _initializationStatus = $"Searching in: {baseDir}";

                string foundPath = null;
                string dllDirectory = null;

                foreach (string path in possiblePaths)
                {
                    DebugLogger.Log($"Checking path: {path} - Exists: {File.Exists(path)}", "RYZENADJ");
                    if (File.Exists(path))
                    {
                        foundPath = path;
                        dllDirectory = Path.GetDirectoryName(path);
                        break;
                    }
                }

                if (foundPath == null)
                {
                    _initializationStatus = "libryzenadj.dll not found - using EXE mode";
                    DebugLogger.Log(_initializationStatus, "RYZENADJ");
                    return;
                }

                DebugLogger.Log($"Found DLL at: {foundPath}", "RYZENADJ");
                _initializationStatus = $"Found DLL at: {foundPath}";

                // Set DLL directory for dependency resolution
                if (!string.IsNullOrEmpty(dllDirectory))
                {
                    DebugLogger.Log($"Setting DLL directory to: {dllDirectory}", "RYZENADJ");
                    SetDllDirectory(dllDirectory);
                }

                // Load dependencies first
                DebugLogger.Log("Loading dependencies...", "RYZENADJ");
                LoadDependencies(dllDirectory);

                // Load the main library
                DebugLogger.Log("Loading libryzenadj.dll...", "RYZENADJ");
                _libHandle = LoadLibrary(foundPath);
                if (_libHandle == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    _initializationStatus = $"Failed to load DLL (Error {error}) - using EXE mode";
                    DebugLogger.Log(_initializationStatus, "RYZENADJ");
                    return;
                }
                DebugLogger.Log($"DLL loaded successfully, handle: {_libHandle}", "RYZENADJ");

                // Get function pointers
                DebugLogger.Log("Loading function pointers...", "RYZENADJ");
                if (!LoadFunctionPointers())
                {
                    _initializationStatus = "Failed to load function pointers - using EXE mode";
                    DebugLogger.Log(_initializationStatus, "RYZENADJ");
                    return;
                }
                DebugLogger.Log("Function pointers loaded successfully", "RYZENADJ");

                // Initialize RyzenAdj
                DebugLogger.Log("Calling init_ryzenadj()...", "RYZENADJ");
                _ryzenAdjHandle = _initRyzenAdj!();
                if (_ryzenAdjHandle != IntPtr.Zero)
                {
                    _useDllMode = true;
                    _initializationStatus = "DLL mode active";
                    DebugLogger.Log($"RyzenAdj initialized successfully, handle: {_ryzenAdjHandle}", "RYZENADJ");
                    System.Diagnostics.Debug.WriteLine("RyzenAdj DLL mode initialized successfully");
                }
                else
                {
                    _initializationStatus = "DLL loaded but init_ryzenadj() failed - using EXE mode";
                    DebugLogger.Log(_initializationStatus, "RYZENADJ");
                }
            }
            catch (Exception ex)
            {
                _initializationStatus = $"Exception: {ex.Message} - using EXE mode";
                DebugLogger.Log($"Exception during initialization: {ex.Message}", "RYZENADJ");
                DebugLogger.Log($"Stack trace: {ex.StackTrace}", "RYZENADJ");
                System.Diagnostics.Debug.WriteLine($"DLL initialization failed: {ex.Message}");
                _useDllMode = false;
            }
        }

        private bool LoadFunctionPointers()
        {
            try
            {
                IntPtr initPtr = GetProcAddress(_libHandle, "init_ryzenadj");
                if (initPtr == IntPtr.Zero) return false;
                _initRyzenAdj = Marshal.GetDelegateForFunctionPointer<InitRyzenAdjDelegate>(initPtr);

                IntPtr setStapmPtr = GetProcAddress(_libHandle, "set_stapm_limit");
                if (setStapmPtr != IntPtr.Zero)
                    _setStapmLimit = Marshal.GetDelegateForFunctionPointer<SetStapmLimitDelegate>(setStapmPtr);

                IntPtr setFastPtr = GetProcAddress(_libHandle, "set_fast_limit");
                if (setFastPtr != IntPtr.Zero)
                    _setFastLimit = Marshal.GetDelegateForFunctionPointer<SetFastLimitDelegate>(setFastPtr);

                IntPtr setSlowPtr = GetProcAddress(_libHandle, "set_slow_limit");
                if (setSlowPtr != IntPtr.Zero)
                    _setSlowLimit = Marshal.GetDelegateForFunctionPointer<SetSlowLimitDelegate>(setSlowPtr);

                IntPtr refreshPtr = GetProcAddress(_libHandle, "refresh_table");
                if (refreshPtr != IntPtr.Zero)
                    _refreshTable = Marshal.GetDelegateForFunctionPointer<RefreshTableDelegate>(refreshPtr);

                IntPtr getStapmPtr = GetProcAddress(_libHandle, "get_stapm_limit");
                if (getStapmPtr != IntPtr.Zero)
                    _getStapmLimit = Marshal.GetDelegateForFunctionPointer<GetStapmLimitDelegate>(getStapmPtr);

                return _initRyzenAdj != null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading function pointers: {ex.Message}");
                return false;
            }
        }

        private void LoadDependencies(string dllDirectory)
        {
            try
            {
                string[] dependencies = { "WinRing0x64.dll", "inpoutx64.dll" };

                foreach (string dep in dependencies)
                {
                    string depPath = Path.Combine(dllDirectory, dep);
                    if (File.Exists(depPath))
                    {
                        IntPtr handle = LoadLibrary(depPath);
                        System.Diagnostics.Debug.WriteLine(handle != IntPtr.Zero ?
                            $"✅ Loaded {dep}" : $"⚠️ Failed to load {dep}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Warning loading dependencies: {ex.Message}");
            }
        }

        public (bool Success, int TdpWatts, string Message) GetCurrentTdp()
        {
            if (_useDllMode && _ryzenAdjHandle != IntPtr.Zero)
            {
                return GetCurrentTdpDll();
            }
            else
            {
                return GetCurrentTdpExe();
            }
        }

        public (bool Success, string Message) SetTdp(int tdpInMilliwatts)
        {
            int tdpWatts = tdpInMilliwatts / 1000;

            // Check if Lenovo device with WMI support - use WMI exclusively
            var device = HardwareDetectionService.GetDetectedDevice();
            if (device.IsLenovo && device.SupportsLenovoWmi)
            {
                Debug.WriteLine($"[TDP] Setting TDP to {tdpWatts}W via WMI (Lenovo device)");
                return SetTdpWmi(tdpInMilliwatts);
            }

            // Non-Lenovo: use ryzenadj DLL or EXE
            if (_useDllMode && _ryzenAdjHandle != IntPtr.Zero)
            {
                return SetTdpDll(tdpInMilliwatts);
            }
            else
            {
                return SetTdpExe(tdpInMilliwatts);
            }
        }

        private (bool Success, int TdpWatts, string Message) GetCurrentTdpDll()
        {
            try
            {
                if (_refreshTable != null && _getStapmLimit != null)
                {
                    _refreshTable(_ryzenAdjHandle);
                    float stapmlimit = _getStapmLimit(_ryzenAdjHandle);

                    System.Diagnostics.Debug.WriteLine($"Raw STAPM limit from DLL: {stapmlimit}");

                    if (!float.IsNaN(stapmlimit) && stapmlimit > 0)
                    {
                        // STAPM limit is usually in milliwatts, but sometimes in watts
                        // If it's a small number (< 100), it's probably in watts already
                        int tdpWatts;
                        if (stapmlimit < 100)
                        {
                            tdpWatts = (int)Math.Round(stapmlimit);
                        }
                        else
                        {
                            tdpWatts = (int)Math.Round(stapmlimit / 1000.0f);
                        }

                        System.Diagnostics.Debug.WriteLine($"Calculated TDP: {tdpWatts}W");

                        // If we get an unreasonable value, return failure so we can use default
                        if (tdpWatts < 5 || tdpWatts > 100)
                        {
                            return (false, 0, $"Invalid TDP value read: {tdpWatts}W");
                        }

                        return (true, tdpWatts, "Success");
                    }
                }

                return (false, 0, "Could not read TDP value from DLL");
            }
            catch (Exception ex)
            {
                return (false, 0, $"DLL Exception: {ex.Message}");
            }
        }

        private (bool Success, string Message) SetTdpDll(int tdpInMilliwatts)
        {
            try
            {
                uint tdpValue = (uint)tdpInMilliwatts;
                System.Diagnostics.Debug.WriteLine($"[TDP] Setting TDP to {tdpInMilliwatts}mW ({tdpInMilliwatts/1000}W), uint value: {tdpValue}");

                int stapmResult = -1;
                int fastResult = -1;
                int slowResult = -1;

                if (_setStapmLimit != null)
                {
                    stapmResult = _setStapmLimit(_ryzenAdjHandle, tdpValue);
                    System.Diagnostics.Debug.WriteLine($"[TDP] STAPM limit result: {stapmResult} (0=success)");
                }

                if (_setFastLimit != null)
                {
                    fastResult = _setFastLimit(_ryzenAdjHandle, tdpValue);
                    System.Diagnostics.Debug.WriteLine($"[TDP] Fast limit result: {fastResult} (0=success)");
                }

                if (_setSlowLimit != null)
                {
                    slowResult = _setSlowLimit(_ryzenAdjHandle, tdpValue);
                    System.Diagnostics.Debug.WriteLine($"[TDP] Slow limit result: {slowResult} (0=success)");
                }

                bool stapmSuccess = stapmResult == 0;
                bool fastSuccess = fastResult == 0;
                bool slowSuccess = slowResult == 0;
                bool anySuccess = stapmSuccess || fastSuccess || slowSuccess;
                int targetTdpWatts = tdpInMilliwatts / 1000;

                // Verify STAPM actually changed - driver updates may cause DLL to return success but not apply
                bool stapmVerified = false;
                if (_refreshTable != null && _getStapmLimit != null)
                {
                    // Give hardware time to update before verification
                    System.Threading.Thread.Sleep(2000);
                    _refreshTable(_ryzenAdjHandle);
                    float actualStapm = _getStapmLimit(_ryzenAdjHandle);
                    Debug.WriteLine($"[TDP] Verification - Actual STAPM: {actualStapm}W, Target: {targetTdpWatts}W");

                    // Check if STAPM is within tolerance (2W)
                    stapmVerified = Math.Abs(actualStapm - targetTdpWatts) <= 2;
                }

                // If DLL returned success but STAPM didn't actually change, try WMI fallback
                if (anySuccess && !stapmVerified)
                {
                    Debug.WriteLine($"[TDP] STAPM verification failed - trying WMI fallback");
                    var wmiResult = SetTdpWmi(tdpInMilliwatts);

                    // WMI return codes are unreliable - verify actual STAPM change instead
                    System.Threading.Thread.Sleep(2000);
                    _refreshTable(_ryzenAdjHandle);
                    float stapmAfterWmi = _getStapmLimit(_ryzenAdjHandle);
                    bool wmiActuallyWorked = Math.Abs(stapmAfterWmi - targetTdpWatts) <= 2;
                    Debug.WriteLine($"[TDP] Post-WMI verification - Actual STAPM: {stapmAfterWmi}W, Target: {targetTdpWatts}W, Success: {wmiActuallyWorked}");

                    if (wmiActuallyWorked)
                    {
                        var details = $"STAPM:{stapmResult} Fast:{fastResult} Slow:{slowResult}";
                        return (true, $"TDP set to {targetTdpWatts}W (DLL+WMI) [{details}]");
                    }
                    else
                    {
                        Debug.WriteLine($"[TDP] WMI fallback did not change STAPM");
                        // Still return success if fast/slow limits worked
                        var details = $"STAPM:{stapmResult}(unverified) Fast:{fastResult} Slow:{slowResult}";
                        return (true, $"TDP set to {targetTdpWatts}W (DLL partial) [{details}]");
                    }
                }

                if (anySuccess)
                {
                    var details = $"STAPM:{stapmResult} Fast:{fastResult} Slow:{slowResult}";
                    return (true, $"TDP set to {targetTdpWatts}W (DLL) [{details}]");
                }
                else
                {
                    return (false, "All TDP set operations failed");
                }
            }
            catch (Exception ex)
            {
                return (false, $"DLL Exception: {ex.Message}");
            }
        }

        private (bool Success, string Message) SetTdpWmi(int tdpInMilliwatts)
        {
            try
            {
                int tdpWatts = tdpInMilliwatts / 1000;
                Debug.WriteLine($"[TDP] Setting TDP via Lenovo WMI: {tdpWatts}W");

                // Use LENOVO_OTHER_METHOD.SetFeatureValue (same as HandheldCompanion)
                using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM LENOVO_OTHER_METHOD");

                foreach (ManagementObject instance in searcher.Get())
                {
                    try
                    {
                        // Set all CPU power limits to the same value
                        // Note: Return codes are unreliable - actual success is verified by caller
                        int[] capabilityIds = {
                            CAP_CPU_SHORT_TERM_POWER_LIMIT,  // SPL/STAPM
                            CAP_CPU_LONG_TERM_POWER_LIMIT,   // Slow
                            CAP_CPU_PEAK_POWER_LIMIT,        // Fast
                            CAP_APU_SPPT_POWER_LIMIT         // APU sPPT
                        };

                        foreach (int capId in capabilityIds)
                        {
                            try
                            {
                                var inParams = instance.GetMethodParameters("SetFeatureValue");
                                inParams["IDs"] = capId;
                                inParams["value"] = tdpWatts;
                                instance.InvokeMethod("SetFeatureValue", inParams, null);
                            }
                            catch { /* Ignore - return codes unreliable anyway */ }
                        }

                        // Return success - actual verification done by caller
                        return (true, $"WMI calls completed for {tdpWatts}W");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[TDP] WMI method call failed: {ex.Message}");
                    }
                }

                return (false, "LENOVO_OTHER_METHOD not available");
            }
            catch (ManagementException)
            {
                return (false, "WMI not available (non-Lenovo device)");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TDP] WMI exception: {ex.Message}");
                return (false, $"WMI Exception: {ex.Message}");
            }
        }

        // Fallback EXE methods (same as before)
        private string GetRyzenAdjPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "ryzenadj", "ryzenadj.exe");
        }

        private (bool Success, int TdpWatts, string Message) GetCurrentTdpExe()
        {
            try
            {
                var ryzenAdjPath = GetRyzenAdjPath();
                if (!File.Exists(ryzenAdjPath))
                {
                    return (false, 0, "RyzenAdj not found");
                }

                var processInfo = new ProcessStartInfo
                {
                    FileName = ryzenAdjPath,
                    Arguments = "-i",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process == null)
                {
                    return (false, 0, "Failed to start RyzenAdj process");
                }

                process.WaitForExit(3000);

                if (process.ExitCode != 0)
                {
                    var error = process.StandardError.ReadToEnd();
                    return (false, 0, $"RyzenAdj error: {error}");
                }

                var output = process.StandardOutput.ReadToEnd();
                var stapmlimitPattern = @"\|\s*STAPM LIMIT\s*\|\s*(\d+(?:\.\d+)?)\s*\|";
                var match = Regex.Match(output, stapmlimitPattern, RegexOptions.IgnoreCase);

                if (match.Success)
                {
                    if (double.TryParse(match.Groups[1].Value, out double tdpValue))
                    {
                        int tdpWatts = (int)Math.Round(tdpValue);
                        return (true, tdpWatts, "Success");
                    }
                }

                return (false, 0, "Could not parse TDP value from output");
            }
            catch (Exception ex)
            {
                return (false, 0, $"Exception: {ex.Message}");
            }
        }

        private (bool Success, string Message) SetTdpExe(int tdpInMilliwatts)
        {
            try
            {
                var ryzenAdjPath = GetRyzenAdjPath();
                if (!File.Exists(ryzenAdjPath))
                {
                    return (false, "RyzenAdj.exe not found");
                }

                var tdpWatts = tdpInMilliwatts / 1000;
                var arguments = $"--stapm-limit={tdpInMilliwatts} --fast-limit={tdpInMilliwatts} --slow-limit={tdpInMilliwatts}";

                var processInfo = new ProcessStartInfo
                {
                    FileName = ryzenAdjPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process == null)
                {
                    return (false, "Failed to start RyzenAdj process");
                }

                process.WaitForExit(5000); // Keep original timeout since this is fallback mode
                const int ACCESS_VIOLATION_CODE = -1073741819;
                if (process.ExitCode == 0 || process.ExitCode == ACCESS_VIOLATION_CODE)
                {
                    return (true, $"TDP set to {tdpWatts}W (EXE-FALLBACK)");
                }
                else
                {
                    var error = process.StandardError.ReadToEnd();
                    return (false, $"RyzenAdj error: {error}");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Exception: {ex.Message}");
            }
        }

        public (bool Success, string Message) ReinitializeAfterResume()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("⚡ Reinitializing TDPService after hibernation resume...");

                // Clean up existing handles first
                if (_libHandle != IntPtr.Zero)
                {
                    FreeLibrary(_libHandle);
                    _libHandle = IntPtr.Zero;
                }
                _ryzenAdjHandle = IntPtr.Zero;

                // Reset state
                _useDllMode = false;
                _initializationStatus = "Reinitializing after resume";

                // Clear all function delegates
                _initRyzenAdj = null;
                _setStapmLimit = null;
                _setFastLimit = null;
                _setSlowLimit = null;
                _refreshTable = null;
                _getStapmLimit = null;
                _getStapmValue = null;

                // Re-initialize DLL mode
                InitializeDllMode();

                var success = _useDllMode && _ryzenAdjHandle != IntPtr.Zero;
                var message = success 
                    ? "TDPService successfully reinitialized after hibernation resume"
                    : "TDPService reinitialization failed - falling back to EXE mode";

                System.Diagnostics.Debug.WriteLine($"⚡ {message}");
                return (success, message);
            }
            catch (Exception ex)
            {
                var errorMessage = $"Exception during TDPService reinitialization: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"⚠️ {errorMessage}");
                return (false, errorMessage);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_libHandle != IntPtr.Zero)
                {
                    FreeLibrary(_libHandle);
                    _libHandle = IntPtr.Zero;
                }
                _ryzenAdjHandle = IntPtr.Zero;
                _disposed = true;
            }
        }

        ~TDPService()
        {
            Dispose();
        }
    }
}