# HUDRA Fan Control & Temperature Monitoring Technical Specification

## Overview
The Fan Control & Temperature Monitoring system provides advanced thermal management capabilities for handheld gaming devices, specifically designed around WinUI 3 architecture. The system integrates hardware-level fan control with real-time temperature monitoring through a dedicated **FanCurvePage** to deliver automated thermal regulation based on user-defined curves and preset configurations.

## Architecture

### Core Components

#### 1. **Temperature Monitoring Service** (`TemperatureMonitorService.cs`)
**Purpose**: Real-time hardware temperature acquisition and monitoring with LibreHardwareMonitor integration

**Primary Implementation**:
- **LibreHardwareMonitor Integration**: Direct hardware sensor access for CPU and GPU temperatures
- **Fallback Strategy**: Multi-tier fallback system ensuring compatibility across diverse hardware
- **Update Frequency**: 2-second polling intervals with differential change detection (>1°C threshold)
- **Data Sources**: CPU, GPU sensors with automatic source prioritization

**Fallback Hierarchy**:
1. **LibreHardwareMonitor** (Primary) - Direct hardware sensor access
2. **WMI Thermal Zones** (`MSAcpi_ThermalZoneTemperature`)
3. **Temperature Probes** (`Win32_TemperatureProbe`)
4. **OpenHardwareMonitor WMI** (if available)
5. **CPU Usage Estimation** (final fallback)

**Key Features**:
```csharp
// Primary sensor reading with LibreHardwareMonitor
private Computer? _computer;
_computer = new Computer
{
    IsCpuEnabled = true,
    IsGpuEnabled = true,
    IsMemoryEnabled = false,
    IsMotherboardEnabled = false
};
```

#### 2. **Fan Control Service** (`FanControlService.cs`)
**Purpose**: Hardware abstraction layer for fan speed control with temperature-based automation

**Core Functionality**:
- **Device Detection**: Automatic hardware detection via `DeviceDetectionService`
- **Control Modes**: Hardware (automatic) and Software (manual) modes
- **Temperature Integration**: Automatic fan speed adjustment based on curve interpolation
- **Real-time Feedback**: Status monitoring with 2-second update cycles

**Temperature Control Integration**:
```csharp
public void EnableTemperatureControl(TemperatureMonitorService temperatureMonitor)
{
    _temperatureMonitor = temperatureMonitor;
    _temperatureMonitor.TemperatureChanged += OnTemperatureChanged;
    _temperatureControlEnabled = true;
}
```

#### 3. **Device Abstraction Layer** (`Services/FanControl/`)
**Purpose**: Hardware-specific fan control implementations

**Base Classes**:
- `ECCommunicationBase`: Low-level embedded controller communication using OpenLibSys
- `IFanControlDevice`: Device interface contract
- `FanControlTypes`: Type definitions and data structures
- `DeviceDetectionService`: Automatic device detection and initialization

**Device Support**:
- `OneXPlayerX1Device`: Specialized support for OneXPlayer X1 handheld series with embedded controller communication
- Extensible architecture for additional device types

**OneXPlayer X1 Implementation**:
```csharp
public ECRegisterMap RegisterMap { get; } = new ECRegisterMap
{
    FanControlAddress = 0x44A,
    FanDutyAddress = 0x44B,
    StatusCommandPort = 0x4E,
    DataPort = 0x4F,
    FanValueMin = 0,
    FanValueMax = 184
};
```

#### 4. **Fan Curve Page** (`Pages/FanCurvePage.xaml`)
**Purpose**: Dedicated page for fan curve management and thermal control

**Implementation**:
- **Dedicated Navigation**: Separate page accessible via main navigation
- **Centralized Control**: Houses the `FanCurveControl` with proper initialization
- **Event Handling**: Manages fan curve change events and status logging

```csharp
public void Initialize()
{
    FanCurveControl.Initialize();
    SetupFanCurveEventHandling();
    _isInitialized = true;
}
```

#### 5. **Fan Curve Control** (`FanCurveControl.xaml`)
**Purpose**: Interactive UI for fan curve definition, visualization, and preset management

**Technical Implementation**:
- **Canvas Rendering**: Custom WinUI 3 Canvas with real-time curve visualization
- **Touch/Mouse Input**: Unified pointer handling for curve point manipulation with touch-optimized interaction
- **Interpolation**: Linear interpolation between user-defined control points
- **Visual Feedback**: Real-time temperature indicators and fan speed feedback
- **Preset System**: Four preset configurations with visual state management

## Fan Curve Preset System

### Preset Configurations
The system includes four built-in fan curve presets:

