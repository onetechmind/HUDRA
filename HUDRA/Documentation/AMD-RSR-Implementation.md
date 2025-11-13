# AMD Radeon Super Resolution (RSR) Implementation

## Overview

HUDRA now supports controlling AMD's Radeon Super Resolution (RSR) feature through the Scaling page. This document explains the implementation, how it works, and future enhancement opportunities.

---

## Architecture

### Components

1. **AmdAdlxService** (`Services/AmdAdlxService.cs`)
   - Core service for AMD GPU feature control
   - Implements AMD ADLX SDK integration (placeholder)
   - Falls back to Windows Registry-based control
   - Handles AMD GPU detection

2. **AmdFeaturesControl** (`Controls/AmdFeaturesControl.xaml` + `.cs`)
   - UI control with RSR toggle
   - Integrates with AmdAdlxService
   - Implements gamepad navigation via IGamepadNavigable
   - Handles state synchronization and error recovery

3. **ScalingPage Integration**
   - AMD Features expander appears above Lossless Scaling
   - Session-persistent expander state
   - Follows existing HUDRA UI patterns

---

## How RSR Triggering Works

### Current Implementation (Registry-Based)

When the user toggles RSR in HUDRA:

1. **User toggles switch** → `RsrEnabled` property changes
2. **Property setter** → Calls `ApplyRsrSettingAsync(bool enabled)`
3. **Service method** → `AmdAdlxService.SetRsrEnabledAsync(enabled, sharpness)`
4. **Registry update** → Writes to `HKEY_CURRENT_USER\Software\AMD\CN\`
   - `RSR_Enable` = 1 (enabled) or 0 (disabled)
   - `RSR_Sharpness` = 80 (default, range 0-100)
5. **Driver notification** → Broadcasts `WM_SETTINGCHANGE` message
6. **State verification** → Reverts toggle if operation fails

### Registry Keys

```
HKEY_CURRENT_USER\Software\AMD\CN\
├── RSR_Enable (DWORD)        # 0 = disabled, 1 = enabled
└── RSR_Sharpness (DWORD)     # 0-100, quality vs performance
```

**Note:** These registry keys may vary by AMD driver version. Current implementation uses common paths but may need adjustment for specific driver versions.

---

## AMD GPU Detection

The service detects AMD GPUs using two methods:

### Method 1: WMI Query
```csharp
SELECT * FROM Win32_VideoController
```
Checks `Name` and `AdapterCompatibility` for "AMD", "Radeon", or "ATI"

### Method 2: Registry Query
```
HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\
{4d36e968-e325-11ce-bfc1-08002be10318}\0000\ProviderName
```
Checks for AMD/ATI provider name

If no AMD GPU is detected, the RSR toggle will be non-functional and revert state changes.

---

## Error Handling

The implementation includes comprehensive error handling:

1. **GPU Not Available**
   - Toggle reverts automatically
   - Debug log: "Cannot apply RSR: No AMD GPU detected"

2. **Registry Access Denied**
   - May require elevated permissions
   - Debug log: "Registry access denied. App may need elevated permissions."
   - Toggle reverts to previous state

3. **Service Not Initialized**
   - Initialization happens on control load
   - Falls back gracefully if initialization fails

4. **Setting Apply Failure**
   - Toggle reverts to previous state
   - TODO: Show user-facing error message

---

## ADLX SDK Integration (Future Enhancement)

### Current State
The `AmdAdlxService` includes placeholder methods for ADLX SDK integration:
- `GetRsrStateViaAdlx()` - Not yet implemented
- `SetRsrViaAdlx()` - Not yet implemented

These methods are currently marked as TODO and return false, causing fallback to registry-based control.

### What ADLX Provides

AMD Device Library eXtra (ADLX) is AMD's official SDK for GPU management:
- **Official API** for AMD driver features
- **Forward compatible** with driver updates
- **Programmatic control** of:
  - Radeon Super Resolution (RSR)
  - Radeon Anti-Lag
  - Radeon Boost
  - FreeSync
  - GPU metrics and monitoring

### Implementation Steps for Full ADLX Support

1. **Obtain ADLX SDK**
   - Download from AMD Developer site
   - Include ADLX headers and DLLs in project

2. **P/Invoke Declarations**
   ```csharp
   [DllImport("amdxx64.dll")]
   private static extern int ADLX_Initialize();

   [DllImport("amdxx64.dll")]
   private static extern int ADLX_GetRSRState(out bool enabled, out int sharpness);

   [DllImport("amdxx64.dll")]
   private static extern int ADLX_SetRSRState(bool enabled, int sharpness);
   ```

3. **Update Service Methods**
   Replace placeholder implementations in `AmdAdlxService.cs`:
   - Implement `TryInitializeAdlx()`
   - Implement `GetRsrStateViaAdlx()`
   - Implement `SetRsrViaAdlx()`

4. **DLL Distribution**
   - Include ADLX DLLs in HUDRA package
   - Or detect AMD driver installation and use driver-provided DLLs

### Benefits of ADLX Implementation

- ✓ More reliable than registry manipulation
- ✓ Works across all AMD driver versions
- ✓ Immediate effect (no driver restart needed)
- ✓ Proper permission handling
- ✓ Access to additional AMD features (Anti-Lag, Boost, etc.)

---

## Testing

### Manual Testing Steps

1. **AMD GPU Present**
   - Launch HUDRA on system with AMD GPU
   - Navigate to Scaling page
   - Expand "AMD Features"
   - Toggle RSR on/off
   - Verify Debug output shows successful state changes

2. **Non-AMD GPU**
   - Launch on Intel/NVIDIA system
   - Verify AMD Features still appears
   - Toggle should revert and log "No AMD GPU detected"

3. **Gamepad Navigation**
   - Connect gamepad
   - Navigate to AMD Features expander with D-pad
   - Press A to expand
   - D-pad down to RSR toggle
   - Press A to toggle RSR
   - Purple border should indicate focus

### Debug Output

Monitor Visual Studio Debug output for:
```
AMD GPU detected
ADLX SDK not available, will use registry fallback
Initializing AMD service...
Loaded RSR state: enabled=False, sharpness=80
Applying RSR setting: True
Registry: Set RSR enabled=True, sharpness=80
Successfully applied RSR setting: True
```

---

## Limitations

### Current Limitations

1. **Registry-Based Control**
   - May not work with all AMD driver versions
   - Registry keys can change between driver releases
   - Requires driver to detect and apply changes

2. **No Immediate Feedback**
   - Settings may not apply until game/app launch
   - AMD driver reads registry on application startup

3. **Permission Issues**
   - May require elevated permissions for registry writes
   - HKEY_CURRENT_USER typically accessible without admin

4. **No Sharpness Control**
   - Currently hardcoded to 80%
   - Could be exposed as slider in future

### Future Enhancements

- [ ] Implement full ADLX SDK integration
- [ ] Add RSR sharpness slider control
- [ ] Add visual feedback when settings apply
- [ ] Show AMD GPU information (model, driver version)
- [ ] Hide/disable control when no AMD GPU present
- [ ] Add additional AMD features (Anti-Lag, Boost, Chill)
- [ ] Per-game RSR profiles

---

## Code Files

| File | Purpose | Lines |
|------|---------|-------|
| `Services/AmdAdlxService.cs` | AMD GPU control service | ~400 |
| `Controls/AmdFeaturesControl.xaml` | RSR toggle UI | ~61 |
| `Controls/AmdFeaturesControl.xaml.cs` | Control logic & gamepad nav | ~286 |
| `Pages/ScalingPage.xaml` | Page integration | Modified |
| `Pages/ScalingPage.xaml.cs` | Expander state management | Modified |

---

## References

- [AMD ADLX SDK Documentation](https://gpuopen.com/adl/)
- [AMD Radeon Super Resolution](https://www.amd.com/en/technologies/radeon-super-resolution)
- [Windows Registry Best Practices](https://docs.microsoft.com/en-us/windows/win32/sysinfo/registry)

---

## Support

For issues or questions:
1. Check Debug output for error messages
2. Verify AMD GPU is detected
3. Check registry permissions
4. Consider implementing full ADLX SDK support

---

**Last Updated:** 2025-11-13
**Author:** Claude Code Agent
**Version:** 1.0 (Registry-based implementation)
