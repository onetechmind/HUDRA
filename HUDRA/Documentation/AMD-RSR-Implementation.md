# AMD Radeon Super Resolution (RSR) Implementation

## Overview

HUDRA now supports controlling AMD's Radeon Super Resolution (RSR) feature through the Scaling page using the **official AMD ADLX SDK**. This document explains the implementation, how it works, and deployment requirements.

---

## Architecture

### Components

1. **AdlxWrapper** (`Services/AMD/AdlxWrapper.cs`) ⭐ **NEW**
   - P/Invoke wrapper for AMD ADLX SDK DLLs
   - Exposes RSR functions: `SetRSR`, `GetRSRState`, `HasRSRSupport`, `SetRSRSharpness`, `GetRSRSharpness`
   - Safe wrappers with comprehensive error handling
   - DLL availability detection and validation
   - Based on: https://github.com/JamesCJ60/ADLX-SDK-Wrapper

2. **AmdAdlxService** (`Services/AmdAdlxService.cs`)
   - Core service for AMD GPU feature control
   - **Primary**: Uses ADLX SDK via AdlxWrapper
   - **Fallback**: Windows Registry if ADLX unavailable
   - Handles AMD GPU detection

3. **AmdFeaturesControl** (`Controls/AmdFeaturesControl.xaml` + `.cs`)
   - UI control with RSR toggle
   - Integrates with AmdAdlxService
   - Implements gamepad navigation via IGamepadNavigable
   - Handles state synchronization and error recovery

4. **ScalingPage Integration**
   - AMD Features expander appears above Lossless Scaling
   - Session-persistent expander state
   - Follows existing HUDRA UI patterns

---

## How RSR Triggering Works

### Primary Implementation (ADLX SDK)

When the user toggles RSR in HUDRA:

```
User Toggle → RsrEnabled Property
    ↓
ApplyRsrSettingAsync(enabled)
    ↓
AmdAdlxService.SetRsrEnabledAsync(enabled, sharpness: 80)
    ↓
[ADLX Path - Primary]
    ↓
AdlxWrapper.TrySetRSR(enabled) → ADLX_3DSettings.dll
    ↓
AMD Driver (Immediate Effect)
    ↓
RSR Applied to Games
```

### ADLX SDK Functions Used

```csharp
// Check hardware support
bool HasRSRSupport();

// Get current state
bool GetRSRState();

// Enable/disable RSR
bool SetRSR(bool isEnabled);

// Get sharpness level (0-100)
int GetRSRSharpness();

// Set sharpness level
bool SetRSRSharpness(int sharpness);
```

### Fallback Implementation (Registry)

If ADLX DLL is not available, HUDRA falls back to registry-based control:

```
Registry Write: HKCU\Software\AMD\CN\
    • RSR_Enable = 1 (on) or 0 (off)
    • RSR_Sharpness = 80 (quality level)
    ↓
WM_SETTINGCHANGE Broadcast
    ↓
AMD Driver Detects Change (on next game launch)
```

**Note:** Registry method is less reliable and driver-version dependent.

---

## ADLX DLL Deployment

### Required DLL

**File:** `ADLX_3DSettings.dll`
**Source:** https://github.com/JamesCJ60/ADLX-SDK-Wrapper

### Installation Steps

1. **Build or Download ADLX DLL**
   - Clone the ADLX wrapper repository
   - Build the `ADLX 3D Settings` project (requires CMake + Visual Studio)
   - Or download pre-built DLL from releases (if available)

2. **Add to HUDRA Project**
   ```
   HUDRA/
   └── External Resources/
       └── AMD/
           └── ADLX/
               └── ADLX_3DSettings.dll
   ```

3. **Set Build Action in Visual Studio**
   - Right-click `ADLX_3DSettings.dll` in Solution Explorer
   - Properties → Build Action: **Content**
   - Copy to Output Directory: **Copy if newer**

4. **Verify Deployment**
   - Build HUDRA
   - Check output directory contains: `External Resources/AMD/ADLX/ADLX_3DSettings.dll`
   - Debug output should show: "Found ADLX DLL at: [path]"

### DLL Architecture Requirements

- **x64 only** - HUDRA must be built for x64 platform
- **Windows 10/11** - ADLX requires Windows 10 1809 or later
- **AMD GPU required** - ADLX functions return false on non-AMD systems

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

### 1. ADLX DLL Not Found
- **Behavior**: Falls back to registry method
- **Debug Log**: "ADLX DLL not found - will use registry fallback"
- **User Impact**: RSR still works via registry (less reliable)

### 2. ADLX DLL Load Failure
- **Causes**:
  - Wrong architecture (x86 DLL in x64 app)
  - Missing dependencies
  - Corrupted DLL
- **Debug Log**: "ADLX DLL architecture mismatch (x64 required)"
- **Behavior**: Falls back to registry method

### 3. GPU Not Available
- **Debug Log**: "Cannot apply RSR: No AMD GPU detected"
- **Behavior**: Toggle reverts automatically

### 4. ADLX Function Failure
- **Debug Log**: "ADLX: SetRSR returned false"
- **Behavior**: Attempts registry fallback, then reverts toggle

### 5. Registry Access Denied (Fallback Mode)
- **Debug Log**: "Registry access denied. App may need elevated permissions."
- **Behavior**: Toggle reverts, no RSR control available

---

## Implementation Advantages

