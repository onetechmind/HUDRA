using System;
using System.IO;
using System.Runtime.InteropServices;

namespace HUDRA.Services.PawnIO
{
    /// <summary>
    /// Owns a single handle to the PawnIO driver device.
    ///
    /// PawnIO gives every open handle its own module instance, so one transport ==
    /// one loaded module. Loading a second module requires a second transport.
    ///
    /// Nothing here throws out to callers: every public entry point returns a
    /// (Success, Message) style tuple and logs failures under "PAWNIO".
    /// </summary>
    public sealed class PawnIoTransport : IDisposable
    {
        // PawnIO >= 2.1.0 exposes the device under GLOBALROOT; older builds only have \\.\PawnIO.
        private const string DevicePathGlobalRoot = @"\\?\GLOBALROOT\Device\PawnIO";
        private const string DevicePathLegacy = @"\\.\PawnIO";

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint OPEN_EXISTING = 3;
        private const int ERROR_ACCESS_DENIED = 5;

        private static readonly IntPtr InvalidHandleValue = new IntPtr(-1);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(
            IntPtr hDevice,
            uint dwIoControlCode,
            byte[]? lpInBuffer,
            int nInBufferSize,
            byte[]? lpOutBuffer,
            int nOutBufferSize,
            out int lpBytesReturned,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        private IntPtr _handle;
        private bool _moduleLoaded;
        private readonly string _devicePath;

        private PawnIoTransport(IntPtr handle, string devicePath)
        {
            _handle = handle;
            _devicePath = devicePath;
        }

        /// <summary>
        /// True once a module binary has been successfully loaded onto this handle.
        /// </summary>
        public bool IsModuleLoaded => _moduleLoaded;

        /// <summary>
        /// The device path this transport was opened against (diagnostics only).
        /// </summary>
        public string DevicePath => _devicePath;

        /// <summary>
        /// Opens a handle to the PawnIO device, preferring the GLOBALROOT path.
        /// Returns (null, message) if the driver is missing or access is denied.
        /// </summary>
        public static (PawnIoTransport? Transport, string Message) TryOpen()
        {
            int lastError = 0;

            foreach (var path in new[] { DevicePathGlobalRoot, DevicePathLegacy })
            {
                try
                {
                    var handle = CreateFile(
                        path,
                        GENERIC_READ | GENERIC_WRITE,
                        0,
                        IntPtr.Zero,
                        OPEN_EXISTING,
                        0,
                        IntPtr.Zero);

                    if (handle != InvalidHandleValue)
                    {
                        DebugLogger.Log($"Opened PawnIO device at {path}", "PAWNIO");
                        return (new PawnIoTransport(handle, path), $"Opened {path}");
                    }

                    lastError = Marshal.GetLastWin32Error();
                    DebugLogger.Log($"CreateFile failed for {path} (Win32 error {lastError})", "PAWNIO");
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"CreateFile threw for {path}: {ex.Message}", "PAWNIO");
                }
            }

            var message = $"Could not open the PawnIO device (Win32 error {lastError}). Is the PawnIO driver installed?";
            DebugLogger.Log(message, "PAWNIO");
            return (null, message);
        }

        /// <summary>
        /// Probes for the driver without keeping a handle. Access-denied still counts as
        /// present: the device object exists, we simply were not allowed to open it.
        /// </summary>
        public static bool IsDriverPresent()
        {
            foreach (var path in new[] { DevicePathGlobalRoot, DevicePathLegacy })
            {
                try
                {
                    var handle = CreateFile(
                        path,
                        GENERIC_READ | GENERIC_WRITE,
                        0,
                        IntPtr.Zero,
                        OPEN_EXISTING,
                        0,
                        IntPtr.Zero);

                    if (handle != InvalidHandleValue)
                    {
                        CloseHandle(handle);
                        return true;
                    }

                    if (Marshal.GetLastWin32Error() == ERROR_ACCESS_DENIED)
                        return true;
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"Driver probe threw for {path}: {ex.Message}", "PAWNIO");
                }
            }

