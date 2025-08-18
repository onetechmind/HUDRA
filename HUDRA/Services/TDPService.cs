using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Diagnostics;

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

        public string InitializationStatus => _initializationStatus;
        public bool IsDllMode => _useDllMode;

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
            InitializeDllMode();
        }

        private void InitializeDllMode()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
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
                    if (File.Exists(path))
                    {
                        foundPath = path;
                        dllDirectory = Path.GetDirectoryName(path);
                        break;
                    }
                }

                if (foundPath == null)
                {
                    _initializationStatus = "❌ libryzenadj.dll not found - using EXE mode";
                    return;
                }

                _initializationStatus = $"Found DLL at: {foundPath}";

                // Set DLL directory for dependency resolution
                if (!string.IsNullOrEmpty(dllDirectory))
                {
                    SetDllDirectory(dllDirectory);
                }

                // Load dependencies first
                LoadDependencies(dllDirectory);

                // Load the main library
                _libHandle = LoadLibrary(foundPath);
                if (_libHandle == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    _initializationStatus = $"❌ Failed to load DLL (Error {error}) - using EXE mode";
                    return;
                }

                // Get function pointers
                if (!LoadFunctionPointers())
                {
                    _initializationStatus = "❌ Failed to load function pointers - using EXE mode";
                    return;
                }

                // Initialize RyzenAdj
                _ryzenAdjHandle = _initRyzenAdj!();
                if (_ryzenAdjHandle != IntPtr.Zero)
                {
                    _useDllMode = true;
                    _initializationStatus = "✅ DLL mode active - FAST!";
                    System.Diagnostics.Debug.WriteLine("🚀 RyzenAdj DLL mode initialized successfully");
                }
                else
                {
                    _initializationStatus = "⚠️ DLL loaded but init_ryzenadj() failed - using EXE mode";
                }
            }
            catch (Exception ex)
            {
                _initializationStatus = $"❌ Exception: {ex.Message} - using EXE mode";
                System.Diagnostics.Debug.WriteLine($"❌ DLL initialization failed: {ex.Message}");
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
                bool stapmSuccess = false;
                bool fastSuccess = false;
                bool slowSuccess = false;

                if (_setStapmLimit != null)
                {
                    stapmSuccess = _setStapmLimit(_ryzenAdjHandle, tdpValue) == 0;
                    System.Diagnostics.Debug.WriteLine($"STAPM limit set: {stapmSuccess}");
                }

                if (_setFastLimit != null)
                {
                    fastSuccess = _setFastLimit(_ryzenAdjHandle, tdpValue) == 0;
                    System.Diagnostics.Debug.WriteLine($"Fast limit set: {fastSuccess}");
                }

                if (_setSlowLimit != null)
                {
                    slowSuccess = _setSlowLimit(_ryzenAdjHandle, tdpValue) == 0;
                    System.Diagnostics.Debug.WriteLine($"Slow limit set: {slowSuccess}");
                }

                bool anySuccess = stapmSuccess || fastSuccess || slowSuccess;

                if (anySuccess)
                {
                    var tdpWatts = tdpInMilliwatts / 1000;
                    var details = $"STAPM:{stapmSuccess} Fast:{fastSuccess} Slow:{slowSuccess}";
                    return (true, $"TDP set to {tdpWatts}W (DLL-FAST) [{details}]");
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