using System;
using System.IO;

namespace HUDRA.Services
{
    public static class DebugLogger
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "HUDRA_Debug.log");

        private static readonly object LogLock = new object();

        public static void Log(string message, string category = "DEBUG")
        {
            try
            {
                lock (LogLock)
                {
                    var logEntry = $"[{DateTime.Now:HH:mm:ss.fff}] [{category}] {message}";

                    // Also write to debug output for development
                    System.Diagnostics.Debug.WriteLine(logEntry);

                    // Write to file for X1 Mini testing
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
    }
}