#### 1. **Stealth Preset** - Silent Operation
- **Purpose**: Minimal fan noise for quiet environments
- **Temperature Points**: 30°C→20%, 40°C→30%, 55°C→40%, 70°C→60%, 85°C→80%
- **Use Case**: Office work, media consumption, light gaming

#### 2. **Cruise Preset** - Balanced Performance
- **Purpose**: Balanced cooling with moderate noise
- **Temperature Points**: 30°C→20%, 40°C→30%, 55°C→50%, 70°C→75%, 85°C→100%
- **Use Case**: General gaming, productivity tasks

#### 3. **Warp Preset** - Maximum Cooling
- **Purpose**: Aggressive cooling for high-performance scenarios
- **Temperature Points**: 30°C→30%, 40°C→45%, 55°C→70%, 70°C→90%, 85°C→100%
- **Use Case**: Intensive gaming, benchmarking, sustained high loads

#### 4. **Custom Preset** - User-Defined Configuration
- **Purpose**: User-created curve saved separately from presets
- **Functionality**: Loads user's custom curve points from separate settings storage
- **Persistence**: Maintains user modifications independently of preset system

### Preset Button Implementation
```csharp
private void ApplyPreset(FanCurvePreset preset)
{
    // Update curve points
    _currentCurve.Points = preset.Points.Select(p => new FanCurvePoint
    {
        Temperature = p.Temperature,
        FanSpeed = p.FanSpeed
    }).ToArray();
    
    // Track active preset
    _currentCurve.ActivePreset = preset.Name;
    
    // Update UI and save settings
    UpdatePresetButtonStates();
    SettingsService.SetFanCurve(_currentCurve);
}
```

**Visual State Management**:
- **Active State**: DarkViolet background with white text
- **Inactive State**: Semi-transparent gray background with muted white text
- **Real-time Updates**: Button states update immediately upon preset selection or manual curve modification

## Technical Implementation Details

### Temperature Monitoring Architecture

#### LibreHardwareMonitor Integration
```csharp
private TemperatureData ReadTemperaturesFromLibreHardware()
{
    foreach (var hardware in _computer.Hardware)
    {
        hardware.Update();
        if (hardware.HardwareType == HardwareType.Cpu)
        {
            var cpuTemps = hardware.Sensors
                .Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue)
                .Select(s => s.Value.Value)
                .Where(temp => temp > 20 && temp < 100);
                
            if (cpuTemps.Any())
                result.CpuTemperature = cpuTemps.Max();
        }
    }
}
```

### Fan Control Architecture

#### Hardware Communication (OneXPlayer X1)
```csharp
public bool SetFanDuty(double percent)
{
    if (_currentMode != FanControlMode.Software)
    {
        SetFanControl(FanControlMode.Software);
    }
    
    percent = Math.Clamp(percent, 0.0, 100.0);
    byte dutyValue = PercentageToDuty(percent, RegisterMap.FanValueMin, RegisterMap.FanValueMax);
    
    return WriteECRegister(RegisterMap.FanDutyAddress, RegisterMap, dutyValue);
}
```

#### Curve Interpolation Algorithm
```csharp
private double InterpolateFanSpeed(double temperature)
{
    var points = _currentCurve.Points.OrderBy(p => p.Temperature).ToArray();
    
    for (int i = 0; i < points.Length - 1; i++)
    {
        var point1 = points[i];
        var point2 = points[i + 1];
        
        if (temperature >= point1.Temperature && temperature <= point2.Temperature)
        {
            var tempRange = point2.Temperature - point1.Temperature;
            var speedRange = point2.FanSpeed - point1.FanSpeed;
            var tempOffset = temperature - point1.Temperature;
            
            return point1.FanSpeed + (speedRange * (tempOffset / tempRange));
        }
    }
}
```

### Touch-Optimized UI Implementation

#### Enhanced Touch Handling
```csharp
private void UpdateTooltip(double temperature, double fanSpeed, Point pointPosition)
{
    // FINGER-FRIENDLY: Much higher offset to clear finger area
    var verticalOffset = _isTouchDragging ? -45 : -15; // 45px above for touch, 15px for mouse
    var tooltipX = gridPointX - (tooltipWidth / 2);
    var tooltipY = gridPointY + verticalOffset - tooltipHeight;
}
```

**Touch Interaction Features**:
- **Larger Hit Targets**: Extended touch tolerance for curve points
- **Finger Clearance**: Tooltips positioned well above touch points
- **Gesture Prevention**: Manipulation mode disabled to prevent parent scroll interference
- **Pointer Capture**: Ensures smooth dragging without losing focus

## Integration Architecture

