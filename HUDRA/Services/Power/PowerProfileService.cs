using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace HUDRA.Services.Power
{
    public class PowerProfileService
    {
        private readonly List<string> _builtInProfileNames = new()
        {
            "Balanced",
            "High performance", 
            "Power saver",
            "Ultimate Performance"
        };

        private readonly List<string> _manufacturerKeywords = new()
        {
            "ASUS", "MSI", "Gaming", "Turbo", "Silent", "Performance", "Eco"
        };

        public async Task<List<PowerProfile>> GetAvailableProfilesAsync()
        {
            try
            {
                var profiles = new List<PowerProfile>();
                var output = await ExecutePowerCfgCommandAsync("/list");
                
                if (string.IsNullOrEmpty(output))
                    return profiles;

                var activeProfileId = await GetActiveProfileIdAsync();
                profiles.AddRange(ParsePowerProfiles(output, activeProfileId));
                
                return profiles;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get power profiles: {ex.Message}");
                return new List<PowerProfile>();
            }
        }

        public async Task<bool> SetActiveProfileAsync(Guid profileId)
        {
            try
            {
                var output = await ExecutePowerCfgCommandAsync($"/setactive {profileId:D}");
                return !output.Contains("error", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to set active profile: {ex.Message}");
                return false;
            }
        }

        public async Task<PowerProfile?> GetActiveProfileAsync()
        {
            try
            {
                var profiles = await GetAvailableProfilesAsync();
                return profiles.FirstOrDefault(p => p.IsActive);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get active profile: {ex.Message}");
                return null;
            }
        }

        private async Task<string> ExecutePowerCfgCommandAsync(string arguments)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "powercfg.exe",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process == null)
                    return string.Empty;

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                
                await process.WaitForExitAsync();

                if (process.ExitCode != 0 && !string.IsNullOrEmpty(error))
                {
                    throw new InvalidOperationException($"PowerCfg failed: {error}");
                }

                return output;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PowerCfg execution failed: {ex.Message}");
                throw;
            }
        }

        private async Task<Guid?> GetActiveProfileIdAsync()
        {
            try
            {
                var output = await ExecutePowerCfgCommandAsync("/getactivescheme");
                
                var match = Regex.Match(output, @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
                if (match.Success && Guid.TryParse(match.Value, out var activeId))
                {
                    return activeId;
                }
                
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get active profile ID: {ex.Message}");
                return null;
            }
        }

        private List<PowerProfile> ParsePowerProfiles(string output, Guid? activeProfileId)
        {
            var profiles = new List<PowerProfile>();
            
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("Existing", StringComparison.OrdinalIgnoreCase))
                    continue;

                var match = Regex.Match(trimmedLine, @"Power Scheme GUID:\s*([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s*\((.+?)\)(?:\s*\*)?$");
                
                if (match.Success)
                {
                    if (Guid.TryParse(match.Groups[1].Value, out var profileId))
                    {
                        var name = match.Groups[2].Value.Trim();
                        var profile = new PowerProfile
                        {
                            Id = profileId,
                            Name = name,
                            IsActive = activeProfileId.HasValue && profileId == activeProfileId.Value,
                            Type = ClassifyProfileType(name),
                            Description = name
                        };
                        
                        profiles.Add(profile);
                    }
                }
            }
            
            return profiles;
        }

        private PowerProfileType ClassifyProfileType(string profileName)
        {
            if (_builtInProfileNames.Any(builtin => 
                string.Equals(profileName, builtin, StringComparison.OrdinalIgnoreCase)))
            {
                return PowerProfileType.WindowsBuiltIn;
            }

            if (_manufacturerKeywords.Any(keyword => 
                profileName.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                return PowerProfileType.ManufacturerCustom;
            }

            if (_builtInProfileNames.Any(builtin => 
                profileName.Contains(builtin, StringComparison.OrdinalIgnoreCase)))
            {
                return PowerProfileType.WindowsBuiltIn;
            }

            return PowerProfileType.UserCreated;
        }

        // CPU Boost Control Methods
        public async Task<bool> GetCpuBoostEnabledAsync()
        {
            try
            {
                var output = await ExecutePowerCfgCommandAsync("/query SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE");
                
                // Look for both AC and DC values in the output
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                bool acEnabled = false, dcEnabled = false;
                
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains("Current AC Power Setting Index:", StringComparison.OrdinalIgnoreCase))
                    {
                        var valueLine = lines[i].Trim();
                        var match = Regex.Match(valueLine, @"0x([0-9a-fA-F]+)");
                        if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.HexNumber, null, out var value))
                        {
                            acEnabled = value > 0;
                        }
                    }
                    else if (lines[i].Contains("Current DC Power Setting Index:", StringComparison.OrdinalIgnoreCase))
                    {
                        var valueLine = lines[i].Trim();
                        var match = Regex.Match(valueLine, @"0x([0-9a-fA-F]+)");
                        if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.HexNumber, null, out var value))
                        {
                            dcEnabled = value > 0;
                        }
                    }
                }
                
                // Return true if both AC and DC are enabled
                return acEnabled && dcEnabled;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get CPU boost status: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SetCpuBoostEnabledAsync(bool enabled)
        {
            try
            {
                var boostValue = enabled ? "5" : "0"; // 5 = Aggressive At Guaranteed, 0 = Disabled
                
                // Set the boost mode for both AC (plugged in) and DC (on battery)
                var setAcOutput = await ExecutePowerCfgCommandAsync($"/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE {boostValue}");
                var setDcOutput = await ExecutePowerCfgCommandAsync($"/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE {boostValue}");
                
                // Apply the changes
                var applyOutput = await ExecutePowerCfgCommandAsync("/setactive SCHEME_CURRENT");
                
                var success = !setAcOutput.Contains("error", StringComparison.OrdinalIgnoreCase) && 
                             !setDcOutput.Contains("error", StringComparison.OrdinalIgnoreCase) &&
                             !applyOutput.Contains("error", StringComparison.OrdinalIgnoreCase);
                             
                if (success)
                {
                    var state = enabled ? "enabled" : "disabled";
                    System.Diagnostics.Debug.WriteLine($"CPU boost {state} for both AC and DC power");
                }
                
                return success;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to set CPU boost: {ex.Message}");
                return false;
            }
        }
    }
}