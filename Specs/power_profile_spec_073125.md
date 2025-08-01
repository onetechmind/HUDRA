# HUDRA Power Profile Integration Specification

## Overview
Add Windows power profile control to HUDRA, allowing users to switch between available power schemes (Balanced, High Performance, manufacturer-specific profiles, and user-created profiles) directly from the HUDRA interface.

## Requirements

### Functional Requirements
- **FR-1**: Dynamically detect all available Windows power profiles on the system
- **FR-2**: Display power profiles in a ComboBox control on the main interface
- **FR-3**: Allow users to switch between power profiles with immediate effect
- **FR-4**: Show the currently active power profile as selected
- **FR-5**: Persist user's preferred power profile in settings using existing Settings Service
- **FR-6**: Handle elevation requirements gracefully (app already requires admin priveleges for other features)

### Non-Functional Requirements
- **NFR-1**: Profile detection must complete within 2 seconds
- **NFR-2**: Profile switching must complete within 1 second
- **NFR-3**: UI must follow existing HUDRA design patterns and styling
- **NFR-4**: Service must integrate with existing MVVM architecture
- **NFR-5**: Error handling must be consistent with existing TDP error patterns

## Technical Architecture

### New Components to Create

#### 1. Models
```
HUDRA/Services/Power/PowerProfile.cs
```
- Properties: Id (Guid), Name (string), IsActive (bool), Type (enum), Description (string)
- PowerProfileType enum: WindowsBuiltIn, ManufacturerCustom, UserCreated, Unknown

#### 2. Service Layer
```
HUDRA/Services/Power/PowerProfileService.cs
```
- GetAvailableProfilesAsync(): List<PowerProfile>
- SetActiveProfileAsync(Guid profileId): bool
- GetActiveProfileAsync(): PowerProfile
- Private methods for parsing powercfg output

#### 3. UI Control
```
HUDRA/Controls/PowerProfileControl.xaml
HUDRA/Controls/PowerProfileControl.xaml.cs
```
- ComboBox with proper styling matching existing controls
- Header, selection handling, visual states

#### 4. MainWindow Integration
```
HUDRA/MainWindow.xaml.cs (extend existing)
```
- Add properties: AvailableProfiles, SelectedProfile
- Add PowerProfileService field and initialization
- Integration with existing INotifyPropertyChanged implementation

#### 5. Settings Extension
```
HUDRA/Services/SettingsService.cs (extend existing)
```
- Add methods: GetPreferredPowerProfile(), SetPreferredPowerProfile(Guid)
- Add methods: GetRestorePowerProfileOnStartup(), SetRestorePowerProfileOnStartup(bool)
- Add private constants for new setting keys

### Implementation Approach

#### Step 1: Create PowerProfile Model
```csharp
public class PowerProfile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public PowerProfileType Type { get; set; }
    public string Description { get; set; } = string.Empty;
}

public enum PowerProfileType
{
    WindowsBuiltIn,
    ManufacturerCustom,
    UserCreated,
    Unknown
}
```

#### Step 2: Implement PowerProfileService
- Use `Process.Start()` to execute `powercfg /list` and `powercfg /setactive`
- Parse powercfg output using regex patterns
- Handle admin elevation requirements
- Implement profile type classification logic
- Add error handling consistent with TDPService patterns

#### Step 3: Create PowerProfileControl
- Follow existing control patterns (similar to ResolutionComboBox)
- Use ComboBox
- Implement proper styling with existing theme

#### Step 4: Integrate with MainWindow
- Add new properties for profiles and selection
- Wire up to existing INotifyPropertyChanged implementation
- Add profile loading to existing initialization sequence

#### Step 5: UI Integration
- Add PowerProfileControl to bottom of SettingsPage.xaml
- Ensure responsive layout on different screen sizes
- Test touch interaction on handheld devices

#### Step 6: Settings Integration
- Extend existing settings.json structure
- Implement startup profile restoration
- Add to existing settings persistence logic

## File Modifications Required

### New Files
- `HUDRA/Services/Power/PowerProfile.cs`
- `HUDRA/Services/Power/PowerProfileService.cs`
- `HUDRA/Controls/PowerProfileControl.xaml`
- `HUDRA/Controls/PowerProfileControl.xaml.cs`

### Files to Modify
- `HUDRA/MainWindow.xaml.cs` - Add power profile properties, service initialization, and event handling
- `HUDRA/Services/SettingsService.cs` - Add power profile preference methods and setting keys
- `HUDRA/Pages/SettingsPage.xaml` - Add PowerProfileControl to UI
- `HUDRA/Services/SettingsService.cs` - Handle new settings properties

## UI Specifications

### PowerProfileControl Layout
```xml
<UserControl>
    <StackPanel Spacing="8">
        <TextBlock Text="Power Profile" 
                   Style="{StaticResource ControlHeaderTextStyle}"/>
        <ComboBox x:Name="ProfileComboBox"
                  MinWidth="200"
                  ItemsSource="{x:Bind AvailableProfiles, Mode=OneWay}"
                  SelectedItem="{x:Bind SelectedProfile, Mode=TwoWay}"
                  DisplayMemberPath="Name">
            <ComboBox.ItemTemplate>
                <DataTemplate x:DataType="local:PowerProfile">
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <FontIcon Glyph="{x:Bind TypeIcon}" FontSize="14"/>
                        <TextBlock Text="{x:Bind Name}"/>
                        <TextBlock Text="(Active)" 
                                   Visibility="{x:Bind IsActiveVisibility}"
                                   Foreground="{ThemeResource AccentTextFillColorPrimaryBrush}"
                                   FontSize="12"/>
                    </StackPanel>
                </DataTemplate>
            </ComboBox.ItemTemplate>
        </ComboBox>
    </StackPanel>
</UserControl>
```

