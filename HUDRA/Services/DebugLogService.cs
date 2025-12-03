using System;
using System.IO;
using System.Reflection;

namespace HUDRA.Services
{
    public static class DebugLogger
    {
        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HUDRA",
            "Logs");

        private static readonly string LogPath = Path.Combine(LogDirectory, "HUDRA_Debug.log");

        private static readonly object LogLock = new object();

        static DebugLogger()
        {
            // Ensure log directory exists
            try
            {
                if (!Directory.Exists(LogDirectory))
                {
                    Directory.CreateDirectory(LogDirectory);
                }
            }
            catch
            {
                // Fail silently
            }
        }

        public static void Log(string message, string category = "DEBUG")
        {
            try
            {
                lock (LogLock)
                {
                    var logEntry = $"[{DateTime.Now:HH:mm:ss.fff}] [{category}] {message}";

                    // Also write to debug output for development
                    System.Diagnostics.Debug.WriteLine(logEntry);

                    // Write to file
                    File.AppendAllText(LogPath, logEntry + Environment.NewLine);
                }
            }
            catch
            {
                // Fail silently - don't crash the app for logging issues
            }
        }

        public static void LogWindowJump(string source, Microsoft.UI.Input.PointerDeviceType pointerType)
        {
            Log($"WINDOW_JUMP from {source} using {pointerType}", "JUMP");
        }

        public static void LogTdpJump(int expectedTdp, int actualTdp, string source)
        {
            Log($"TDP_JUMP expected:{expectedTdp} actual:{actualTdp} from:{source}", "TDP");
        }

        public static void LogNavigation(string action, string details = "")
        {
            Log($"NAVIGATION {action} {details}", "NAV");
        }

        public static void ClearLog()
        {
            try
            {
                if (File.Exists(LogPath))
                    File.Delete(LogPath);
            }
            catch { }
        }

        /// <summary>
        /// Writes a crash report to a timestamped file.
        /// </summary>
        public static void WriteCrashReport(Exception exception)
        {
            try
            {
                // Ensure directory exists
                if (!Directory.Exists(LogDirectory))
                {
                    Directory.CreateDirectory(LogDirectory);
                }

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var crashLogPath = Path.Combine(LogDirectory, $"HUDRA_Crash_{timestamp}.log");

                var version = GetAppVersion();

                var crashReport = $@"===============================================
HUDRA CRASH REPORT
===============================================
Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}
Version: {version}
OS: {Environment.OSVersion}
64-bit OS: {Environment.Is64BitOperatingSystem}
64-bit Process: {Environment.Is64BitProcess}
CLR Version: {Environment.Version}

===============================================
EXCEPTION DETAILS
===============================================
Exception Type: {exception.GetType().FullName}
Message: {exception.Message}

Stack Trace:
{exception.StackTrace}

";

                // Include inner exceptions
                var innerException = exception.InnerException;
                int innerCount = 1;
                while (innerException != null)
                {
                    crashReport += $@"
-----------------------------------------------
INNER EXCEPTION {innerCount}
-----------------------------------------------
Type: {innerException.GetType().FullName}
Message: {innerException.Message}

Stack Trace:
{innerException.StackTrace}

";
                    innerException = innerException.InnerException;
                    innerCount++;
                }

                crashReport += "===============================================\n";

                File.WriteAllText(crashLogPath, crashReport);
                System.Diagnostics.Debug.WriteLine($"Crash report written to: {crashLogPath}");
            }
            catch
            {
                // If crash logging fails, there's nothing else we can do
            }
        }

        /// <summary>
        /// Gets the application version string.
        /// </summary>
        public static string GetAppVersion()
        {
            try
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                if (version != null)
                {
                    return $"HUDRA Beta v{version.Major}.{version.Minor}.{version.Build}";
                }
            }
            catch
            {
                // Fail silently
            }
            return "HUDRA Beta (unknown version)";
        }
    }
}