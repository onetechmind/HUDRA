using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using HUDRA.Models;

namespace HUDRA.Services
{
    public class LosslessScalingSettingsService
    {
        private const string SETTINGS_PATH = @"%LOCALAPPDATA%\Lossless Scaling\Settings.xml";
        private const string BACKUP_SUFFIX = ".hudra.backup";
        private const string PROCESS_NAME = "LosslessScaling";
        private const string TEMPLATE_PATH = @"Configuration\Settings.xml";
        
        private readonly LosslessScalingService _losslessScalingService;
        
        public LosslessScalingSettingsService(LosslessScalingService losslessScalingService)
        {
            _losslessScalingService = losslessScalingService;
        }
        
        public async Task<bool> ApplySettingsAndRestartAsync(LosslessScalingSettings settings)
        {
            try
            {
                var settingsPath = Environment.ExpandEnvironmentVariables(SETTINGS_PATH);
                var backupPath = settingsPath + BACKUP_SUFFIX;
                
                // Step 1: Terminate Lossless Scaling if running
                if (!await TerminateLosslessScalingAsync())
                {
                    return false;
                }
                
                // Step 2: Backup existing settings
                if (!await BackupUserSettingsAsync())
                {
                    return false;
                }
                
                // Step 3: Create new settings file with user selections
                if (!await CreateAndApplySettingsAsync(settings))
                {
                    // Auto-restore on failure
                    await RestoreUserSettingsAsync();
                    return false;
                }
                
                // Step 4: Start Lossless Scaling
                if (!await StartLosslessScalingAsync())
                {
                    // Auto-restore on failure
                    await RestoreUserSettingsAsync();
                    return false;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ApplySettingsAndRestartAsync: {ex.Message}");
                // Attempt to restore backup on any error
                await RestoreUserSettingsAsync();
                return false;
            }
        }
        
        public async Task<bool> BackupUserSettingsAsync()
        {
            try
            {
                var settingsPath = Environment.ExpandEnvironmentVariables(SETTINGS_PATH);
                var backupPath = settingsPath + BACKUP_SUFFIX;
                
                if (File.Exists(settingsPath))
                {
                    await Task.Run(() => File.Copy(settingsPath, backupPath, overwrite: true));
                    System.Diagnostics.Debug.WriteLine($"Backed up settings to: {backupPath}");
                    return true;
                }
                
                System.Diagnostics.Debug.WriteLine("No existing settings file to backup");
                return true; // Not an error if no file exists yet
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error backing up settings: {ex.Message}");
                return false;
            }
        }
        
        public async Task<bool> RestoreUserSettingsAsync()
        {
            try
            {
                var settingsPath = Environment.ExpandEnvironmentVariables(SETTINGS_PATH);
                var backupPath = settingsPath + BACKUP_SUFFIX;
                
                if (File.Exists(backupPath))
                {
                    // Terminate LS first
                    await TerminateLosslessScalingAsync();
                    
                    // Restore backup
                    await Task.Run(() => File.Copy(backupPath, settingsPath, overwrite: true));
                    System.Diagnostics.Debug.WriteLine($"Restored settings from backup: {backupPath}");
                    
                    // Restart LS
                    await StartLosslessScalingAsync();
                    return true;
                }
                
                System.Diagnostics.Debug.WriteLine("No backup file found to restore");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error restoring settings: {ex.Message}");
                return false;
            }
        }
        
        public async Task<bool> TerminateLosslessScalingAsync()
        {
            try
            {
                var processes = Process.GetProcessesByName(PROCESS_NAME);
                if (processes.Length == 0)
                {
                    return true; // Already not running
                }
                
                foreach (var process in processes)
                {
                    try
                    {
                        // Try graceful close first
                        process.CloseMainWindow();
                        
                        // Wait up to 5 seconds for graceful shutdown
                        bool exited = process.WaitForExit(3000);
                        
                        if (!exited && !process.HasExited)
                        {
                            // Force kill if graceful didn't work
                            process.Kill();
                            process.WaitForExit(3000);
                        }
                        
                        process.Dispose();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error terminating LS process: {ex.Message}");
                    }
                }
                
                // Wait a moment for cleanup
                await Task.Delay(1000);
                
                // Verify termination
                var remainingProcesses = Process.GetProcessesByName(PROCESS_NAME);
                bool success = remainingProcesses.Length == 0;
                
                foreach (var process in remainingProcesses)
                {
                    process.Dispose();
                }
                
                System.Diagnostics.Debug.WriteLine($"LS termination result: {success}");
                return success;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error terminating Lossless Scaling: {ex.Message}");
                return false;
            }
        }
        
        public async Task<bool> StartLosslessScalingAsync()
        {
            try
            {
                // Try to find LS executable in common locations
                string lsPath = FindLosslessScalingExecutable();
                if (string.IsNullOrEmpty(lsPath))
                {
                    System.Diagnostics.Debug.WriteLine("Could not find Lossless Scaling executable");
                    return false;
                }
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = lsPath,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    WorkingDirectory = Path.GetDirectoryName(lsPath)
                };
                
                var process = Process.Start(startInfo);
                if (process == null)
                {
                    return false;
                }
                
                // Give LS time to start up
                await Task.Delay(2000);
                
                // Verify it's running
                bool isRunning = _losslessScalingService.IsLosslessScalingRunning();
                System.Diagnostics.Debug.WriteLine($"LS start result: {isRunning}");
                
                return isRunning;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting Lossless Scaling: {ex.Message}");
                return false;
            }
        }
        
