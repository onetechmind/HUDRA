# HUDRA - AI Assistant Development Guide

## Project Overview

**HUDRA** (Heads-Up Display Runtime Assistant) is a modern WinUI 3 desktop application for AMD Ryzen handheld gaming devices. It provides a sleek performance control overlay with TDP adjustment, game detection, resolution management, and system monitoring capabilities.

**Key Features:**
- Real-time TDP (Thermal Design Power) control via RyzenAdj integration
- Smart game detection with machine learning
- Audio and brightness controls
- Battery monitoring
- System tray integration
- Gamepad navigation support
- Fan curve management
- FPS limiting via RTSS integration
- Lossless Scaling integration

## Technology Stack

### Core Framework
- **Platform:** .NET 8.0 (Windows 10.0.19041.0+)
- **UI Framework:** WinUI 3 (Microsoft.WindowsAppSDK 1.5.240428000)
- **Target Platform:** x64 Windows only
- **Language:** C# with nullable reference types enabled
- **Minimum Windows Version:** 10.0.17763.0 (Windows 10 1809)

### Key Dependencies
```xml
Microsoft.WindowsAppSDK 1.5.240428000
Microsoft.Windows.SDK.BuildTools 10.0.22621.3233
MouseKeyHook 5.7.1                    (Global hotkey support)
System.Management 9.0.6               (WMI access for hardware)
System.Management.Automation 7.4.6    (PowerShell integration)
GameLib.NET 1.4.1                     (Game library detection)
LiteDB 5.0.21                         (Embedded database for settings)
LibreHardwareMonitorLib 0.9.4         (Hardware monitoring)
RTSSSharedMemoryNET                   (RivaTuner integration)
```

### Native Dependencies
- **RyzenAdj:** AMD TDP control library (Tools/ryzenadj/)
- **WinRing0:** Low-level hardware access driver
- **ADLX:** AMD Display Library X (External Resources/AMD/ADLX/)
- **RTSS:** RivaTuner Statistics Server integration (Tools/)

## Project Structure

