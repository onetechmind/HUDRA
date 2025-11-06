# Lenovo Legion Go - WMI Fan Speed Management

This document explains how the HandheldCompanion application manages fan speed on the Lenovo Legion Go device through Windows Management Instrumentation (WMI) calls.

## Overview

The Lenovo Legion Go exposes hardware control through proprietary WMI providers in the `root\WMI` namespace. The fan control implementation uses three different WMI classes to manage cooling behavior:

- `LENOVO_OTHER_METHOD` - Low-level hardware feature control
- `LENOVO_FAN_METHOD` - Fan curve table management
- `LENOVO_GAMEZONE_DATA` - Gaming performance mode control

## Implementation Location

**File**: `/HandheldCompanion/Devices/Lenovo/LegionGo.cs`

**Related Files**:
- `/HandheldCompanion/Devices/Lenovo/FanTable.cs` - Fan curve data structure
- `/HandheldCompanion/WMI.cs` - WMI utility wrapper

---

## WMI Capability IDs

Fan-related capability IDs are defined in the `CapabilityID` enum (LegionGo.cs:27-57):

```csharp
public enum CapabilityID
{
    FanFullSpeed = 0x04020000,           // Full speed override control
    CpuCurrentFanSpeed = 0x04030001,     // CPU fan speed reading
    GpuCurrentFanSpeed = 0x04030002,     // GPU fan speed reading
    CpuCurrentTemperature = 0x05040000,  // CPU temperature reading
    GpuCurrentTemperature = 0x05050000   // GPU temperature reading
}
```

## Fan Mode Enumeration

The Legion Go supports four fan modes (LegionGo.cs:19-25):

```csharp
public enum LegionMode
{
    Quiet = 0x01,         // Low noise, reduced performance
    Balanced = 0x02,      // Balance between noise and performance
    Performance = 0x03,   // Maximum cooling, higher noise
    Custom = 0xFF         // User-defined fan curve
}
```

---

## Function Reference

### 1. GetFanFullSpeed()

**Location**: LegionGo.cs:68-83

**Purpose**: Queries whether full-speed fan mode is currently enabled.

**WMI Details**:
```csharp
WMI.Call<bool>(
    scope: "root\\WMI",
    query: "SELECT * FROM LENOVO_OTHER_METHOD",
    methodName: "GetFeatureValue",
    methodParams: new() {
        { "IDs", 0x04020000 }  // CapabilityID.FanFullSpeed
    },
    resultSelector: pdc => Convert.ToInt32(pdc["Value"].Value) == 1
)
```

**Returns**:
- `true` - Full speed mode is enabled (fans at 100%)
- `false` - Full speed mode is disabled (normal operation)

**Error Handling**: Returns `false` on exception, logs error message.

---

### 2. SetFanFullSpeed(bool enabled)

**Location**: LegionGo.cs:85-102

**Purpose**: Enables or disables full-speed fan override mode.

**WMI Details**:
```csharp
WMI.Call(
    scope: "root\\WMI",
    query: "SELECT * FROM LENOVO_OTHER_METHOD",
    methodName: "SetFeatureValue",
    methodParams: new() {
        { "IDs", 0x04020000 },      // CapabilityID.FanFullSpeed
        { "value", enabled ? 1 : 0 } // 1 = enable, 0 = disable
    }
)
```

**Parameters**:
- `enabled` (bool): `true` to run fans at maximum speed, `false` for normal operation

**Behavior**:
- When enabled: Fans run at 100% speed regardless of temperature
- When disabled: Fans follow the active fan curve
- Automatically reset to `false` when the application closes (LegionGo.cs:307)

**Error Handling**: Logs error with enabled state on exception.

---

### 3. SetFanTable(FanTable fanTable)

**Location**: LegionGo.cs:104-117

**Purpose**: Uploads a custom fan curve (temperature-to-speed mapping) to the device firmware.

**WMI Details**:
```csharp
WMI.Call(
    scope: "root\\WMI",
    query: "SELECT * FROM LENOVO_FAN_METHOD",
    methodName: "Fan_Set_Table",
    methodParams: new() {
        { "FanTable", fanTable.GetBytes() }  // 64-byte array
    }
)
```

