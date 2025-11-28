using HUDRA.Models;
using System;
using System.Diagnostics;

namespace HUDRA.Services
{
    public class GameLauncherService
    {
        /// <summary>
        /// Launch a game using platform-specific launcher or direct executable
        /// </summary>
        /// <param name="game">The game to launch</param>
        /// <returns>True if launch was successful, false otherwise</returns>
        public bool LaunchGame(DetectedGame game)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"GameLauncher: Attempting to launch {game.DisplayName}");

                // Strategy 1: Try platform-specific launch via LauncherInfo
                // Only attempt if LauncherInfo is a valid protocol URL (contains "://")
                if (!string.IsNullOrEmpty(game.LauncherInfo) && game.LauncherInfo.Contains("://"))
                {
                    System.Diagnostics.Debug.WriteLine($"GameLauncher: Trying platform launch with LauncherInfo: {game.LauncherInfo}");

                    if (TryPlatformLaunch(game))
                    {
                        System.Diagnostics.Debug.WriteLine($"GameLauncher: Successfully launched {game.DisplayName} via platform");
                        return true;
                    }
                }
                else if (!string.IsNullOrEmpty(game.LauncherInfo))
                {
                    System.Diagnostics.Debug.WriteLine($"GameLauncher: Skipping invalid LauncherInfo (not a protocol URL): {game.LauncherInfo}");
                }

                // Strategy 2: Try direct executable launch
                if (!string.IsNullOrEmpty(game.ExecutablePath) && System.IO.File.Exists(game.ExecutablePath))
                {
                    System.Diagnostics.Debug.WriteLine($"GameLauncher: Trying direct executable launch: {game.ExecutablePath}");

                    if (TryDirectLaunch(game.ExecutablePath))
                    {
                        System.Diagnostics.Debug.WriteLine($"GameLauncher: Successfully launched {game.DisplayName} via direct executable");
                        return true;
                    }
                }

                System.Diagnostics.Debug.WriteLine($"GameLauncher: Failed to launch {game.DisplayName}");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GameLauncher: Error launching {game.DisplayName}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Try to launch game via platform-specific launcher (Steam, Epic, etc.)
        /// </summary>
        private bool TryPlatformLaunch(DetectedGame game)
        {
            try
            {
                // LauncherInfo typically contains the platform-specific URL or command
                // For example, Steam uses "steam://rungameid/XXXXX"
                // Epic uses "com.epicgames.launcher://apps/XXXXX?action=launch&silent=true"

                var startInfo = new ProcessStartInfo
                {
                    FileName = game.LauncherInfo,
                    UseShellExecute = true
                };

                Process.Start(startInfo);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GameLauncher: Platform launch failed for {game.DisplayName}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Try to launch game directly via executable
        /// </summary>
        private bool TryDirectLaunch(string executablePath)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    UseShellExecute = true,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(executablePath)
                };

                Process.Start(startInfo);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GameLauncher: Direct launch failed for {executablePath}: {ex.Message}");
                return false;
            }
        }
    }
}
