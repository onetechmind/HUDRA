using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;

namespace HUDRA.Services
{
    public static class StartupService
    {
        private const string TASK_NAME = "HUDRA_Startup";

        /// <summary>
        /// Enables or disables HUDRA to start with Windows using Task Scheduler.
        /// This preserves admin privileges and provides the best user experience.
        /// </summary>
        /// <param name="enable">True to enable startup, false to disable</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool SetStartupEnabled(bool enable)
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                {
                    Debug.WriteLine("❌ Cannot determine executable path");
                    return false;
                }

                if (enable)
                {
                    return CreateStartupTask(exePath);
                }
                else
                {
                    return DeleteStartupTask();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Startup operation failed: {ex.Message}");
                return false;
            }
        }

        private static bool CreateStartupTask(string exePath)
        {
            try
            {
                // Create task with compatible parameters only
                var arguments = $"/create /tn \"{TASK_NAME}\" " +
                               $"/tr \"\\\"{exePath}\\\" --startup\" " +
                               $"/sc onlogon " +               // Schedule: on logon
                               $"/rl highest " +               // Run with highest privileges  
                               $"/delay 0000:15 " +            // 30 second delay
                               $"/f";                          // Force overwrite if exists

                var result = ExecuteSchedulerCommand(arguments);

                if (result.success)
                {
                    // Now configure the conditions and settings that schtasks can't directly set
                    bool conditionsFixed = FixTaskConditions();

                    return true;
                }
                else
                {
                    Debug.WriteLine($"❌ Task creation failed: {result.output}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Error creating startup task: {ex.Message}");
                return false;
            }
        }

        private static bool FixTaskConditions()
        {
            try
            {
                // Use schtasks to modify the task conditions
                // Unfortunately, we need to use XML export/import to fix the power conditions
                var tempXmlPath = Path.GetTempFileName() + ".xml";

                // Export current task to XML
                var exportArgs = $"/query /tn \"{TASK_NAME}\" /xml";
                var exportResult = ExecuteSchedulerCommand(exportArgs);

                if (!exportResult.success)
                {
                    Debug.WriteLine("❌ Failed to export task XML");
                    return false;
                }

                // Modify the XML to fix power conditions
                var modifiedXml = FixTaskXml(exportResult.output);
                File.WriteAllText(tempXmlPath, modifiedXml);

                // Import the modified XML
                var importArgs = $"/create /tn \"{TASK_NAME}\" /xml \"{tempXmlPath}\" /f";
                var importResult = ExecuteSchedulerCommand(importArgs);

                // Clean up temp file
                try { File.Delete(tempXmlPath); } catch { }

                if (importResult.success)
                {
                    Debug.WriteLine("✅ Task conditions fixed via XML import");
                    return true;
                }
                else
                {
                    Debug.WriteLine($"⚠️ XML import failed: {importResult.output}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠️ Error fixing task conditions: {ex.Message}");
                return false;
            }
        }

        private static string FixTaskXml(string originalXml)
        {
            try
            {
                // Fix the power conditions in the XML
                var xml = originalXml;

                // Remove AC power requirement
                xml = xml.Replace("<DisallowStartIfOnBatteries>true</DisallowStartIfOnBatteries>",
                                 "<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>");

                // Remove stop on battery power
                xml = xml.Replace("<StopIfGoingOnBatteries>true</StopIfGoingOnBatteries>",
                                 "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>");

                // Enable "run as soon as possible after missed"
                xml = xml.Replace("<StartWhenAvailable>false</StartWhenAvailable>",
                                 "<StartWhenAvailable>true</StartWhenAvailable>");

                // If the tags don't exist, add them
                if (!xml.Contains("<DisallowStartIfOnBatteries>"))
                {
                    xml = xml.Replace("</Settings>",
                                     "<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\n    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\n    <StartWhenAvailable>true</StartWhenAvailable>\n  </Settings>");
                }

                Debug.WriteLine("🔧 Modified task XML for handheld device compatibility");
                return xml;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠️ Error modifying XML: {ex.Message}");
                return originalXml; // Return original if modification fails
            }
        }

        private static bool DeleteStartupTask()
        {
            try
            {
                var arguments = $"/delete /tn \"{TASK_NAME}\" /f";
                var result = ExecuteSchedulerCommand(arguments);

                if (result.success)
                {
                    Debug.WriteLine("✅ HUDRA startup task removed successfully");
                    return true;
                }
                else
                {
                    // Task might not exist, which is fine
                    Debug.WriteLine($"ℹ️ Task deletion result: {result.output}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Error deleting startup task: {ex.Message}");
                return false;
            }
        }

        private static (bool success, string output) ExecuteSchedulerCommand(string arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "schtasks",
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                        return (false, "Failed to start schtasks process");

                    process.WaitForExit(15000); // Generous timeout for UAC

                    var output = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();

                    var fullOutput = !string.IsNullOrEmpty(error) ? $"{output}\n{error}" : output;

                    return (process.ExitCode == 0, fullOutput.Trim());
                }
            }
            catch (Exception ex)
            {
                return (false, $"Exception: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if HUDRA startup task exists
        /// </summary>
        public static bool IsStartupEnabled()
        {
            try
            {
                var arguments = $"/query /tn \"{TASK_NAME}\"";
                var result = ExecuteSchedulerCommand(arguments);

                return result.success;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if the app was launched at startup
        /// </summary>
        public static bool WasLaunchedAtStartup(string[] args)
        {
            return args != null && Array.Exists(args, arg => arg == "--startup");
        }

        /// <summary>
        /// Checks if the current process has admin privileges
        /// </summary>
        public static bool IsRunningAsAdmin()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets detailed startup status for debugging
        /// </summary>
        public static string GetStartupStatus()
        {
            try
            {
                var isAdmin = IsRunningAsAdmin();
                var isEnabled = IsStartupEnabled();

                return $"Admin: {(isAdmin ? "✅" : "❌")} | Startup: {(isEnabled ? "✅" : "❌")} | Method: Task Scheduler";
            }
            catch (Exception ex)
            {
                return $"Error checking status: {ex.Message}";
            }
        }

        /// <summary>
        /// Gets detailed startup task information for troubleshooting
        /// </summary>
        public static string GetTaskDetails()
        {
            try
            {
                var arguments = $"/query /tn \"{TASK_NAME}\" /fo LIST /v";
                var result = ExecuteSchedulerCommand(arguments);

                return result.success ? result.output : $"Task not found or error occurred: {result.output}";
            }
            catch (Exception ex)
            {
                return $"Error querying task: {ex.Message}";
            }
        }

        /// <summary>
        /// Tests if the task would run correctly and gets last run result
        /// </summary>
        public static string GetTaskRunInfo()
        {
            try
            {
                var arguments = $"/query /tn \"{TASK_NAME}\" /fo LIST";
                var result = ExecuteSchedulerCommand(arguments);

                if (result.success)
                {
                    return result.output;
                }
                else
                {
                    return $"Error getting task info: {result.output}";
                }
            }
            catch (Exception ex)
            {
                return $"Error getting task info: {ex.Message}";
            }
        }

        /// <summary>
        /// Manually runs the task for testing
        /// </summary>
        public static bool TestTaskExecution()
        {
            try
            {
                Debug.WriteLine("🧪 Testing task execution manually...");
                var arguments = $"/run /tn \"{TASK_NAME}\"";
                var result = ExecuteSchedulerCommand(arguments);

                if (result.success)
                {
                    Debug.WriteLine("✅ Task test execution command successful");

                    // Wait a moment then check if the task actually ran
                    System.Threading.Thread.Sleep(2000);

                    var statusInfo = GetTaskRunInfo();
                    Debug.WriteLine($"Task status after run: {statusInfo}");

                    return true;
                }
                else
                {
                    Debug.WriteLine($"❌ Task test execution failed: {result.output}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Error testing task: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets the exact command line that would be executed
        /// </summary>
        public static string GetExecutablePath()
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                return exePath ?? "Unknown";
            }
            catch
            {
                return "Error getting path";
            }
        }
    }
}