        private async Task<bool> CreateAndApplySettingsAsync(LosslessScalingSettings settings)
        {
            try
            {
                var settingsPath = Environment.ExpandEnvironmentVariables(SETTINGS_PATH);
                var templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, TEMPLATE_PATH);
                
                if (!File.Exists(templatePath))
                {
                    System.Diagnostics.Debug.WriteLine($"Template file not found: {templatePath}");
                    return false;
                }
                
                // Load template
                var doc = await Task.Run(() => XDocument.Load(templatePath));
                if (doc.Root == null)
                {
                    return false;
                }
                
                // Preserve user's hotkey settings from existing file if it exists
                if (File.Exists(settingsPath))
                {
                    try
                    {
                        var (hotkey, modifiers) = _losslessScalingService.ParseHotkeyFromSettings();
                        var hotkeyElement = doc.Root.Element("Hotkey");
                        var modifiersElement = doc.Root.Element("HotkeyModifierKeys");
                        
                        if (hotkeyElement != null)
                            hotkeyElement.Value = hotkey;
                        if (modifiersElement != null)
                            modifiersElement.Value = modifiers;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Warning: Could not preserve hotkeys: {ex.Message}");
                    }
                }
                
                // Find the Default profile and apply settings
                var defaultProfile = doc.Root
                    .Element("GameProfiles")?
                    .Elements("Profile")?
                    .FirstOrDefault(p => p.Element("Title")?.Value == "Default");
                
                if (defaultProfile == null)
                {
                    System.Diagnostics.Debug.WriteLine("Default profile not found in template");
                    return false;
                }
                
                // Apply user settings to Default profile
                SetElementValue(defaultProfile, "ScalingType", settings.GetScalingTypeXmlValue());
                SetElementValue(defaultProfile, "FrameGeneration", settings.GetFrameGenXmlValue());
                SetElementValue(defaultProfile, "LSFG3Multiplier", settings.GetFrameGenMultiplierXmlValue());
                SetElementValue(defaultProfile, "LSFGFlowScale", settings.FlowScale.ToString());
                
                // Save the modified settings
                await Task.Run(() => doc.Save(settingsPath));
                
                System.Diagnostics.Debug.WriteLine($"Applied settings: Upscaling={settings.UpscalingEnabled}, FrameGen={settings.FrameGenMultiplier}, FlowScale={settings.FlowScale}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating settings file: {ex.Message}");
                return false;
            }
        }
        
        private void SetElementValue(XElement parent, string elementName, string value)
        {
            var element = parent.Element(elementName);
            if (element != null)
            {
                element.Value = value;
            }
            else
            {
                parent.Add(new XElement(elementName, value));
            }
        }
        
        private string FindLosslessScalingExecutable()
        {
            // Common installation paths for Lossless Scaling
            var commonPaths = new[]
            {
                @"C:\Program Files\Lossless Scaling\LosslessScaling.exe",
                @"C:\Program Files (x86)\Lossless Scaling\LosslessScaling.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Lossless Scaling\LosslessScaling.exe")
            };
            
            foreach (var path in commonPaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }
            
            // Try to find it in Steam library (common location)
            try
            {
                var steamPaths = new[]
                {
                    @"C:\Program Files (x86)\Steam\steamapps\common\Lossless Scaling\LosslessScaling.exe",
                    @"C:\Program Files\Steam\steamapps\common\Lossless Scaling\LosslessScaling.exe"
                };
                
                foreach (var path in steamPaths)
                {
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error searching Steam paths: {ex.Message}");
            }
            
            return "";
        }
        
        public LosslessScalingSettings? GetCurrentSettings()
        {
            try
            {
                var settingsPath = Environment.ExpandEnvironmentVariables(SETTINGS_PATH);
                if (!File.Exists(settingsPath))
                {
                    return null;
                }
                
                var doc = XDocument.Load(settingsPath);
                var defaultProfile = doc.Root?
                    .Element("GameProfiles")?
                    .Elements("Profile")?
                    .FirstOrDefault(p => p.Element("Title")?.Value == "Default");
                
                if (defaultProfile == null)
                {
                    return null;
                }
                
                var scalingType = defaultProfile.Element("ScalingType")?.Value ?? "LS1";
                var frameGeneration = defaultProfile.Element("FrameGeneration")?.Value ?? "Off";
                var lsfg3Multiplier = defaultProfile.Element("LSFG3Multiplier")?.Value ?? "2";
                var flowScaleStr = defaultProfile.Element("LSFGFlowScale")?.Value ?? "70";
                
                if (int.TryParse(flowScaleStr, out int flowScale))
                {
                    return LosslessScalingSettings.FromXml(scalingType, frameGeneration, lsfg3Multiplier, flowScale);
                }
                
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading current settings: {ex.Message}");
                return null;
            }
        }
    }
}