```
HUDRA/
├── .claude/                        # Claude Code configuration
├── ADLX_Wrapper_Source/           # AMD Display Library wrappers
├── Architecture/                   # Technical architecture docs
│   ├── winui3-contentdialog-gamepad-support.md
│   ├── winui3-expander-state-management.md
│   └── winui3-gamepad-navigation.md
├── Specs/                         # Feature specifications
│   └── power_profile_spec_073125.md
├── techdocs/                      # Technical documentation
├── HUDRA/                         # Main application source
│   ├── AttachedProperties/       # XAML attached properties
│   ├── Assets/                   # Icons, sounds, images
│   │   ├── HUDRA_Logo.ico
│   │   ├── tick.wav
│   │   └── *.png images
│   ├── Configuration/            # Settings and configuration
│   │   └── Settings.xml          # Default settings template
│   ├── Controls/                 # Custom WinUI 3 controls
│   │   ├── TdpPickerControl      # TDP scroll picker
│   │   ├── ResolutionPickerControl
│   │   ├── AudioControlsControl
│   │   ├── BrightnessControlControl
│   │   ├── FanControlControl
│   │   ├── FanCurveControl
│   │   ├── PowerProfileControl
│   │   ├── FpsLimiterControl
│   │   └── HotkeySelector
│   ├── Extensions/               # C# extension methods
│   ├── External Resources/       # Third-party binaries
│   │   └── AMD/ADLX/            # AMD Display Library DLLs
│   ├── Helpers/                  # Utility helpers
│   ├── Interfaces/               # Interface definitions
│   │   └── IGamepadNavigable.cs
│   ├── Models/                   # Data models
│   │   ├── DetectedGame.cs
│   │   ├── FpsLimitSettings.cs
│   │   ├── GameInfo.cs
│   │   └── LosslessScalingSettings.cs
│   ├── Pages/                    # Navigation pages
│   │   ├── MainPage.xaml/cs      # Main performance controls
│   │   ├── SettingsPage.xaml/cs  # App configuration
│   │   ├── FanCurvePage.xaml/cs  # Fan curve editor
│   │   └── ScalingPage.xaml/cs   # Lossless Scaling settings
│   ├── Services/                 # Core business logic
│   │   ├── AMD/                  # AMD-specific services
│   │   ├── FanControl/          # Fan control implementations
│   │   │   ├── ECCommunicationBase.cs
│   │   │   ├── FanControlDeviceBase.cs
│   │   │   ├── DeviceDetectionService.cs
│   │   │   └── Devices/         # Device-specific implementations
│   │   │       ├── GPD.cs
│   │   │       └── OneXPlayer.cs
│   │   ├── GameLibraryProviders/ # Game library integrations
│   │   ├── Power/                # Power management services
│   │   ├── TDPService.cs         # RyzenAdj integration
│   │   ├── TrayIconService.cs
│   │   ├── AudioService.cs
│   │   ├── BatteryService.cs
│   │   ├── BrightnessService.cs
│   │   ├── ResolutionService.cs
│   │   ├── NavigationService.cs
│   │   ├── WindowManagementService.cs
│   │   ├── EnhancedGameDetectionService.cs
│   │   ├── GamepadNavigationService.cs
│   │   ├── PowerEventService.cs
│   │   ├── StartupService.cs
│   │   ├── LosslessScalingService.cs
│   │   └── RtssFpsLimiterService.cs
│   ├── Tools/                    # External binaries and scripts
│   │   ├── ryzenadj/            # RyzenAdj TDP control tools
│   │   │   ├── libryzenadj.dll
│   │   │   ├── ryzenadj.exe
│   │   │   ├── WinRing0x64.dll/sys
│   │   │   └── *.bat, *.ps1 scripts
│   │   └── RTSS*.dll            # RivaTuner integration
│   ├── Utils/                    # Utility classes
│   ├── App.xaml/cs              # Application entry point
│   ├── MainWindow.xaml/cs       # Main window implementation
│   └── HUDRA.csproj             # Project file
├── HUDRA.sln                     # Visual Studio solution
├── README.md                     # User documentation
├── AGENTS.md                     # Repository guidelines
└── CLAUDE.md                     # This file

Total: ~96 source files (C# + XAML), 41 service files
```

## Development Workflow

### Prerequisites
1. **Visual Studio 2022** with:
   - Windows App SDK workload
   - .NET 8 SDK
   - Windows 11 SDK (22000 or later)
2. **Administrator privileges** (required for TDP control and hardware access)
3. **AMD Ryzen processor** (recommended for full testing)

### Initial Setup
```bash
# Clone the repository
git clone <repository-url>
cd HUDRA

# Restore dependencies
dotnet restore

# Build the solution
dotnet build HUDRA/HUDRA.csproj -c Debug
```

### Build Commands
```bash
# Debug build (development)
dotnet build HUDRA/HUDRA.csproj -c Debug

# Release build (packaging)
dotnet build HUDRA/HUDRA.csproj -c Release

# Run the application
dotnet run --project HUDRA/HUDRA.csproj

# Clean build artifacts
dotnet clean
```

### Visual Studio Development
- Open `HUDRA.sln` in Visual Studio 2022
- Set build configuration to Debug or Release
- Run with F5 (will prompt for admin rights)
- For full debugging, attach with administrator privileges

### Important Build Notes
- **Content Files:** Tools/ryzenadj/ and External Resources/ directories are copied to bin/ on build
- **Self-Contained:** WindowsAppSDKSelfContained is enabled
- **Unsafe Code:** AllowUnsafeBlocks is enabled for P/Invoke operations
- **Nullable:** Nullable reference types are enabled - address warnings, don't suppress them

## Coding Conventions