### MainWindow Integration Pattern
Following the existing pattern in `MainWindow.xaml.cs`:

```csharp
public sealed partial class MainWindow : Window, INotifyPropertyChanged
{
    // Add alongside existing service fields
    private readonly PowerProfileService _powerProfileService;
    
    // Add alongside existing properties (like BatteryPercentageText)
    private ObservableCollection<PowerProfile> _availableProfiles = new();
    public ObservableCollection<PowerProfile> AvailableProfiles
    {
        get => _availableProfiles;
        set { _availableProfiles = value; OnPropertyChanged(); }
    }

    private PowerProfile? _selectedProfile;
    public PowerProfile? SelectedProfile
    {
        get => _selectedProfile;
        set 
        { 
            if (_selectedProfile != value)
            {
                _selectedProfile = value;
                OnPropertyChanged();
                _ = OnProfileSelectionChanged(value);
            }
        }
    }

    public MainWindow()
    {
        // Initialize service alongside existing services
        _powerProfileService = new PowerProfileService();
        
        // Rest of existing initialization...
        InitializeWindow();
        
        // Load profiles after window initialization
        _ = LoadPowerProfilesAsync();
    }

    private async Task LoadPowerProfilesAsync()
    {
        try
        {
            var profiles = await _powerProfileService.GetAvailableProfilesAsync();
            AvailableProfiles = new ObservableCollection<PowerProfile>(profiles);
            
            // Set current active profile
            SelectedProfile = profiles.FirstOrDefault(p => p.IsActive);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load power profiles: {ex.Message}");
        }
    }

    private async Task OnProfileSelectionChanged(PowerProfile? profile)
    {
        if (profile == null) return;

        try
        {
            var success = await _powerProfileService.SetActiveProfileAsync(profile.Id);
            if (success)
            {
                // Play audio feedback (like existing TDP changes)
                _audioService?.PlayTickSound();
                
                // Update active state
                foreach (var p in AvailableProfiles)
                    p.IsActive = p.Id == profile.Id;
                
                // Save preference
                SettingsService.SetPreferredPowerProfile(profile.Id);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to change power profile: {ex.Message}");
        }
    }
}
```

### Visual Indicators
- Battery icon for power saver profiles
- Lightning bolt for high performance profiles
- Gear icon for custom/manufacturer profiles

## Error Handling Specifications

### Expected Error Scenarios
1. **Insufficient Privileges**: Some profile changes require admin rights
2. **Profile Not Found**: Profile was deleted after enumeration
3. **PowerCfg Execution Failed**: System command execution issues
4. **Parsing Errors**: Unexpected powercfg output format

### Error Handling Strategy
- Follow existing TDPService error patterns
- Show user-friendly error messages
- Graceful degradation (disable control if service unavailable)
- Log errors for debugging without breaking user experience

## Testing Requirements

### Unit Tests
- PowerProfileService profile parsing logic
- Profile type classification accuracy
- Settings persistence and restoration

### Integration Tests
- Profile switching functionality
- UI responsiveness and proper binding
- Settings integration and startup behavior

### Manual Testing Scenarios
1. Test on device with manufacturer power profiles (ASUS, MSI, etc.)
2. Test with user-created custom profiles
3. Test profile switching while running games
4. Test startup profile restoration
5. Test error scenarios (missing admin rights, etc.)

## Performance Considerations

### Optimization Requirements
- Cache profile list to avoid repeated powercfg calls
- Implement profile change debouncing (avoid rapid switching)
- Lazy load profile descriptions (only when needed)
- Minimize impact on existing TDP control performance

### Resource Usage
- Profile detection should add <50ms to startup time
- Memory footprint increase should be <1MB
- No background polling (event-driven updates only)

## Security Considerations

### Elevation Handling
- Gracefully handle cases where admin rights are required
- Never store credentials or attempt privilege escalation
- Clear user messaging about admin requirements
- Fallback to view-only mode when elevation unavailable

### Input Validation
- Validate all GUID inputs before system calls
- Sanitize profile names for display
- Prevent injection attacks through profile names

## Integration Points

### Existing System Integration
- Integrate with existing settings persistence
- Follow existing error notification patterns

## Acceptance Criteria

### Functional Acceptance
- [ ] All available power profiles are detected and displayed
- [ ] Profile switching works immediately without restart
- [ ] Currently active profile is clearly indicated
- [ ] Settings are properly persisted and restored
- [ ] Error handling works gracefully

### Quality Acceptance
- [ ] Code follows existing HUDRA patterns and conventions
- [ ] UI integrates seamlessly with existing design
- [ ] Performance impact is minimal (<100ms for operations)
- [ ] All error scenarios are handled appropriately