### Application-Level Integration (`App.xaml.cs`)
```csharp
protected override async void OnLaunched(LaunchActivatedEventArgs args)
{
    TemperatureMonitor = new TemperatureMonitorService(MainWindow.DispatcherQueue);
    FanControlService = new FanControlService(MainWindow.DispatcherQueue);
    
    // Enable fan control if fan curve is enabled in settings
    var fanCurve = SettingsService.GetFanCurve();
    if (fanCurve.IsEnabled)
    {
        FanControlService.EnableTemperatureControl(TemperatureMonitor);
    }
}
```

### Navigation Integration (`MainWindow.xaml.cs`)
```csharp
private void InitializeFanCurvePage()
{
    if (_fanCurvePage == null) return;
    
    try
    {
        _fanCurvePage.Initialize();
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"ERROR in InitializeFanCurvePage: {ex.Message}");
    }
}
```

### Settings Persistence
```csharp
public static FanCurve GetFanCurve()
{
    var isEnabled = GetBooleanSetting(FanCurveEnabledKey, false);
    var pointsJson = GetStringSetting(FanCurvePointsKey, "");
    var activePreset = GetStringSetting(FanCurveActivePresetKey, "");
    
    return new FanCurve
    {
        IsEnabled = isEnabled,
        Points = JsonSerializer.Deserialize<FanCurvePoint[]>(pointsJson) ?? GetDefaultPoints(),
        ActivePreset = activePreset
    };
}
```

## Performance Characteristics

### Resource Usage
- **Temperature Monitoring**: 2-second polling with change-based updates (>1°C threshold)
- **Fan Control**: Hardware-level communication with minimal CPU overhead
- **UI Rendering**: Optimized Canvas updates during curve editing only
- **Memory Usage**: LibreHardwareMonitor managed lifecycle with proper disposal

### Safety Features
- **Temperature Bounds**: 20°C - 100°C validation with sensor filtering
- **Fan Speed Limits**: 0-100% with device-specific constraints (OneXPlayer: 0-184 raw values)
- **Error Handling**: Graceful degradation with fallback temperature estimation
- **State Persistence**: Automatic curve saving and restoration with preset tracking

### Real-time Temperature Control
```csharp
private void OnTemperatureChanged(object? sender, TemperatureChangedEventArgs e)
{
    UpdateTemperatureDisplay(e.TemperatureData);
    
    if (_temperatureControlEnabled && _currentCurve.IsEnabled)
    {
        ApplyTemperatureBasedFanControl(e.TemperatureData.MaxTemperature);
    }
}
```

## Device Compatibility

### Supported Hardware
- **OneXPlayer X1 Series**: Full fan control and temperature monitoring via embedded controller
- **Generic Devices**: Temperature monitoring via LibreHardwareMonitor
- **Extensible Framework**: Easy addition of new device types through `IFanControlDevice` interface

### System Requirements
- **Windows 10/11**: Version 1809 or later
- **Admin Privileges**: Required for low-level hardware access (EC communication)
- **WinUI 3**: Windows App SDK 1.5 or later
- **.NET 8**: Runtime dependency
- **LibreHardwareMonitor**: Embedded for temperature sensor access

### Error Handling Strategy
```csharp
public async Task<FanControlResult> InitializeAsync()
{
    try
    {
        _device = DeviceDetectionService.DetectDevice();
        
        if (_device == null)
        {
            return FanControlResult.FailureResult("No supported fan control device detected");
        }
        
        StartStatusMonitoring();
        return FanControlResult.SuccessResult($"Fan control initialized: {DeviceInfo}");
    }
    catch (Exception ex)
    {
        return FanControlResult.FailureResult($"Failed to initialize: {ex.Message}", ex);
    }
}
```

## File Structure
```
HUDRA/
├── Pages/
│   └── FanCurvePage.xaml                    # Dedicated fan curve page
│   └── FanCurvePage.xaml.cs                 # Page initialization and event handling
├── Controls/
│   └── FanCurveControl.xaml                 # Interactive fan curve editor
│   └── FanCurveControl.xaml.cs              # Canvas rendering and touch handling
├── Services/
│   ├── FanControlService.cs                 # Main fan control orchestration
│   ├── TemperatureMonitorService.cs         # Temperature monitoring with LibreHW
│   └── FanControl/
│       ├── DeviceDetectionService.cs        # Automatic device detection
│       ├── IFanControlDevice.cs             # Device interface
│       ├── ECCommunicationBase.cs           # Low-level EC communication
│       └── Devices/
│           └── OneXPlayerX1Device.cs        # OneXPlayer X1 implementation
└── App.xaml.cs                              # Global service initialization
```

This specification provides a comprehensive foundation for thermal management in handheld gaming devices, combining real-time monitoring with intelligent fan control through an intuitive WinUI 3 interface, enhanced with preset management and touch-optimized interactions.