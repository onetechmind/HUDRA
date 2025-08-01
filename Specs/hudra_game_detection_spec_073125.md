# HUDRA Game Detection Service Simplification Specification

## Overview
This specification outlines the refactoring of HUDRA's GameDetectionService to rely solely on game directory detection while maintaining the learning system for inclusion/exclusion lists and navbar button functionality.

## Current State Analysis

### Existing Detection Methods (TO BE REMOVED)
1. **GPU Usage Detection** - `GetProcessGpuUsage()` and `GetSimpleGpuEstimate()`
2. **Graphics DLL Detection** - `HasGraphicsLibraries()`
3. **Fullscreen Detection** - `IsLikelyFullscreenApp()`
4. **Memory Usage Heuristics** - Working set analysis in `GetSimpleGpuEstimate()`

### Components to Retain
1. **Directory Detection** - `IsInGameDirectory()` method
2. **Learning System** - `GameLearningService` with inclusion/exclusion lists
3. **Hardcoded Exclusions** - `_definitelyNotGames` HashSet
4. **Xbox App Detection** - `IsXboxAppNotGame()` and `IsXboxGame()` methods
5. **Navbar Button Logic** - Auto-presentation when games are detected

## Target Architecture

### Simplified Detection Flow
```
1. Check if current game is still running (existing logic)
2. Get foreground window and process
3. Check hardcoded exclusion list (_definitelyNotGames) → EXCLUDE
4. Check learned exclusion list → EXCLUDE  
5. Check learned inclusion list → INCLUDE (immediate return)
6. Check game directory logic → LEARN and INCLUDE/EXCLUDE
7. All other processes → EXCLUDE and LEARN as non-game
```

## Implementation Requirements

### File: `HUDRA/Services/GameDetectionService.cs`

#### Methods to Modify

**1. `IsLikelyGame()` Method - COMPLETE REWRITE**
```csharp
private bool IsLikelyGame(Process process, string windowTitle)
{
    // Early exclusion: Is this definitely NOT a game?
    if (_definitelyNotGames.Contains(process.ProcessName))
    {
        System.Diagnostics.Debug.WriteLine($"Excluded by process name: {process.ProcessName}");
        return false;
    }

    // Special check for Xbox app vs Xbox games
    if (IsXboxAppNotGame(process, windowTitle))
    {
        System.Diagnostics.Debug.WriteLine($"Excluded Xbox app: {windowTitle}");
        return false;
    }

    // ONLY METHOD: Check if it's in a game directory
    if (IsInGameDirectory(process))
    {
        System.Diagnostics.Debug.WriteLine($"Game detected by directory: {process.ProcessName}");
        return true;
    }

    System.Diagnostics.Debug.WriteLine($"Not detected as game: {process.ProcessName} (not in game directory)");
    return false;
}
```

#### Methods to Remove Completely
1. `GetProcessGpuUsage(int processId)`
2. `GetSimpleGpuEstimate(Process process)`
3. `HasGraphicsLibraries(Process process)`
4. `IsLikelyFullscreenApp(IntPtr windowHandle)`

#### Windows API Imports to Remove
- All GPU-related P/Invoke declarations
- Screen metrics related to fullscreen detection
- Any performance counter related imports

#### Methods to Keep Unchanged
1. `IsInGameDirectory(Process process)` - Core directory detection logic
2. `IsXboxGame(string executablePath)` - Xbox-specific directory detection
3. `IsXboxAppNotGame(Process process, string windowTitle)` - Xbox app filtering
4. `DetectActiveGame()` - Main detection orchestration (learning logic)
5. `DetectGamesCallback()` - Timer callback and game state management

### Enhanced Game Directory Detection