**Parameters**:
- `fanTable` (FanTable): A structure containing 10 fan speed values (0-100%)

**FanTable Structure** (FanTable.cs:6-88):

The FanTable is a readonly struct with the following layout:

| Field | Type | Size | Description |
|-------|------|------|-------------|
| FSTM | byte | 1 | Fan Speed Table Mode (always 1) |
| FSID | byte | 1 | Fan Speed ID (always 0) |
| FSTL | uint | 4 | Fan Speed Table Length (always 0) |
| FSS0-FSS9 | ushort × 10 | 20 | 10 fan speed values (0-100%) |
| (padding) | - | 38 | Zero padding to 64 bytes |

**Byte Serialization**:
```
Offset  Field     Size
------  -----     ----
0x00    FSTM      1 byte
0x01    FSID      1 byte
0x02    FSTL      4 bytes (uint, little-endian)
0x06    FSS0      2 bytes (ushort, little-endian)
0x08    FSS1      2 bytes
0x0A    FSS2      2 bytes
0x0C    FSS3      2 bytes
0x0E    FSS4      2 bytes
0x10    FSS5      2 bytes
0x12    FSS6      2 bytes
0x14    FSS7      2 bytes
0x16    FSS8      2 bytes
0x18    FSS9      2 bytes
0x1A-0x3F         38 bytes padding (zeros)
```

**Default Fan Table** (LegionGo.cs:346):
```csharp
new FanTable([44, 48, 55, 60, 71, 79, 87, 87, 100, 100])
```

**Error Handling**: Logs error with serialized fan table bytes on exception.

---

### 4. GetSmartFanMode()

**Location**: LegionGo.cs:119-134

**Purpose**: Retrieves the currently active fan mode from the device.

**WMI Details**:
```csharp
WMI.Call<int>(
    scope: "root\\WMI",
    query: "SELECT * FROM LENOVO_GAMEZONE_DATA",
    methodName: "GetSmartFanMode",
    methodParams: [],  // No parameters required
    resultSelector: pdc => Convert.ToInt32(pdc["Data"].Value)
)
```

**Returns**:
- `1` (0x01) - Quiet mode
- `2` (0x02) - Balanced mode
- `3` (0x03) - Performance mode
- `255` (0xFF) - Custom mode
- `-1` - Error occurred

**Error Handling**: Returns `-1` on exception, logs error message.

---

### 5. SetSmartFanMode(int fanMode)

**Location**: LegionGo.cs:136-149

**Purpose**: Sets the device to a specific fan mode (Quiet/Balanced/Performance).

**WMI Details**:
```csharp
WMI.Call(
    scope: "root\\WMI",
    query: "SELECT * FROM LENOVO_GAMEZONE_DATA",
    methodName: "SetSmartFanMode",
    methodParams: new() {
        { "Data", fanMode }  // 1=Quiet, 2=Balanced, 3=Performance, 0xFF=Custom
    }
)
```

**Parameters**:
- `fanMode` (int): Mode value from `LegionMode` enum

**Valid Values**:
- `1` - Quiet (low power/performance)
- `2` - Balanced (moderate power/performance)
- `3` - Performance (high power/performance)
- `255` - Custom (user-defined fan curve)

**Error Handling**: Logs error with fan mode value on exception.

---

### 6. PowerProfileManager_Applied()

**Location**: LegionGo.cs:347-371

**Purpose**: Automatically configures fan settings when a power profile is applied by the user.

**Logic Flow**:

```
PowerProfile Applied
        |
        v
   Is FanMode == Hardware?
        |
        ├─ NO ──> Extract 10 fan speeds from profile
        |         Create custom FanTable
        |         SetFanTable(custom)
        |
        └─ YES ─> SetFanTable(defaultFanTable)
        |
        v
   Get current fan mode
        |
        v
   Does it match profile.OEMPowerMode?
        |
        └─ NO ──> SetSmartFanMode(profile.OEMPowerMode)
```

**Implementation**:

```csharp
protected override void PowerProfileManager_Applied(PowerProfile profile, UpdateSource source)
{
    if (profile.FanProfile.fanMode != FanMode.Hardware)
    {
        // Custom fan control - use profile's fan curve
        ushort[] fanSpeeds = profile.FanProfile.fanSpeeds
            .Skip(1)      // Skip first element
            .Take(10)     // Take exactly 10 values
            .Select(speed => (ushort)speed)
            .ToArray();

        FanTable fanTable = new(fanSpeeds);
        SetFanTable(fanTable);
    }
    else
    {
        // Hardware control - restore defaults
        SetFanTable(defaultFanTable);
    }

    // Synchronize OEM power mode
    int currentFanMode = GetSmartFanMode();
    if (Enum.IsDefined(typeof(LegionMode), profile.OEMPowerMode)
        && currentFanMode != profile.OEMPowerMode)
    {
        SetSmartFanMode(profile.OEMPowerMode);
    }
}
```

**Behavior**:
1. **Software Fan Control**: When user enables custom fan curves, extracts 10 speed values from the profile and uploads them to the device
2. **Hardware Fan Control**: When user selects hardware mode, restores the default factory fan curve
3. **Mode Synchronization**: Ensures the Legion Go's power mode matches the selected profile (Quiet/Balanced/Performance)

**Power Profile Integration** (LegionGo.cs:204-239):

The Legion Go defines three default power profiles with associated fan modes:

| Profile | Legion Mode | TDP (W) | OS Power Mode | Description |
|---------|-------------|---------|---------------|-------------|
| Better Battery | Quiet (0x01) | 8W | BetterBattery | Low noise, extended battery |
| Better Performance | Balanced (0x02) | 15W | BetterPerformance | Balanced power/noise |
| Best Performance | Performance (0x03) | 25W | BestPerformance | Maximum cooling/performance |

---

## WMI Utility Methods

The `WMI` class (WMI.cs) provides wrapper methods for interacting with Windows Management Instrumentation:

### Void Call (line 166-179)

```csharp
public static void Call(
    string scope,                           // WMI namespace (e.g., "root\\WMI")
    string query,                           // WQL query to find WMI object
    string methodName,                      // Method to invoke
    Dictionary<string, object> methodParams // Method parameters
)
```

**Process**:
1. Uses `ManagementObjectSearcher` to query WMI namespace
2. Retrieves first matching `ManagementObject`
3. Gets method parameters schema via `GetMethodParameters()`
4. Populates parameters from dictionary
5. Invokes method via `InvokeMethod()`

### Generic Call<T> (line 181-195)

```csharp
public static T Call<T>(
    string scope,
    string query,
    string methodName,
    Dictionary<string, object> methodParams,
    Func<PropertyDataCollection, T> resultSelector  // Transform result
)
```

**Process**:
- Same as void Call, but processes the `ManagementBaseObject` result
- Passes result properties to `resultSelector` lambda
- Returns transformed value of type `T`

**Example Usage**:
```csharp
bool isEnabled = WMI.Call<bool>(
    "root\\WMI",
    "SELECT * FROM LENOVO_OTHER_METHOD",
    "GetFeatureValue",
    new() { { "IDs", 0x04020000 } },
    pdc => Convert.ToInt32(pdc["Value"].Value) == 1  // Transform to bool
);
```

---

## Device Lifecycle Integration

### Initialization (LegionGo.cs:198-261)

The constructor sets up device capabilities:
```csharp
Capabilities |= DeviceCapabilities.FanControl;    // Basic fan control
Capabilities |= DeviceCapabilities.FanOverride;   // Full-speed override
```

### Shutdown (LegionGo.cs:295-316)

Before device shutdown/restart:
```csharp
public override void Close()
{
    // Reset fan to normal operation
    SetFanFullSpeed(false);

    // ... other cleanup
    base.Close();
}
```

This ensures the device doesn't remain locked at 100% fan speed after application exit.

---

## Error Handling Strategy

All fan control methods follow a consistent error handling pattern:

1. **Try-Catch Wrapper**: All WMI calls are wrapped in try-catch blocks
2. **Error Logging**: Exceptions are logged with method name and relevant parameters
3. **Safe Defaults**:
   - Read methods return safe defaults (`false`, `-1`)
   - Write methods fail silently after logging
4. **No Throws**: Errors never propagate to caller

**Example**:
```csharp
try
{
    WMI.Call(/* ... */);
}
catch (Exception ex)
{
    LogManager.LogError("Error in SetFanFullSpeed: {0}, Enabled: {1}",
        ex.Message, enabled);
}
```