### ADLX SDK Benefits (Primary Method)

✅ **Immediate effect** - No driver restart needed
✅ **Driver-agnostic** - Works across all AMD driver versions
✅ **Official API** - Supported by AMD
✅ **More reliable** - Direct driver communication
✅ **Future-proof** - AMD maintains compatibility

### Registry Fallback Benefits

✅ **No DLL dependency** - Works without ADLX
✅ **Zero deployment** - Registry is always available
⚠️ **Delayed effect** - Settings apply on game launch
⚠️ **Driver-dependent** - Registry keys may change

---

## Testing

### ADLX Path Testing

1. **Verify DLL Deployment**
   ```
   Debug Output: "Found ADLX DLL at: [path]"
   Debug Output: "ADLX initialized successfully. RSR supported: True"
   ```

2. **Toggle RSR**
   ```
   Debug Output: "ADLX: Setting RSR enabled=True, sharpness=80"
   Debug Output: "ADLX: Successfully set RSR enabled=True"
   Debug Output: "ADLX: Successfully set RSR sharpness=80"
   ```

3. **Verify State**
   ```
   Debug Output: "ADLX: RSR enabled=True, sharpness=80"
   ```

### Fallback Path Testing

1. **Remove ADLX DLL** (test fallback)
   ```
   Debug Output: "ADLX DLL not found - will use registry fallback"
   ```

2. **Toggle RSR**
   ```
   Debug Output: "Registry: Set RSR enabled=True, sharpness=80"
   ```

### Non-AMD System Testing

1. **Launch on Intel/NVIDIA**
   ```
   Debug Output: "No AMD GPU detected"
   Debug Output: "Cannot apply RSR: No AMD GPU detected"
   ```

2. **Verify Toggle Reverts**
   - Toggle should flip back to off automatically

---

## Building ADLX_3DSettings.dll

If you need to build the DLL yourself:

### Prerequisites
- Visual Studio 2019 or later
- CMake 3.15 or later
- AMD ADLX SDK headers (included in wrapper repo)

### Build Steps

1. **Clone ADLX Wrapper**
   ```bash
   git clone https://github.com/JamesCJ60/ADLX-SDK-Wrapper.git
   cd ADLX-SDK-Wrapper
   ```

2. **Build with CMake**
   ```bash
   cd "ADLX 3D Settings"
   mkdir build
   cd build
   cmake .. -A x64
   cmake --build . --config Release
   ```

3. **Output DLL**
   ```
   ADLX 3D Settings/build/Release/ADLX_3DSettings.dll
   ```

4. **Copy to HUDRA**
   ```
   Copy DLL to: HUDRA/External Resources/AMD/ADLX/
   ```

---

## Future Enhancements

- [ ] Add RSR sharpness slider control (currently hardcoded to 80%)
- [ ] Show AMD GPU info (model, driver version)
- [ ] Hide/disable control when no AMD GPU present
- [ ] Add more AMD features:
  - [ ] Radeon Anti-Lag
  - [ ] Radeon Boost
  - [ ] Radeon Chill
  - [ ] FreeSync control
- [ ] Per-game RSR profiles
- [ ] Visual feedback when settings apply
- [ ] User-facing error messages (instead of just debug logs)

---

## Troubleshooting

### RSR Not Working

1. **Check Debug Output**
   - Look for "ADLX initialized successfully"
   - If not found, check DLL deployment

2. **Verify DLL Exists**
   ```
   [HUDRA Install]/External Resources/AMD/ADLX/ADLX_3DSettings.dll
   ```

3. **Check Platform Target**
   - HUDRA must be built for **x64** (not AnyCPU or x86)

4. **Verify AMD GPU**
   - Debug output should show "AMD GPU detected"

5. **Check AMD Driver Version**
   - Update to latest AMD Adrenalin driver
   - ADLX requires driver from 2020 or later

### DLL Not Loading

**Error:** "ADLX DLL not found"

**Solutions:**
- Verify DLL path in project
- Check Build Action = Content
- Check Copy to Output Directory = Copy if newer
- Rebuild solution

**Error:** "ADLX DLL architecture mismatch"

**Solutions:**
- Build HUDRA for x64 platform
- Ensure ADLX_3DSettings.dll is 64-bit

---

## Code Files

| File | Purpose | Lines |
|------|---------|-------|
| `Services/AMD/AdlxWrapper.cs` | ADLX P/Invoke wrapper | ~230 |
| `Services/AmdAdlxService.cs` | AMD control service | ~400 |
| `Controls/AmdFeaturesControl.xaml` | RSR toggle UI | ~61 |
| `Controls/AmdFeaturesControl.xaml.cs` | Control logic | ~286 |
| `Pages/ScalingPage.xaml` | Page integration | Modified |
| `Pages/ScalingPage.xaml.cs` | State management | Modified |

---

## References

- [AMD ADLX SDK Wrapper (GitHub)](https://github.com/JamesCJ60/ADLX-SDK-Wrapper)
- [AMD GPUOpen ADLX Documentation](https://gpuopen.com/adl/)
- [AMD Radeon Super Resolution](https://www.amd.com/en/technologies/radeon-super-resolution)
- [P/Invoke in C#](https://docs.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke)

---

**Last Updated:** 2025-11-13
**Author:** Claude Code Agent
**Version:** 2.0 (ADLX SDK implementation with registry fallback)