**Expand `IsInGameDirectory()` with Additional Paths**
```csharp
private bool IsInGameDirectory(Process process)
{
    try
    {
        string? executablePath = process.MainModule?.FileName;
        if (string.IsNullOrEmpty(executablePath)) return false;

        var gameDirectories = new[]
        {
            // Steam
            @"\Steam\steamapps\common\",
            @"\Program Files (x86)\Steam\steamapps\common\",
            @"\Program Files\Steam\steamapps\common\",
            
            // Epic Games
            @"\Epic Games\",
            @"\Program Files\Epic Games\",
            @"\Program Files (x86)\Epic Games\",
            
            // Origin/EA
            @"\Origin Games\",
            @"\EA Games\",
            @"\Program Files\Origin Games\",
            @"\Program Files (x86)\Origin Games\",
            
            // Ubisoft
            @"\Ubisoft\Ubisoft Game Launcher\games\",
            @"\Program Files\Ubisoft\Ubisoft Game Launcher\games\",
            @"\Program Files (x86)\Ubisoft\Ubisoft Game Launcher\games\",
            
            // GOG
            @"\GOG Galaxy\Games\",
            @"\Program Files\GOG Galaxy\Games\",
            @"\Program Files (x86)\GOG Galaxy\Games\",
            
            // Xbox/Microsoft Store games
            @"\XboxGames\",
            @"\Xbox Games\",
            @"\WindowsApps\",
            @"\Microsoft.Gaming\",
            @"\Program Files\WindowsApps\",
            @"\Program Files (x86)\WindowsApps\",
            
            // Generic game folders
            @"\Games\",
            @"\Program Files\Games\",
            @"\Program Files (x86)\Games\",
            
            // Additional common game directories
            @"\Riot Games\",
            @"\Battle.net\",
            @"\Blizzard Entertainment\",
            @"\Rockstar Games\",
            @"\Take-Two Interactive\",
            @"\Square Enix\",
            @"\Activision\",
            @"\SEGA\",
            @"\Capcom\",
            @"\Valve\",
            @"\2K Games\",
            @"\Bethesda Game Studios\",
            @"\CD Projekt RED\"
        };

        bool inGameDirectory = gameDirectories.Any(dir =>
            executablePath.Contains(dir, StringComparison.OrdinalIgnoreCase));

        // Additional Xbox-specific detection
        if (!inGameDirectory && IsXboxGame(executablePath))
        {
            return true;
        }

        return inGameDirectory;
    }
    catch
    {
        return false;
    }
}
```

### Learning System Preservation

**Keep Existing Learning Logic in `DetectActiveGame()`**
- Maintain inclusion list priority (immediate return for known games)
- Maintain exclusion list filtering with session tracking
- Continue learning from directory detection results
- Preserve all GameLearningService functionality

### Navbar Button Integration

**Maintain Existing UI Integration**
- Keep `GameDetected` and `GameStopped` events
- Preserve `MainWindow.xaml.cs` game detection handlers
- Maintain purple glow animation and game controller icon
- Keep tooltip updates and visibility logic

## Testing Strategy

### Validation Scenarios
1. **Steam Games** - Verify detection in standard Steam directory
2. **Epic Games** - Test Epic Games Launcher installations  
3. **Xbox Games** - Validate Xbox/Microsoft Store game detection
4. **Non-Games** - Ensure browsers, productivity apps are excluded
5. **Learning Persistence** - Verify inclusion/exclusion lists survive restarts
6. **Navbar Behavior** - Confirm button appears/disappears correctly

### Performance Expectations
- **Faster Detection** - Removal of GPU checks should improve performance
- **Reduced CPU Usage** - No more graphics DLL enumeration or GPU polling
- **Improved Reliability** - Directory-based detection is more deterministic
- **Better Battery Life** - Less system resource usage during detection

## Migration Considerations

### Backwards Compatibility
- Existing learned game lists will continue to work
- No changes to settings or configuration files required
- Directory detection logic is already proven and reliable

### Edge Cases
1. **Portable Games** - Games not in standard directories may not be detected initially
2. **Custom Installations** - Users with non-standard game directories may need manual learning
3. **Development Environments** - Game development tools should remain in exclusion list

### User Communication
- Update documentation to reflect directory-based detection
- Add troubleshooting section for games in non-standard locations
- Emphasize that the system learns over time for edge cases

## Success Criteria

1. **100% Removal** of GPU, graphics DLL, fullscreen, and memory-based detection
2. **Preserved Functionality** for directory detection and learning system
3. **Maintained UI Integration** with navbar button and visual feedback
4. **Improved Performance** with reduced system resource usage
5. **Simplified Codebase** with removal of complex heuristic detection logic

## Code Quality Requirements

### WinUI 3 Best Practices
- Use proper async/await patterns for any new file I/O operations
- Maintain existing MVVM architecture
- Ensure proper disposal of resources
- Follow existing naming conventions and code style

### Error Handling
- Maintain existing try-catch blocks around process enumeration
- Add logging for directory detection failures
- Preserve graceful degradation when process access is denied

### Performance Optimization
- Cache game directory list as static readonly
- Minimize string allocations in hot paths
- Preserve existing timer intervals and threading model

This specification provides a clear roadmap for simplifying HUDRA's game detection to rely solely on directory-based logic while maintaining all learning and UI functionality.