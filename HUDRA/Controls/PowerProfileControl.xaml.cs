using HUDRA.Interfaces;
using HUDRA.AttachedProperties;
using HUDRA.Services;
using HUDRA.Services.Power;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
    public sealed partial class PowerProfileControl : UserControl, INotifyPropertyChanged, IGamepadNavigable
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<PowerProfileChangedEventArgs>? PowerProfileChanged;

        private PowerProfileService? _powerProfileService;
        private ObservableCollection<PowerProfile> _availableProfiles = new();
        private PowerProfile? _selectedProfile;
        private bool _isUpdatingSelection = false;
        private bool _isUpdatingCpuBoost = false;
        private bool _isUpdatingIntelligentSwitching = false;

        // Gamepad navigation fields
        private GamepadNavigationService? _gamepadNavigationService;
        private int _currentFocusedElement = 0; // 0=DefaultProfile, 1=GamingProfile, 2=IntelligentSwitching, 3=CpuBoost, 4=PowerOptionsLink
        private bool _isFocused = false;

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

        // IGamepadNavigable implementation
        public bool CanNavigateUp => _currentFocusedElement > 0;
        public bool CanNavigateDown => _currentFocusedElement < 4;
        public bool CanNavigateLeft => false;
        public bool CanNavigateRight => false;
        public bool CanActivate => true;
        public FrameworkElement NavigationElement => this;

        // Slider interface implementations - PowerProfile has no sliders
        public bool IsSlider => false;
        public bool IsSliderActivated { get; set; } = false;

        // ComboBox interface implementations
        public bool HasComboBoxes => true;
        public bool IsComboBoxOpen { get; set; } = false;
        public ComboBox? GetFocusedComboBox()
        {
            return _currentFocusedElement switch
            {
                0 => DefaultProfileComboBox,
                1 => GamingProfileComboBox,
                _ => null
            };
        }
        public int ComboBoxOriginalIndex { get; set; } = -1;
        public bool IsNavigatingComboBox { get; set; } = false;

        public void ProcessCurrentSelection()
        {
            if ((_currentFocusedElement == 0 || _currentFocusedElement == 1) && !IsNavigatingComboBox)
            {
                // ComboBox selection will be handled by existing event handlers
                System.Diagnostics.Debug.WriteLine($"🎮 PowerProfile: ComboBox selection processed");
            }
        }

        // Focus brush properties for XAML binding
        public Brush DefaultProfileFocusBrush
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true && _currentFocusedElement == 0)
                {
                    return new SolidColorBrush(IsComboBoxOpen ? Microsoft.UI.Colors.DodgerBlue : Microsoft.UI.Colors.DarkViolet);
                }
                return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        public Brush GamingProfileFocusBrush
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true && _currentFocusedElement == 1)
                {
                    return new SolidColorBrush(IsComboBoxOpen ? Microsoft.UI.Colors.DodgerBlue : Microsoft.UI.Colors.DarkViolet);
                }
                return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        public Brush IntelligentSwitchingFocusBrush
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true && _currentFocusedElement == 2)
                {
                    return new SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
                }
                return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        public Brush CpuBoostFocusBrush
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true && _currentFocusedElement == 3)
                {
                    return new SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
                }
                return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        public Brush PowerOptionsLinkFocusBrush
        {
            get
            {
                if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true && _currentFocusedElement == 4)
                {
                    return new SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
                }
                return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            }
        }

        public PowerProfileControl()
        {
            this.InitializeComponent();
            this.DataContext = this;
            InitializeGamepadNavigation();
        }

        private void InitializeGamepadNavigation()
        {
            GamepadNavigation.SetIsEnabled(this, true);
            GamepadNavigation.SetNavigationGroup(this, "MainControls");
            GamepadNavigation.SetNavigationOrder(this, 1);
            // Not directly navigable at page level - only accessible through parent expander
            GamepadNavigation.SetCanNavigate(this, false);
        }

        private void InitializeGamepadNavigationService()
        {
            if (Application.Current is App app && app.MainWindow is MainWindow mainWindow)
            {
                _gamepadNavigationService = mainWindow.GamepadNavigationService;
            }
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

                // Set up both combo boxes
                if (DefaultProfileComboBox != null)
                {
                    DefaultProfileComboBox.ItemsSource = AvailableProfiles;
                    
                    // Load saved default profile
                    var defaultProfileId = SettingsService.GetDefaultPowerProfile();
                    if (defaultProfileId.HasValue)
                    {
                        var defaultProfile = profiles.FirstOrDefault(p => p.Id == defaultProfileId.Value);
                        if (defaultProfile != null)
                        {
                            _isUpdatingSelection = true;
                            DefaultProfileComboBox.SelectedItem = defaultProfile;
                            _isUpdatingSelection = false;
                        }
                    }
                }

                if (GamingProfileComboBox != null)
                {
                    GamingProfileComboBox.ItemsSource = AvailableProfiles;
                    
                    // Load saved gaming profile
                    var gamingProfileId = SettingsService.GetGamingPowerProfile();
                    if (gamingProfileId.HasValue)
                    {
                        var gamingProfile = profiles.FirstOrDefault(p => p.Id == gamingProfileId.Value);
                        if (gamingProfile != null)
                        {
                            _isUpdatingSelection = true;
                            GamingProfileComboBox.SelectedItem = gamingProfile;
                            _isUpdatingSelection = false;
                        }
                    }
                }

                // Load intelligent switching state
                if (IntelligentSwitchingToggle != null)
                {
                    _isUpdatingIntelligentSwitching = true;
                    IntelligentSwitchingToggle.IsOn = SettingsService.GetIntelligentPowerSwitchingEnabled();
                    _isUpdatingIntelligentSwitching = false;
                }

                // Load and restore CPU boost state
                await LoadCpuBoostStateAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load power profiles: {ex.Message}");
            }
        }

        private async void OnDefaultProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingSelection || DefaultProfileComboBox?.SelectedItem is not PowerProfile selectedProfile)
                return;

            try
            {
                _isUpdatingSelection = true;
                
                // Save the default profile setting
                SettingsService.SetDefaultPowerProfile(selectedProfile.Id);
                System.Diagnostics.Debug.WriteLine($"Default power profile set to: {selectedProfile.Name}");

                // If intelligent switching is disabled or no game is active, apply this profile immediately
                if (_powerProfileService != null && 
                    (!_powerProfileService.IsIntelligentSwitchingEnabled || !_powerProfileService.IsGameActive))
                {
                    var success = await _powerProfileService.SetActiveProfileAsync(selectedProfile.Id);
                    if (success)
                    {
                        // Update active state for all profiles
                        foreach (var profile in AvailableProfiles)
                        {
                            profile.IsActive = profile.Id == selectedProfile.Id;
                        }

                        OnPropertyChanged(nameof(AvailableProfiles));
                        PowerProfileChanged?.Invoke(this, new PowerProfileChangedEventArgs(selectedProfile, true));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to set default power profile: {ex.Message}");
            }
            finally
            {
                _isUpdatingSelection = false;
            }
        }

        private async void OnGamingProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingSelection || GamingProfileComboBox?.SelectedItem is not PowerProfile selectedProfile)
                return;

            try
            {
                _isUpdatingSelection = true;
                
                // Save the gaming profile setting
                SettingsService.SetGamingPowerProfile(selectedProfile.Id);
                System.Diagnostics.Debug.WriteLine($"Gaming power profile set to: {selectedProfile.Name}");

                // If intelligent switching is enabled and a game is active, apply this profile immediately
                if (_powerProfileService != null && 
                    _powerProfileService.IsIntelligentSwitchingEnabled && _powerProfileService.IsGameActive)
                {
                    var success = await _powerProfileService.SetActiveProfileAsync(selectedProfile.Id);
                    if (success)
                    {
                        // Update active state for all profiles
                        foreach (var profile in AvailableProfiles)
                        {
                            profile.IsActive = profile.Id == selectedProfile.Id;
                        }

                        OnPropertyChanged(nameof(AvailableProfiles));
                        PowerProfileChanged?.Invoke(this, new PowerProfileChangedEventArgs(selectedProfile, true));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to set gaming power profile: {ex.Message}");
            }
            finally
            {
                _isUpdatingSelection = false;
            }
        }

        private void OnIntelligentSwitchingToggled(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingIntelligentSwitching || _powerProfileService == null || IntelligentSwitchingToggle == null)
                return;

            try
            {
                var enabled = IntelligentSwitchingToggle.IsOn;
                _powerProfileService.SetIntelligentSwitchingEnabled(enabled);
                
                var status = enabled ? "enabled" : "disabled";
                System.Diagnostics.Debug.WriteLine($"Intelligent power switching {status}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to toggle intelligent switching: {ex.Message}");
                
                // Revert toggle state on error
                _isUpdatingIntelligentSwitching = true;
                IntelligentSwitchingToggle.IsOn = !IntelligentSwitchingToggle.IsOn;
                _isUpdatingIntelligentSwitching = false;
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

        // IGamepadNavigable event handlers
        public void OnGamepadNavigateUp()
        {
            // Handle ComboBox navigation when open
            if ((_currentFocusedElement == 0 || _currentFocusedElement == 1) && IsComboBoxOpen)
            {
                var comboBox = GetFocusedComboBox();
                if (comboBox != null)
                {
                    var currentIndex = comboBox.SelectedIndex;
                    if (currentIndex > 0)
                    {
                        comboBox.SelectedIndex = currentIndex - 1;
                        System.Diagnostics.Debug.WriteLine($"🎮 PowerProfile: ComboBox moved up to index {comboBox.SelectedIndex}");
                    }
                }
                return;
            }

            // Normal navigation between elements
            if (_currentFocusedElement > 0)
            {
                _currentFocusedElement--;
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 PowerProfile: Moved up to element {_currentFocusedElement}");
            }
        }

        public void OnGamepadNavigateDown()
        {
            // Handle ComboBox navigation when open
            if ((_currentFocusedElement == 0 || _currentFocusedElement == 1) && IsComboBoxOpen)
            {
                var comboBox = GetFocusedComboBox();
                if (comboBox != null)
                {
                    var currentIndex = comboBox.SelectedIndex;
                    if (currentIndex < comboBox.Items.Count - 1)
                    {
                        comboBox.SelectedIndex = currentIndex + 1;
                        System.Diagnostics.Debug.WriteLine($"🎮 PowerProfile: ComboBox moved down to index {comboBox.SelectedIndex}");
                    }
                }
                return;
            }

            // Normal navigation between elements
            if (_currentFocusedElement < 4)
            {
                _currentFocusedElement++;
                UpdateFocusVisuals();
                System.Diagnostics.Debug.WriteLine($"🎮 PowerProfile: Moved down to element {_currentFocusedElement}");
            }
        }

        public void OnGamepadNavigateLeft()
        {
            // No left navigation in PowerProfile control
        }

        public void OnGamepadNavigateRight()
        {
            // No right navigation in PowerProfile control
        }

        public void OnGamepadActivate()
        {
            switch (_currentFocusedElement)
            {
                case 0: // DefaultProfileComboBox
                case 1: // GamingProfileComboBox
                    var comboBox = GetFocusedComboBox();
                    if (comboBox != null)
                    {
                        if (!IsComboBoxOpen)
                        {
                            // Open ComboBox
                            ComboBoxOriginalIndex = comboBox.SelectedIndex;
                            comboBox.IsDropDownOpen = true;
                            IsComboBoxOpen = true;
                            IsNavigatingComboBox = true;
                            UpdateFocusVisuals();
                            System.Diagnostics.Debug.WriteLine($"🎮 PowerProfile: Opened ComboBox {_currentFocusedElement}");
                        }
                        else
                        {
                            // Close ComboBox and apply selection
                            comboBox.IsDropDownOpen = false;
                            IsComboBoxOpen = false;
                            IsNavigatingComboBox = false;
                            ProcessCurrentSelection();
                            UpdateFocusVisuals();
                            System.Diagnostics.Debug.WriteLine($"🎮 PowerProfile: Closed ComboBox {_currentFocusedElement} and applied selection");
                        }
                    }
                    break;

                case 2: // IntelligentSwitchingToggle
                    if (IntelligentSwitchingToggle != null)
                    {
                        IntelligentSwitchingToggle.IsOn = !IntelligentSwitchingToggle.IsOn;
                        System.Diagnostics.Debug.WriteLine($"🎮 PowerProfile: Toggled IntelligentSwitching to {IntelligentSwitchingToggle.IsOn}");
                    }
                    break;

                case 3: // CpuBoostToggle
                    if (CpuBoostToggle != null)
                    {
                        CpuBoostToggle.IsOn = !CpuBoostToggle.IsOn;
                        System.Diagnostics.Debug.WriteLine($"🎮 PowerProfile: Toggled CpuBoost to {CpuBoostToggle.IsOn}");
                    }
                    break;

                case 4: // PowerOptionsLink
                    OnPowerOptionsLinkClick(PowerOptionsLink, new RoutedEventArgs());
                    System.Diagnostics.Debug.WriteLine($"🎮 PowerProfile: Activated PowerOptionsLink");
                    break;
            }
        }

        public void OnGamepadFocusReceived()
        {
            // Initialize gamepad service if needed
            if (_gamepadNavigationService == null)
            {
                InitializeGamepadNavigationService();
            }

            _currentFocusedElement = 0; // Start with DefaultProfileComboBox
            _isFocused = true;
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 PowerProfile: Received gamepad focus");
        }

        public void OnGamepadFocusLost()
        {
            _isFocused = false;
            IsComboBoxOpen = false;
            IsNavigatingComboBox = false;
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 PowerProfile: Lost gamepad focus");
        }

        public void FocusLastElement()
        {
            // Focus the last element (element 4: AC Power Profile)
            _currentFocusedElement = 4;
            _isFocused = true;
            UpdateFocusVisuals();
            System.Diagnostics.Debug.WriteLine($"🎮 PowerProfile: Focused last element (AC Power Profile)");
        }

        public void AdjustSliderValue(int direction)
        {
            // No sliders in PowerProfile control
        }

        private void UpdateFocusVisuals()
        {
            // Dispatch on UI thread to ensure bindings update reliably with gamepad navigation
            DispatcherQueue.TryEnqueue(() =>
            {
                OnPropertyChanged(nameof(DefaultProfileFocusBrush));
                OnPropertyChanged(nameof(GamingProfileFocusBrush));
                OnPropertyChanged(nameof(IntelligentSwitchingFocusBrush));
                OnPropertyChanged(nameof(CpuBoostFocusBrush));
                OnPropertyChanged(nameof(PowerOptionsLinkFocusBrush));
            });
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