### C# Style Guidelines
- **Indentation:** 4 spaces (no tabs)
- **Public Members:** PascalCase (`TdpService`, `GetCurrentValue()`)
- **Private Fields:** _camelCase with underscore prefix (`_ryzenAdjHandle`, `_isDragging`)
- **Properties:** PascalCase with expression bodies where appropriate
- **Async Methods:** Always use `Async` suffix (`InitializeAsync()`, `LoadAsync()`)
- **Nullable:** Use nullable reference types (`string?`, `TdpService?`)
- **Interfaces:** Prefix with `I` (`IGamepadNavigable`, `INotifyPropertyChanged`)

### XAML Conventions
- **Compact Attributes:** Keep attributes organized and readable
- **Naming:** Use `x:Name` with PascalCase for named elements
- **Resources:** Define in application or window resources
- **Bindings:** Use `x:Bind` for compiled bindings where possible (better performance than `Binding`)

### Architecture Patterns
- **MVVM:** Follow Model-View-ViewModel pattern
- **Services:** Singleton or scoped services, registered in App.xaml.cs
- **Separation:** Keep UI logic in code-behind, business logic in Services
- **Disposal:** Implement IDisposable for services with unmanaged resources
- **Events:** Use PropertyChanged for data binding updates

### Example Code Pattern
```csharp
namespace HUDRA.Services
{
    public class ExampleService : IDisposable
    {
        private readonly SomeService _dependency;
        private bool _disposed = false;
        private int _currentValue;

        public int CurrentValue
        {
            get => _currentValue;
            private set
            {
                if (_currentValue != value)
                {
                    _currentValue = value;
                    OnPropertyChanged();
                }
            }
        }

        public ExampleService(SomeService dependency)
        {
            _dependency = dependency;
        }

        public async Task<bool> InitializeAsync()
        {
            try
            {
                // Initialization logic
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Initialization failed: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                // Cleanup resources
                _disposed = true;
            }
        }
    }
}
```

## Key Services and Components

### Core Services

#### TDPService (Services/TDPService.cs)
- Manages AMD TDP (Thermal Design Power) control via RyzenAdj
- Supports both DLL mode (libryzenadj.dll) and EXE mode (ryzenadj.exe)
- P/Invoke integration with native libraries
- Thread-safe operations with proper disposal
- Range: 5W-30W

#### EnhancedGameDetectionService
- Automatic game detection with machine learning
- Tracks fullscreen applications and GPU usage
- Maintains game history in LiteDB
- Provides "Return to Game" functionality

#### WindowManagementService
- Manages window positioning (default: bottom-right corner)
- Handles DPI scaling
- Always-on-top functionality
- Drag-and-drop window movement

#### TrayIconService
- System tray integration
- Show/hide functionality
- Context menu for quick actions
- Persist through close (minimize to tray)

#### GamepadNavigationService
- Controller support for UI navigation
- L1/R1 page switching
- Face button interactions
- Implements IGamepadNavigable interface

#### FanControlService
- Device-specific fan curve management
- EC (Embedded Controller) communication
- Supports GPD, OneXPlayer, and other devices
- Custom fan curve profiles

#### RtssFpsLimiterService
- RivaTuner Statistics Server integration
- Global FPS limiting
- Per-game FPS profiles

#### LosslessScalingService
- Integration with Lossless Scaling application
- Profile management
- Settings synchronization

### Custom Controls

All controls are in `HUDRA/Controls/` and follow the pattern:
- `.xaml` file for UI definition
- `.xaml.cs` code-behind for logic
- Implement `IGamepadNavigable` for controller support

**Key Controls:**
- **TdpPickerControl:** Smooth scrolling TDP picker (5-30W)
- **ResolutionPickerControl:** Resolution and refresh rate selection
- **AudioControlsControl:** Volume slider and mute toggle
- **BrightnessControlControl:** System brightness adjustment
- **FanControlControl:** Fan speed and mode selection
- **FanCurveControl:** Visual fan curve editor
- **PowerProfileControl:** Windows power profile switching
- **FpsLimiterControl:** FPS limit configuration
- **HotkeySelector:** Keyboard shortcut configuration

### Pages

