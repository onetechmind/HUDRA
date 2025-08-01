using HUDRA.Services;
using HUDRA.Services.Power;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace HUDRA.Controls
{
    public sealed partial class PowerProfileControl : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<PowerProfileChangedEventArgs>? PowerProfileChanged;

        private PowerProfileService? _powerProfileService;
        private ObservableCollection<PowerProfile> _availableProfiles = new();
        private PowerProfile? _selectedProfile;
        private bool _isUpdatingSelection = false;
        private bool _isUpdatingCpuBoost = false;

        public ObservableCollection<PowerProfile> AvailableProfiles
        {
            get => _availableProfiles;
            set
            {
                if (_availableProfiles != value)
                {
                    _availableProfiles = value;
                    OnPropertyChanged();
                }
            }
        }

        public PowerProfile? SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (_selectedProfile != value && !_isUpdatingSelection)
                {
                    _selectedProfile = value;
                    OnPropertyChanged();
                }
            }
        }

        public PowerProfileControl()
        {
            this.InitializeComponent();
            this.DataContext = this;
        }

        public async Task InitializeAsync()
        {
            try
            {
                _powerProfileService = new PowerProfileService();
                await LoadPowerProfilesAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize PowerProfileControl: {ex.Message}");
            }
        }

        private async Task LoadPowerProfilesAsync()
        {
            try
            {
                if (_powerProfileService == null) return;

                var profiles = await _powerProfileService.GetAvailableProfilesAsync();
                AvailableProfiles = new ObservableCollection<PowerProfile>(profiles);

                if (ProfileComboBox != null)
                {
                    ProfileComboBox.ItemsSource = AvailableProfiles;
                    
                    // Set current active profile as selected
                    var activeProfile = profiles.FirstOrDefault(p => p.IsActive);
                    if (activeProfile != null)
                    {
                        _isUpdatingSelection = true;
                        SelectedProfile = activeProfile;
                        ProfileComboBox.SelectedItem = activeProfile;
                        _isUpdatingSelection = false;
                    }
                }

                // Load and restore CPU boost state
                await LoadCpuBoostStateAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load power profiles: {ex.Message}");
            }
        }

        private async void OnProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingSelection || ProfileComboBox?.SelectedItem is not PowerProfile selectedProfile)
                return;

            try
            {
                _isUpdatingSelection = true;
                SelectedProfile = selectedProfile;

                if (_powerProfileService != null)
                {
                    var success = await _powerProfileService.SetActiveProfileAsync(selectedProfile.Id);
                    if (success)
                    {
                        // Update active state for all profiles
                        foreach (var profile in AvailableProfiles)
                        {
                            profile.IsActive = profile.Id == selectedProfile.Id;
                        }

                        // Trigger property change notifications for UI updates
                        OnPropertyChanged(nameof(AvailableProfiles));

                        PowerProfileChanged?.Invoke(this, new PowerProfileChangedEventArgs(selectedProfile, true));

                        // Load CPU boost state for the new profile
                        await LoadCpuBoostStateAsync();
                    }
                    else
                    {
                        // Reset selection on failure
                        var activeProfile = AvailableProfiles.FirstOrDefault(p => p.IsActive);
                        if (activeProfile != null)
                        {
                            SelectedProfile = activeProfile;
                            ProfileComboBox.SelectedItem = activeProfile;
                        }

                        PowerProfileChanged?.Invoke(this, new PowerProfileChangedEventArgs(selectedProfile, false));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to change power profile: {ex.Message}");
                PowerProfileChanged?.Invoke(this, new PowerProfileChangedEventArgs(selectedProfile, false));
            }
            finally
            {
                _isUpdatingSelection = false;
            }
        }

        public async Task RefreshProfilesAsync()
        {
            await LoadPowerProfilesAsync();
        }

        private async Task LoadCpuBoostStateAsync()
        {
            try
            {
                if (_powerProfileService == null || CpuBoostToggle == null) return;

                _isUpdatingCpuBoost = true;

                // Get user's preferred setting from SettingsService
                var preferredState = SettingsService.GetCpuBoostEnabled();
                
                // Apply the preferred setting to Windows if restoration is enabled
                if (SettingsService.GetRestoreCpuBoostOnStartup())
                {
                    await _powerProfileService.SetCpuBoostEnabledAsync(preferredState);
                }
                
                // Set toggle to match the preferred setting
                CpuBoostToggle.IsOn = preferredState;
                _isUpdatingCpuBoost = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load CPU boost state: {ex.Message}");
                _isUpdatingCpuBoost = false;
            }
        }

        private async void OnCpuBoostToggled(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingCpuBoost || _powerProfileService == null || CpuBoostToggle == null)
                return;

            try
            {
                var success = await _powerProfileService.SetCpuBoostEnabledAsync(CpuBoostToggle.IsOn);
                if (!success)
                {
                    // Revert toggle state on failure
                    _isUpdatingCpuBoost = true;
                    CpuBoostToggle.IsOn = !CpuBoostToggle.IsOn;
                    _isUpdatingCpuBoost = false;
                    
                    System.Diagnostics.Debug.WriteLine("Failed to change CPU boost setting");
                }
                else
                {
                    // Save user preference on successful change
                    SettingsService.SetCpuBoostEnabled(CpuBoostToggle.IsOn);
                    
                    var state = CpuBoostToggle.IsOn ? "enabled" : "disabled";
                    System.Diagnostics.Debug.WriteLine($"CPU boost {state} successfully");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to toggle CPU boost: {ex.Message}");
                
                // Revert toggle state on error
                _isUpdatingCpuBoost = true;
                CpuBoostToggle.IsOn = !CpuBoostToggle.IsOn;
                _isUpdatingCpuBoost = false;
            }
        }

        private void OnPowerOptionsLinkClick(object sender, RoutedEventArgs e)
        {
            try
            {
                // Try to open Control Panel Power Options directly
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powercfg.cpl",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to open power options: {ex.Message}");
                
                // Fallback: try to open Windows Settings Power Options page
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "ms-settings:poweroptions",
                        UseShellExecute = true
                    });
                }
                catch (Exception fallbackEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Fallback also failed: {fallbackEx.Message}");
                }
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class PowerProfileChangedEventArgs : EventArgs
    {
        public PowerProfile Profile { get; }
        public bool IsApplied { get; }

        public PowerProfileChangedEventArgs(PowerProfile profile, bool isApplied)
        {
            Profile = profile;
            IsApplied = isApplied;
        }
    }
}