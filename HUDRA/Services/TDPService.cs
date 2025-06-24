using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace HUDRA.Services
{
    public class TDPService
    {
        private string GetRyzenAdjPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "ryzenadj", "ryzenadj.exe");
        }

        public (bool Success, int TdpWatts, string Message) GetCurrentTdp()
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

                process.WaitForExit(5000); // 5 second timeout

                if (process.ExitCode != 0)
                {
                    var error = process.StandardError.ReadToEnd();
                    return (false, 0, $"RyzenAdj error: {error}");
                }

                var output = process.StandardOutput.ReadToEnd();

                // Parse the STAPM LIMIT value from the table
                // Looking for pattern like "| STAPM LIMIT         |    15.000 |"
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

        public (bool Success, string Message) SetTdp(int tdpInMilliwatts)
        {
            try
            {
                var ryzenAdjPath = GetRyzenAdjPath();
                if (!File.Exists(ryzenAdjPath))
                {
                    return (false, "RyzenAdj not found");
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

                process.WaitForExit(5000); // 5 second timeout
                const int ACCESS_VIOLATION_CODE = -1073741819;
                if (process.ExitCode == 0 || process.ExitCode == ACCESS_VIOLATION_CODE)
                {
                    return (true, $"TDP set to {tdpWatts}W successfully");
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
    }
}