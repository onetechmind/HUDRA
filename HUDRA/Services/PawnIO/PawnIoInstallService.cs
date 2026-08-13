using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using Microsoft.Win32;

namespace HUDRA.Services.PawnIO
{
    /// <summary>
    /// Detects and silently installs the PawnIO driver, and resolves the paths of the
    /// PawnIO assets bundled under Tools\PawnIO.
    ///
    /// HUDRA always runs elevated, so the installer needs no extra elevation step.
    /// </summary>
    public static class PawnIoInstallService
    {
        private const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO";
        private const string InstallerUrl = "https://github.com/namazso/PawnIO.Setup/releases/latest/download/PawnIO_setup.exe";
        private const int InstallTimeoutMs = 120000;

        /// <summary>
        /// Reports whether PawnIO is installed, plus its version and install location when known.
        /// Falls back to a device probe when the uninstall registry entry is missing (for example
        /// when the driver was installed by another tool that did not register itself).
        /// </summary>
        public static (bool Installed, string Version, string InstallLocation) Detect()
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                    using var key = baseKey.OpenSubKey(UninstallKeyPath);
                    if (key == null)
                        continue;

                    var version = key.GetValue("DisplayVersion") as string ?? string.Empty;
                    var location = key.GetValue("InstallLocation") as string ?? string.Empty;

                    DebugLogger.Log($"PawnIO found in registry ({view}): version='{version}' location='{location}'", "PAWNIO");
                    return (true, version, location);
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"Reading PawnIO registry ({view}) failed: {ex.Message}", "PAWNIO");
                }
            }

            if (PawnIoTransport.IsDriverPresent())
            {
                DebugLogger.Log("PawnIO not in registry but the device responded - treating as installed", "PAWNIO");
                return (true, string.Empty, string.Empty);
            }

            DebugLogger.Log("PawnIO is not installed", "PAWNIO");
            return (false, string.Empty, string.Empty);
        }

        /// <summary>
        /// Downloads the official PawnIO installer at runtime, runs it silently, then re-detects.
        /// The downloaded installer is deleted afterwards.
        ///
        /// This runs synchronously by design: SettingsPage wraps it in Task.Run and App startup
        /// calls it on a background thread, so the public signature stays non-async.
        /// </summary>
        public static (bool Success, string Message) InstallSilent()
        {
            var installerPath = Path.Combine(Path.GetTempPath(), "HUDRA", "PawnIO_setup.exe");

            try
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(installerPath)!);

                    DebugLogger.Log($"Downloading PawnIO installer from {InstallerUrl}", "PAWNIO");

                    using var httpClient = new HttpClient
                    {
                        Timeout = TimeSpan.FromSeconds(120)
                    };

                    var bytes = httpClient.GetByteArrayAsync(InstallerUrl).GetAwaiter().GetResult();
                    File.WriteAllBytes(installerPath, bytes);

                    DebugLogger.Log($"Downloaded PawnIO installer ({bytes.Length} bytes) to {installerPath}", "PAWNIO");
                }
                catch (Exception ex)
                {
                    var downloadMessage = $"Could not download PawnIO installer: {ex.Message}. Check your internet connection.";
                    DebugLogger.Log(downloadMessage, "PAWNIO");
                    return (false, downloadMessage);
                }

                DebugLogger.Log($"Running PawnIO installer silently: {installerPath}", "PAWNIO");

                var startInfo = new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = "-install -silent",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(installerPath) ?? AppContext.BaseDirectory
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    const string startMessage = "PawnIO installer could not be started.";
                    DebugLogger.Log(startMessage, "PAWNIO");
                    return (false, startMessage);
                }

                if (!process.WaitForExit(InstallTimeoutMs))
                {
                    var timeoutMessage = $"PawnIO installer did not finish within {InstallTimeoutMs / 1000} seconds.";
                    DebugLogger.Log(timeoutMessage, "PAWNIO");
                    return (false, timeoutMessage);
                }

                DebugLogger.Log($"PawnIO installer exited with code {process.ExitCode}", "PAWNIO");

                var detection = Detect();
                if (detection.Installed)
                {
                    var okMessage = string.IsNullOrEmpty(detection.Version)
                        ? "PawnIO installed."
                        : $"PawnIO {detection.Version} installed.";
                    DebugLogger.Log(okMessage, "PAWNIO");
                    return (true, okMessage);
                }

                var failMessage = $"PawnIO installer finished (exit code {process.ExitCode}) but PawnIO was still not detected.";
                DebugLogger.Log(failMessage, "PAWNIO");
                return (false, failMessage);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"Installing PawnIO threw: {ex.Message}", "PAWNIO");
                return (false, ex.Message);
            }
            finally
            {
                try
                {
                    if (File.Exists(installerPath))
                        File.Delete(installerPath);
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"Could not delete temp PawnIO installer: {ex.Message}", "PAWNIO");
                }
            }
        }

        /// <summary>
        /// Resolves a bundled PawnIO module binary, e.g. "LpcIO.bin".
        /// </summary>
        public static string GetModulePath(string moduleName)
        {
            return Path.Combine(AppContext.BaseDirectory, "Tools", "PawnIO", moduleName);
        }
    }
}