            return false;
        }

        /// <summary>
        /// Loads a PawnIO module binary (.bin) onto this handle.
        /// </summary>
        public (bool Success, string Message) LoadModuleFromFile(string binPath)
        {
            if (_handle == IntPtr.Zero || _handle == InvalidHandleValue)
            {
                const string closedMessage = "PawnIO transport is not open.";
                DebugLogger.Log(closedMessage, "PAWNIO");
                return (false, closedMessage);
            }

            if (string.IsNullOrWhiteSpace(binPath) || !File.Exists(binPath))
            {
                var missingMessage = $"PawnIO module not found: {binPath}";
                DebugLogger.Log(missingMessage, "PAWNIO");
                return (false, missingMessage);
            }

            try
            {
                var moduleBytes = File.ReadAllBytes(binPath);

                bool ok = DeviceIoControl(
                    _handle,
                    PawnIoFraming.IoctlLoadBinary,
                    moduleBytes,
                    moduleBytes.Length,
                    null,
                    0,
                    out _,
                    IntPtr.Zero);

                if (!ok)
                {
                    int error = Marshal.GetLastWin32Error();
                    var failMessage = $"Loading PawnIO module '{Path.GetFileName(binPath)}' failed (Win32 error {error}).";
                    DebugLogger.Log(failMessage, "PAWNIO");
                    return (false, failMessage);
                }

                _moduleLoaded = true;
                var message = $"Loaded PawnIO module '{Path.GetFileName(binPath)}' ({moduleBytes.Length} bytes)";
                DebugLogger.Log(message, "PAWNIO");
                return (true, message);
            }
            catch (Exception ex)
            {
                var message = $"Loading PawnIO module '{binPath}' threw: {ex.Message}";
                DebugLogger.Log(message, "PAWNIO");
                return (false, message);
            }
        }

        /// <summary>
        /// Calls an exported function on the loaded module.
        /// Returns an empty output array on any failure; never throws.
        /// </summary>
        public (bool Success, ulong[] Output, string Message) Execute(string function, ulong[] args, int outCount)
        {
            if (_handle == IntPtr.Zero || _handle == InvalidHandleValue)
            {
                const string closedMessage = "PawnIO transport is not open.";
                DebugLogger.Log(closedMessage, "PAWNIO");
                return (false, Array.Empty<ulong>(), closedMessage);
            }

            try
            {
                var input = PawnIoFraming.BuildExecuteInput(function, args);
                int outputSize = outCount > 0 ? outCount * 8 : 0;
                var output = new byte[outputSize];

                bool ok = DeviceIoControl(
                    _handle,
                    PawnIoFraming.IoctlExecute,
                    input,
                    input.Length,
                    outputSize > 0 ? output : null,
                    outputSize,
                    out int bytesReturned,
                    IntPtr.Zero);

                if (!ok)
                {
                    int error = Marshal.GetLastWin32Error();
                    var failMessage = $"PawnIO execute '{function}' failed (Win32 error {error}).";
                    DebugLogger.Log(failMessage, "PAWNIO");
                    return (false, Array.Empty<ulong>(), failMessage);
                }

                var values = PawnIoFraming.ParseExecuteOutput(output, bytesReturned);
                return (true, values, $"PawnIO execute '{function}' returned {values.Length} value(s)");
            }
            catch (Exception ex)
            {
                var message = $"PawnIO execute '{function}' threw: {ex.Message}";
                DebugLogger.Log(message, "PAWNIO");
                return (false, Array.Empty<ulong>(), message);
            }
        }

        public void Dispose()
        {
            if (_handle == IntPtr.Zero || _handle == InvalidHandleValue)
                return;

            try
            {
                CloseHandle(_handle);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"Closing PawnIO handle threw: {ex.Message}", "PAWNIO");
            }
            finally
            {
                _handle = InvalidHandleValue;
                _moduleLoaded = false;
            }
        }
    }
}