- **MainPage:** Primary interface with performance controls
- **SettingsPage:** Application configuration and preferences
- **FanCurvePage:** Advanced fan curve editor
- **ScalingPage:** Lossless Scaling integration settings

## Hardware Integration

### RyzenAdj Integration
**Location:** `Tools/ryzenadj/`
**Files:**
- `libryzenadj.dll` - Native library for TDP control
- `ryzenadj.exe` - Command-line tool (fallback mode)
- `WinRing0x64.dll/sys` - Low-level driver for MSR access

**CRITICAL:** Never modify or commit changed RyzenAdj binaries without provenance. These require administrator privileges and kernel-mode driver access.

### ADLX Integration
**Location:** `External Resources/AMD/ADLX/`
- AMD Display Library X for GPU control
- Multiple DLLs for different functionality (3DSettings, AutoTuning, DisplaySettings, PerformanceMetrics)

### Fan Control
**Device-Specific:** Each handheld device (GPD, OneXPlayer, etc.) has custom EC communication protocols in `Services/FanControl/Devices/`.

**IMPORTANT:** Test fan control thoroughly on actual hardware. Incorrect EC commands can cause hardware issues.

## Configuration and Settings

### Settings File
**Runtime Location:** `%LOCALAPPDATA%\HUDRA\settings.json`
**Default Template:** `HUDRA/Configuration/Settings.xml`

### Key Settings
```json
{
  "TdpCorrectionEnabled": true,
  "UseStartupTdp": true,
  "StartupTdp": 15,
  "LastUsedTdp": 20,
  "StartAtWindowsStartup": true,
  "MinimizeToTray": true,
  "CloseToTray": true,
  "WindowHeight": 752,
  "WindowWidth": 800
}
```

### Database
**LiteDB:** Used for game detection history and learned behaviors
**Location:** `%LOCALAPPDATA%\HUDRA\`

## Testing Guidelines

### Manual Testing
No automated test project exists yet. All testing is manual.

**Critical Test Flows:**
1. **TDP Control:**
   - Adjust TDP via scroll and click
   - Verify audio feedback (tick sound)
   - Confirm TDP persistence across restarts
   - Test "Sticky TDP" correction

2. **Game Detection:**
   - Launch various games
   - Verify detection and "Return to Game" button
   - Check fullscreen detection
   - Validate learning behavior

3. **UI Navigation:**
   - Test keyboard navigation (Tab, Arrow keys)
   - Test gamepad navigation (D-pad, L1/R1)
   - Verify page transitions
   - Check window dragging

4. **Power Events:**
   - Test suspend/resume
   - Verify TDP reapplication after wake
   - Check hibernate scenarios

5. **System Integration:**
   - Test system tray functionality
   - Verify global hotkey (Win+Alt+Ctrl)
   - Check startup behavior
   - Validate admin privilege handling

### Hardware Requirements for Testing
- AMD Ryzen processor (4000 series or newer)
- Handheld gaming device (for full feature testing)
- Touch screen (optional, for touch testing)
- Xbox-compatible gamepad (for navigation testing)

### Testing Best Practices
- **Always rebuild** before testing to ensure content files are copied
- **Run as Administrator** for TDP and hardware features
- **Document edge cases** in PR descriptions
- **Capture screenshots/GIFs** for UI changes
- **Test on actual hardware** when possible (especially fan control)

## Git Workflow

### Branch Strategy
- Feature branches: `feature/<description>`
- Bug fixes: `fix/<description>`
- Development happens on feature branches
- PRs merged to main branch

### Commit Guidelines
- **Summary:** Concise (≤72 chars), describe intent
- **Examples:**
  - ✅ "Refine expander state handling"
  - ✅ "Add FPS limiter integration"
  - ✅ "Fix TDP correction on resume from hibernate"
  - ❌ "Update code"
  - ❌ "Changes"

- **Group related changes** per commit
- **Atomic commits:** Each commit should compile and work

### Pull Request Requirements
1. **Brief narrative** of the change
2. **Linked issues** when available (Fixes #123)
3. **Test notes** or manual validation evidence
4. **Screenshots/GIFs** for UI updates
5. **Highlight impacts** to Tools/ binaries or configuration
6. **Note breaking changes** prominently

### Example PR Description
```markdown
## Summary
Adds FPS limiting via RTSS integration with per-game profiles.

