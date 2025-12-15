using System.Collections.Generic;

namespace HUDRA.Models
{
    /// <summary>
    /// Detailed feedback on profile application, tracking success/failure for each setting.
    /// </summary>
    public class ProfileApplicationResult
    {
        public bool OverallSuccess { get; set; } = true;
        public Dictionary<string, bool> SettingResults { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public string? ActiveProfileProcessName { get; set; }

        public void AddResult(string settingName, bool success, string? errorMessage = null)
        {
            SettingResults[settingName] = success;
            if (!success)
            {
                OverallSuccess = false;
                if (!string.IsNullOrEmpty(errorMessage))
                {
                    Errors.Add($"{settingName}: {errorMessage}");
                }
            }
        }
    }
}
