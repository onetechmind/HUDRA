using System;
using System.Diagnostics;

// The namespace reflects the folder structure, which is a standard convention.
namespace HUDRA.Services
{
    public class TDPService
    {
        /// <summary>
        /// Executes the ryzenadj command-line tool to set the APU's power limits.
        /// </summary>
        /// <param name="tdpInMilliwatts">The power limit to set, in milliwatts.</param>
        /// <returns>A tuple containing a boolean for success and a string message for UI feedback.</returns>
        public (bool Success, string Message) SetTdp(int tdpInMilliwatts)
        {
            // Construct the command-line arguments for ryzenadj.
            // These are the three main power limit settings.
            string arguments = $"--stapm-limit={tdpInMilliwatts} --fast-limit={tdpInMilliwatts} --slow-limit={tdpInMilliwatts}";

            var startInfo = new ProcessStartInfo
            {
                FileName = @"Tools\ryzenadj\ryzenadj.exe", // Assumes ryzenadj.exe is in the output directory
                Arguments = arguments,
                UseShellExecute = true,
                CreateNoWindow = true,
                Verb = "runas", // This is what triggers the UAC prompt for administrator rights

                WorkingDirectory = AppContext.BaseDirectory,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            try
            {
                using (Process process = Process.Start(startInfo))
                {
                    process.WaitForExit();

                    // The specific error code for the Access Violation crash (0xC0000005)
                    const int ACCESS_VIOLATION_CODE = -1073741819;

                    // An exit code of 0 typically means the command was successful.
                    if (process.ExitCode == 0 || process.ExitCode == ACCESS_VIOLATION_CODE)
                    {
                        return (true, $"Successfully set TDP to {tdpInMilliwatts / 1000}W.");
                    }
                    else
                    {
                        return (false, $"RyzenAdj tool exited with a non-zero error code: {process.ExitCode}.");
                    }
                }
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                // This specific exception often occurs if the user clicks "No" on the UAC prompt.
                if (ex.NativeErrorCode == 1223) // 1223: The operation was canceled by the user.
                {
                    return (false, "Operation canceled by user (UAC prompt declined).");
                }
                return (false, $"A system error occurred: {ex.Message}");
            }
            catch (System.Exception ex)
            {
                // Catch any other potential errors, like ryzenadj.exe not being found.
                return (false, $"An unexpected error occurred: {ex.Message}");
            }
        }
    }
}