## Changes
- New RtssFpsLimiterService for RTSS communication
- FpsLimiterControl UI component
- Settings page integration
- Game-specific FPS profile storage

## Testing
- Tested with RTSS 7.3.5 installed
- Verified FPS limiting in multiple games
- Confirmed profile persistence across restarts

## Screenshots
[Add screenshots]

## Notes
- Requires RTSS to be installed (gracefully degrades if not present)
- No changes to Tools/ binaries
```

## Security and Configuration

### Security Considerations
1. **Administrator Rights:** Required for RyzenAdj and fan control
2. **Binary Integrity:** Never commit modified RyzenAdj/WinRing0 binaries
3. **Secrets:** Never commit personal hardware IDs or credentials
4. **Logs:** Scrub user-specific paths when sharing logs
5. **Scripts:** Ensure least-privilege execution

### Configuration Files
- **Settings.xml:** Default template, safe to modify
- **.gitignore:** Properly configured for VS/C# projects
- **Never commit:** `bin/`, `obj/`, `*.user` files

## Common Patterns and Practices

### Service Registration (App.xaml.cs)
Services are typically instantiated in `App.OnLaunched()` or `MainWindow` constructor:
```csharp
public App()
{
    InitializeComponent();
    TdpMonitor = new TdpMonitorService(MainWindow.DispatcherQueue);
    FanControlService = new FanControlService(MainWindow.DispatcherQueue);
}
```

### UI Thread Marshaling
Use `DispatcherQueue` for updating UI from background threads:
```csharp
_dispatcherQueue.TryEnqueue(() =>
{
    // Update UI properties here
    CurrentValue = newValue;
});
```

### P/Invoke Pattern
```csharp
[DllImport("kernel32.dll", SetLastError = true)]
private static extern IntPtr LoadLibrary(string lpFileName);