---

## Usage Examples

### Example 1: Enable Full Speed Mode

```csharp
LegionGo device = new LegionGo();

// Enable maximum fan speed
device.SetFanFullSpeed(true);

// Verify it's enabled
bool isFullSpeed = device.GetFanFullSpeed();
Console.WriteLine($"Full speed mode: {isFullSpeed}"); // True
```

### Example 2: Set Custom Fan Curve

```csharp
// Create a gentle fan curve (low noise)
ushort[] gentleCurve = { 30, 35, 40, 45, 50, 55, 60, 65, 70, 80 };
FanTable customTable = new FanTable(gentleCurve);

// Upload to device
device.SetFanTable(customTable);

// Set to custom mode
device.SetSmartFanMode((int)LegionMode.Custom);
```

### Example 3: Switch Power Mode

```csharp
// Check current mode
int currentMode = device.GetSmartFanMode();
Console.WriteLine($"Current mode: {(LegionMode)currentMode}");

// Switch to Performance mode
device.SetSmartFanMode((int)LegionMode.Performance);

// Restore default fan curve
FanTable defaultTable = new FanTable([44, 48, 55, 60, 71, 79, 87, 87, 100, 100]);
device.SetFanTable(defaultTable);
```

---

## Technical Notes

### WMI Provider Requirements

- **Operating System**: Windows (WMI is Windows-specific)
- **Driver**: Lenovo Legion Go drivers must be installed
- **Privileges**: May require administrator privileges for WMI writes

### Fan Curve Behavior

- The 10 fan speed values (FSS0-FSS9) correspond to temperature thresholds
- Exact temperature thresholds are firmware-defined (not exposed via WMI)
- Values are percentages (0-100)
- The device interpolates between points for smooth transitions

### Persistence

- Fan table changes are **non-persistent** (reset on reboot)
- Smart fan mode may persist depending on firmware behavior
- Application reapplies settings on startup via `PowerProfileManager_Applied()`

### Thread Safety

- WMI calls use `ManagementObjectSearcher` which is not thread-safe
- Fan control methods should be called from a single thread
- The PowerProfileManager ensures serialized access

---

## Troubleshooting

### Issue: WMI Calls Fail

**Symptoms**: LogManager shows "Error in SetFanFullSpeed" or similar messages

**Possible Causes**:
1. Lenovo Legion Go drivers not installed
2. WMI service disabled
3. Insufficient privileges
4. Device not detected

**Solutions**:
- Verify device detection via `IsReady()`
- Check Windows Event Viewer for WMI errors
- Run application as Administrator
- Reinstall Lenovo Legion Go drivers

### Issue: Fan Curve Not Applied

**Symptoms**: Fan speeds don't match custom curve

**Possible Causes**:
1. Full speed mode is enabled (overrides curve)
2. Wrong fan mode selected (not Custom)
3. Invalid fan speed values

**Solutions**:
```csharp
// Disable full speed override
device.SetFanFullSpeed(false);

// Set to custom mode
device.SetSmartFanMode((int)LegionMode.Custom);

// Verify values are 0-100
ushort[] validatedCurve = fanSpeeds.Select(s => Math.Clamp(s, 0, 100)).ToArray();
device.SetFanTable(new FanTable(validatedCurve));
```

### Issue: Mode Switches Don't Work

**Symptoms**: `GetSmartFanMode()` returns old value after `SetSmartFanMode()`

**Possible Causes**:
1. Firmware lag (mode change takes time)
2. Invalid mode value
3. Hardware override active

**Solutions**:
- Add small delay after mode change
- Verify mode value is in `LegionMode` enum
- Check if another application is controlling fans

---

## Related Documentation

- **PowerProfile System**: See `/HandheldCompanion/Managers/PowerProfileManager.cs`
- **Device Capabilities**: See `/HandheldCompanion/Shared/DeviceCapabilities.cs`
- **Fan Profile Types**: See `/HandheldCompanion/Managers/FanMode.cs`

---

## Revision History

| Date | Version | Changes |
|------|---------|---------|
| 2025-11-06 | 1.0 | Initial documentation |