[DllImport("user32.dll")]
private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
```

### Dependency Paths
Always use `AppDomain.CurrentDomain.BaseDirectory` for finding dependencies:
```csharp
string baseDir = AppDomain.CurrentDomain.BaseDirectory;
string dllPath = Path.Combine(baseDir, "Tools", "ryzenadj", "libryzenadj.dll");
```

### Error Handling
```csharp
try
{
    // Hardware operation
    return true;
}
catch (Exception ex)
{
    Debug.WriteLine($"Operation failed: {ex.Message}");
    // Graceful degradation
    return false;
}
```

### IDisposable Pattern
```csharp
public void Dispose()
{
    if (!_disposed)
    {
        // Release unmanaged resources
        if (_handle != IntPtr.Zero)
        {
            FreeLibrary(_handle);
            _handle = IntPtr.Zero;
        }
        _disposed = true;
    }
}
```

## Important Notes for AI Assistants

### When Making Changes

1. **Preserve Binary Dependencies**
   - Never modify files in `Tools/ryzenadj/` or `External Resources/AMD/ADLX/`
   - These are third-party binaries with specific licensing
   - Changes require validation on actual hardware

2. **Hardware Capability Checks**
   - Always guard hardware operations with capability checks
   - Provide graceful degradation when hardware unavailable
   - Example: Check if RyzenAdj initializes before using TDP control

3. **Nullable Reference Types**
   - Project has nullable enabled - respect nullability annotations
   - Use `?` for nullable types, `!` only when you're certain
   - Address warnings, don't suppress them

4. **Build Configuration**
   - Content files must be set to `CopyToOutputDirectory="PreserveNewest"`
   - Check HUDRA.csproj `<Content>` and `<ItemGroup>` sections
   - Test builds after adding new assets or tools

5. **XAML Binding**
   - Prefer `x:Bind` over `Binding` for better performance
   - Understand the difference between OneTime, OneWay, and TwoWay modes
   - Use `Mode=OneWay` for read-only data, `Mode=TwoWay` for editable controls

6. **Thread Safety**
   - UI updates must happen on UI thread (use DispatcherQueue)
   - Services may be called from multiple threads - add locks if needed
   - Be cautious with async/await and UI interactions

7. **Memory Management**
   - Dispose services properly in App shutdown
   - Unsubscribe from events to prevent memory leaks
   - P/Invoke resources need explicit cleanup

8. **Documentation References**
   - `Architecture/` contains WinUI 3 specific patterns
   - `Specs/` contains feature specifications
   - `README.md` is user-facing documentation
   - `AGENTS.md` has additional repository guidelines

### Before Committing

- [ ] Code builds successfully (`dotnet build`)
- [ ] No new nullable reference warnings
- [ ] Content files copy correctly to bin/
- [ ] Manual testing on critical paths
- [ ] No hardcoded paths or secrets
- [ ] XAML changes render correctly
- [ ] No modifications to Tools/ or External Resources/ binaries
- [ ] Thread-safe operations for background services
- [ ] Proper IDisposable implementation for new services
- [ ] Updated relevant documentation if adding new features

### Common Pitfalls to Avoid

❌ **Don't:** Modify RyzenAdj or ADLX binaries
✅ **Do:** Use existing APIs, report issues upstream

❌ **Don't:** Suppress nullable warnings
✅ **Do:** Fix nullability properly with checks or nullable types

❌ **Don't:** Use Thread.Sleep() on UI thread
✅ **Do:** Use async/await and Task.Delay()

❌ **Don't:** Create services without IDisposable when needed
✅ **Do:** Implement IDisposable for unmanaged resources

❌ **Don't:** Hardcode file paths
✅ **Do:** Use AppDomain.CurrentDomain.BaseDirectory

❌ **Don't:** Update UI from background threads directly
✅ **Do:** Use DispatcherQueue.TryEnqueue()

❌ **Don't:** Commit bin/, obj/, or .vs/ directories
✅ **Do:** Check .gitignore is working correctly

## Additional Resources

### Documentation
- **README.md:** User guide and feature overview
- **AGENTS.md:** Repository guidelines and workflow
- **Architecture/*.md:** WinUI 3 patterns and technical decisions
- **Specs/*.md:** Feature specifications and requirements

### External Links
- [WinUI 3 Documentation](https://docs.microsoft.com/en-us/windows/apps/winui/)
- [RyzenAdj GitHub](https://github.com/FlyGoat/RyzenAdj)
- [.NET 8 Documentation](https://docs.microsoft.com/en-us/dotnet/core/)
- [Windows App SDK](https://docs.microsoft.com/en-us/windows/apps/windows-app-sdk/)

### Key Files to Reference
- `HUDRA/App.xaml.cs` - Application lifecycle and service initialization
- `HUDRA/MainWindow.xaml.cs` - Main window logic and service orchestration
- `HUDRA/Services/TDPService.cs` - Example of P/Invoke and native library integration
- `HUDRA/Controls/TdpPickerControl.xaml` - Example of custom control implementation

## Support and Troubleshooting

### Common Issues

**TDP Control Not Working**
- Verify running as Administrator
- Check RyzenAdj files in Tools/ryzenadj/
- Ensure AMD Ryzen processor present

**Build Failures**
- Run `dotnet restore` first
- Check Windows SDK version installed
- Verify .NET 8 SDK present

**Missing Content Files**
- Check .csproj `<Content>` sections
- Rebuild solution (dotnet clean && dotnet build)
- Verify CopyToOutputDirectory settings

**UI Not Updating**
- Ensure PropertyChanged events firing
- Check DispatcherQueue usage for thread marshaling
- Verify x:Bind Mode settings

---

**Last Updated:** 2025-11-17
**Version:** 1.0
**Maintained By:** Development Team

For questions or clarifications, refer to the GitHub Issues or Discussions.
