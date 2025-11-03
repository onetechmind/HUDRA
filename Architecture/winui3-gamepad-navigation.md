# WinUI 3 Gamepad Navigation System

## Overview

This document provides a comprehensive guide to the gamepad navigation system implemented in HUDRA for AMD Ryzen handheld gaming devices. The system enables full UI control using gamepad input, essential for handheld gaming devices where touch/mouse input may be unavailable or inconvenient during gaming sessions.

**Key Features:**
- Full D-pad navigation between controls
- L1/R1 shoulder button page navigation  
- A button activation for controls
- B button for back/cancel operations
- Visual focus indicators (DarkViolet borders)
- Slider value adjustment with D-pad
- ComboBox navigation with item preview
- Automatic focus management on page transitions
- Non-gamepad input detection to clear focus

## Problem Context

Handheld gaming devices like the ROG Ally, Legion Go, and Steam Deck primarily use gamepad controls. Users need to adjust performance settings (TDP, fan curves, resolution) without reaching for touch controls or connecting external input devices. The navigation system must be:

- **Intuitive**: Follow standard gamepad navigation patterns
- **Visual**: Clear indication of focused elements
- **Responsive**: Immediate feedback for all inputs
- **Reliable**: Consistent behavior across all controls and pages
- **Non-intrusive**: Automatically disable when using mouse/touch

## Architecture

### Core Components

```
┌─────────────────────────────────────────────────┐
│           GamepadNavigationService              │
│  - Gamepad polling (60Hz)                      │
│  - Input processing & repeat handling          │
│  - Focus management                            │
│  - Page navigation coordination                │
└────────────┬────────────────────────────────────┘
             │
             ├──────────────┬──────────────┬─────────────┐
             │              │              │             │
┌────────────▼───────┐ ┌───▼──────┐ ┌────▼──────┐ ┌───▼────────┐
│ IGamepadNavigable  │ │ Gamepad  │ │ Attached  │ │Navigation  │
│    Interface       │ │Navigation│ │Properties │ │  Helper    │
│ - Navigation flags │ │   .cs    │ │   System  │ │   Class    │
│ - Event handlers   │ └──────────┘ └───────────┘ └────────────┘
│ - Focus properties │
└────────────────────┘
```

### Key Classes and Interfaces

1. **GamepadNavigationService** (`Services/GamepadNavigationService.cs`)
   - Central service managing all gamepad input
   - Polls gamepad state at 60Hz
   - Handles input repeat delays
   - Manages focus state across controls

2. **IGamepadNavigable** (`Interfaces/IGamepadNavigable.cs`)
   - Interface implemented by all navigable controls
   - Defines navigation capabilities and event handlers
   - Provides focus visualization properties

3. **GamepadNavigation** (`AttachedProperties/GamepadNavigation.cs`)
   - WinUI 3 attached properties for XAML configuration
   - Enables navigation on controls via XAML
   - Manages navigation groups and order

4. **GamepadNavigationHelper** (`AttachedProperties/GamepadNavigation.cs`)
   - Helper class connecting attached properties to IGamepadNavigable
   - Handles focus state changes
   - Manages control lifecycle events

## Implementation Details

### IGamepadNavigable Interface

```csharp
public interface IGamepadNavigable
{
    // Navigation capability flags
    bool CanNavigateUp { get; }
    bool CanNavigateDown { get; }
    bool CanNavigateLeft { get; }
    bool CanNavigateRight { get; }
    bool CanActivate { get; }
    
    // Navigation element reference
    FrameworkElement NavigationElement { get; }
    
    // Slider-specific properties
    bool IsSlider { get; }
    bool IsSliderActivated { get; set; }
    void AdjustSliderValue(int direction);
    
    // ComboBox-specific properties
    bool HasComboBoxes { get; }
    bool IsComboBoxOpen { get; set; }
    ComboBox? GetFocusedComboBox();
    int ComboBoxOriginalIndex { get; set; }
    bool IsNavigatingComboBox { get; set; }
    void ProcessCurrentSelection();
    
    // Event handlers
    void OnGamepadNavigateUp();
    void OnGamepadNavigateDown();
    void OnGamepadNavigateLeft();
    void OnGamepadNavigateRight();
    void OnGamepadActivate();
    void OnGamepadFocusReceived();
    void OnGamepadFocusLost();
}
```

### Focus Visualization

All navigable controls implement focus visualization using a DarkViolet border:

```csharp
public Brush FocusBorderBrush
{
    get
    {
        if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true)
        {
            return new SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
        }
        return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }
}
```

**Important**: Controls with multiple focusable elements may use different colors:
- **DarkViolet**: Standard focused control
- **DodgerBlue**: Activated slider (adjusting value)
- **MediumOrchid**: Selected preset button

### Lazy Initialization Pattern

To handle initialization timing issues, controls use lazy initialization for the gamepad service:

```csharp
public void OnGamepadFocusReceived()
{
    // Lazy initialization of gamepad service if needed
    if (_gamepadNavigationService == null)
    {
        InitializeGamepadNavigationService();
    }
    
    _isFocused = true;
    UpdateFocusVisuals();
}

private void InitializeGamepadNavigationService()
{
    if (Application.Current is App app && app.MainWindow is MainWindow mainWindow)
    {
        _gamepadNavigationService = mainWindow.GamepadNavigationService;
    }
}
```

**Critical**: This pattern ensures the service is available even when controls are initialized before the service or when property getters are called during initial rendering.

## Control-Specific Implementations

### Slider Controls (TDP, Brightness, Audio)

Sliders support two modes:
1. **Navigation Mode**: D-pad moves between controls
2. **Adjustment Mode**: D-pad adjusts slider value

```csharp
// TdpPickerControl implementation
public void OnGamepadActivate()
{
    if (!IsSliderActivated)
    {
        // Enter adjustment mode
        IsSliderActivated = true;
        _gamepadNavigationService?.ActivateSlider(this);
    }
    else
    {
        // Exit adjustment mode  
        IsSliderActivated = false;
        _gamepadNavigationService?.DeactivateSlider();
    }
}

public void AdjustSliderValue(int direction)
{
    double increment = 1.0; // 1W for TDP
    double newValue = Math.Clamp(_currentTdp + direction, 5, 30);
    UpdateTdpValue(newValue);
}
```

**Visual Feedback**: 
- Normal focus: DarkViolet border
- Adjustment mode: DodgerBlue border

### ComboBox Controls (Resolution, FPS Limiter)

ComboBoxes implement special navigation to preview items without committing:

```csharp
public void OnGamepadNavigateDown()
{
    if (!IsComboBoxOpen)
    {
        // Open ComboBox and save original selection
        var comboBox = GetFocusedComboBox();
        if (comboBox != null)
        {
            ComboBoxOriginalIndex = comboBox.SelectedIndex;
            IsComboBoxOpen = true;
            comboBox.IsDropDownOpen = true;
        }
    }
    else
    {
        // Navigate to next item
        NavigateComboBoxItem(1);
    }
}

private void NavigateComboBoxItem(int direction)
{
    var comboBox = GetFocusedComboBox();
    if (comboBox != null)
    {
        int newIndex = Math.Clamp(
            comboBox.SelectedIndex + direction,
            0, 
            comboBox.Items.Count - 1
        );
        comboBox.SelectedIndex = newIndex;
    }
}
```

**Behavior**:
- **D-pad Down**: Opens ComboBox
- **D-pad Up/Down** (when open): Navigate items with preview
- **A button**: Confirm selection
- **B button**: Cancel and restore original selection

### Toggle Controls with Buttons (Fan Curve)

The FanCurveControl demonstrates a complex multi-element control pattern with both toggle and button navigation. This serves as a reusable template for other controls with multiple interactive elements.

#### Architecture Pattern

The control manages internal navigation between different focusable elements within a single IGamepadNavigable implementation:

```csharp
private int _currentFocusedElement = 0; // 0=Toggle, 1-4=Preset Buttons

// Enable navigation capabilities based on current focus state
public bool CanNavigateUp => _currentFocusedElement >= 1; // From buttons to toggle
public bool CanNavigateDown => _currentFocusedElement == 0 && PresetButtonsPanel?.Visibility == Visibility.Visible; // From toggle to buttons
public bool CanNavigateLeft => _currentFocusedElement > 1; // Between buttons
public bool CanNavigateRight => _currentFocusedElement == 0 || (_currentFocusedElement >= 1 && _currentFocusedElement < 4); // Alternative to down navigation
```

#### Navigation Implementation

**Multi-directional Navigation:**
```csharp
public void OnGamepadNavigateDown() 
{
    if (_currentFocusedElement == 0 && PresetButtonsPanel?.Visibility == Visibility.Visible)
    {
        _currentFocusedElement = 1; // Navigate to first button
        UpdateFocusVisuals();
    }
}

public void OnGamepadNavigateUp() 
{
    if (_currentFocusedElement >= 1) // From any button back to toggle
    {
        _currentFocusedElement = 0;
        UpdateFocusVisuals();
    }
}

public void OnGamepadNavigateRight()
{
    if (_currentFocusedElement == 0 && PresetButtonsPanel?.Visibility == Visibility.Visible)
    {
        _currentFocusedElement = 1; // Alternative path to buttons
    }
    else if (_currentFocusedElement >= 1 && _currentFocusedElement < 4)
    {
        _currentFocusedElement++; // Move between buttons
    }
    UpdateFocusVisuals();
}

public void OnGamepadNavigateLeft()
{
    if (_currentFocusedElement > 1) // Move backwards through buttons
    {
        _currentFocusedElement--;
        UpdateFocusVisuals();
    }
}
```

#### Button Activation Pattern

Each focusable element maps to its corresponding action:

```csharp
public void OnGamepadActivate()
{
    switch (_currentFocusedElement)
    {
        case 0: // Toggle
            FanCurveToggle.IsOn = !FanCurveToggle.IsOn;
            break;
        case 1: // Stealth preset
            StealthPreset_Click(StealthPresetButton, new RoutedEventArgs());
            break;
        case 2: // Cruise preset
            CruisePreset_Click(CruisePresetButton, new RoutedEventArgs());
            break;
        case 3: // Warp preset
            WarpPreset_Click(WarpPresetButton, new RoutedEventArgs());
            break;
        case 4: // Custom preset
            CustomPresetButton_Click(CustomPresetButton, new RoutedEventArgs());
            break;
    }
}
```

#### Visual Focus System

The control implements sophisticated focus visualization for multiple element types:

**XAML Structure with Named Borders:**
```xml
<!-- Fan Curve Toggle with focus border -->
<Border BorderBrush="{x:Bind ToggleFocusBrush, Mode=OneWay}"
        BorderThickness="2" CornerRadius="12">
    <ToggleSwitch x:Name="FanCurveToggle" ... />
</Border>

<!-- Individual button focus borders -->
<Border x:Name="StealthPresetBorder" BorderThickness="0" CornerRadius="6">
    <Button x:Name="StealthPresetButton" ... />
</Border>
<Border x:Name="CruisePresetBorder" BorderThickness="0" CornerRadius="6">
    <Button x:Name="CruisePresetButton" ... />
</Border>
<!-- Additional preset buttons... -->
```

**Dynamic Focus Property Management:**
```csharp
public Brush ToggleFocusBrush
{
    get
    {
        bool shouldShowFocus = IsFocused && 
                              _gamepadNavigationService?.IsGamepadActive == true && 
                              _currentFocusedElement == 0;
        
        return shouldShowFocus 
            ? new SolidColorBrush(Microsoft.UI.Colors.DarkViolet)
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }
}
```

**Individual Button Focus Management:**
```csharp
private void UpdatePresetButtonFocusStates()
{
    // Reset all buttons first
    SetPresetButtonFocus(StealthPresetButton, false);
    SetPresetButtonFocus(CruisePresetButton, false);
    SetPresetButtonFocus(WarpPresetButton, false);
    SetPresetButtonFocus(CustomPresetButton, false);
    
    // Set focus on current element
    if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true)
    {
        switch (_currentFocusedElement)
        {
            case 1: SetPresetButtonFocus(StealthPresetButton, true); break;
            case 2: SetPresetButtonFocus(CruisePresetButton, true); break;
            case 3: SetPresetButtonFocus(WarpPresetButton, true); break;
            case 4: SetPresetButtonFocus(CustomPresetButton, true); break;
        }
    }
}

private void SetPresetButtonFocus(Button? button, bool hasFocus)
{
    if (button == null) return;
    
    var borderBrush = hasFocus ? new SolidColorBrush(Microsoft.UI.Colors.DarkViolet) : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    var borderThickness = hasFocus ? new Thickness(2) : new Thickness(0);
    
    // Map buttons to their named borders for precise control
    Border? border = button.Name switch
    {
        "StealthPresetButton" => StealthPresetBorder,
        "CruisePresetButton" => CruisePresetBorder,
        "WarpPresetButton" => WarpPresetBorder,
        "CustomPresetButton" => CustomPresetBorder,
        _ => button.Parent as Border
    };
    
    if (border != null)
    {
        border.BorderBrush = borderBrush;
        border.BorderThickness = borderThickness;
    }
}
```

#### Navigation Flow Visualization

**Fan Curve Page Navigation Map:**
```
┌─────────────────┐
│ Custom Fan      │◄── Focus starts here (element 0)
│ Curve Toggle    │
└─────────────────┘
         │ Down/Right
         ▼
┌─────────────────────────────────────────────────────┐
│ [Stealth] [Cruise] [Warp] [Custom]                 │◄── Button row (elements 1-4)
│     1        2       3       4                     │    Left/Right navigation
└─────────────────────────────────────────────────────┘
         ▲
         │ Up - Return to toggle
```

#### Expandable Design Pattern

This implementation provides a template for other multi-element controls:

**Applicability Examples:**
- **Settings Page**: Apply/Reset/Default buttons
- **Audio Controls**: EQ preset buttons  
- **Display Modes**: Gaming/Desktop/Battery buttons
- **Profile Management**: Save/Load/Delete buttons

**Reusable Components:**
1. **State Management**: `_currentFocusedElement` index tracking
2. **Navigation Capabilities**: Dynamic `CanNavigate*` properties based on state
3. **Focus Mapping**: Named border containers for precise visual control
4. **Activation Pattern**: Switch-based element activation
5. **Visual Updates**: Centralized `UpdateFocusVisuals()` method

**Implementation Benefits:**
- **Intuitive Navigation**: Multiple paths to reach elements (Down or Right to buttons)
- **Visual Clarity**: Each element has distinct focus indication
- **Extensible**: Easy to add/remove buttons or change navigation flow
- **Performance**: Minimal overhead with efficient state tracking
- **Accessibility**: Clear visual feedback for all focusable elements

## Navigation Flow

### Page Navigation (L1/R1)

Shoulder buttons navigate between pages in a fixed order:

```csharp
// In GamepadNavigationService
if (HasButton(newButtons, GamepadButtons.LeftShoulder))
{
    _gamepadPageNavigationRequested?.Invoke(this, 
        new GamepadPageNavigationEventArgs(NavigationDirection.Previous));
}
else if (HasButton(newButtons, GamepadButtons.RightShoulder))
{
    _gamepadPageNavigationRequested?.Invoke(this, 
        new GamepadPageNavigationEventArgs(NavigationDirection.Next));
}

// In MainWindow - Page order
var pageOrder = new List<Type>
{
    typeof(MainPage),
    typeof(FanCurvePage),
    typeof(ScalingPage),
    typeof(SettingsPage)
};
```

### Control Navigation (D-pad)

Within a page, D-pad navigates between controls based on their capabilities:

```csharp
private void HandleNavigationAction(NavigationAction action)
{
    var currentElement = _currentFocusedElement;
    if (currentElement == null) return;

    // Check if element implements IGamepadNavigable
    if (currentElement is IGamepadNavigable navigable)
    {
        switch (action)
        {
            case NavigationAction.Up:
                if (navigable.CanNavigateUp)
                    navigable.OnGamepadNavigateUp();
                else
                    NavigateToNextControl(NavigationDirection.Up);
                break;
            // ... other directions
        }
    }
}
```

### Input Repeat Handling

The system implements intelligent repeat delays:

```csharp
private const int INPUT_REPEAT_DELAY_MS = 200; // Initial delay
private const int INPUT_REPEAT_RATE_MS = 50;   // Repeat rate

private void ProcessGamepadInput(GamepadReading reading)
{
    var newButtons = GetNewlyPressedButtons(reading.Buttons);
    bool shouldProcessRepeats = 
        (DateTime.Now - _lastInputTime).TotalMilliseconds >= INPUT_REPEAT_DELAY_MS;
    
    if (newButtons.Count > 0 || shouldProcessRepeats)
    {
        ProcessNavigationInput(reading, newButtons, shouldProcessRepeats);
        _lastInputTime = DateTime.Now;
    }
}
```

## Focus Management

### Automatic Focus on Page Load

When navigating to a page, the first navigable control receives focus:

```csharp
// In MainWindow.InitializeFanCurvePage()
private void InitializeFanCurvePage()
{
    if (_fanCurvePage == null) return;
    
    _fanCurvePage.Initialize();
    
    // Initialize gamepad navigation for the page
    _gamepadNavigationService.InitializePageNavigation(_fanCurvePage.FanCurveControl);
}

// In GamepadNavigationService
public void InitializePageNavigation(FrameworkElement rootElement)
{
    ClearFocus(); // Clear any existing focus
    
    var navigableElements = GamepadNavigation.GetNavigableElements(rootElement);
    if (navigableElements.Count > 0 && _isGamepadActive)
    {
        SetFocus(navigableElements[0]);
    }
}
```

### Focus Clearing on Non-Gamepad Input

Focus automatically clears when mouse or keyboard input is detected:

```csharp
private void OnGlobalPointerPressed(object sender, PointerRoutedEventArgs e)
{
    var pointer = e.GetCurrentPoint(null);
    if (pointer.PointerDeviceType == PointerDeviceType.Mouse ||
        pointer.PointerDeviceType == PointerDeviceType.Touch)
    {
        if (_isGamepadActive)
        {
            DeactivateGamepadMode();
            ClearFocus();
        }
    }
}
```

## Common Issues and Solutions

### Issue 1: Focus Border Not Appearing on Page Navigation (CRITICAL)

**Problem**: When navigating to a page via L1/R1, the focus border doesn't appear until D-pad is pressed.

**Root Cause Analysis (Multiple Issues)**:

1. **Missing Constructor Initialization**: Controls implementing `IGamepadNavigable` must call `InitializeGamepadNavigation()` in their constructor to set up attached properties.

2. **Root Element Detection Bug**: The `GetNavigableElements()` method only examined child elements, never the root element itself. When calling `InitializePageNavigation(controlInstance)`, the control instance was never considered as a navigable element.

3. **Timing Issues**: Gamepad navigation setup occurs after page initialization in some cases.

**Complete Solution**:

**Step 1**: Ensure gamepad navigation setup in control constructor:
```csharp
public FanCurveControl()
{
    this.InitializeComponent();
    this.DataContext = this;
    InitializeDefaultCurve();
    InitializeGamepadNavigation(); // CRITICAL: Must call this!
}

private void InitializeGamepadNavigation()
{
    GamepadNavigation.SetIsEnabled(this, true);
    GamepadNavigation.SetNavigationGroup(this, "MainControls");
    GamepadNavigation.SetNavigationOrder(this, 6);
}
```

**Step 2**: Fix GetNavigableElements to check root element (Fixed in AttachedProperties/GamepadNavigation.cs):
```csharp
public static IEnumerable<FrameworkElement> GetNavigableElements(FrameworkElement root, string? group = null)
{
    var elements = new List<FrameworkElement>();
    
    // CRITICAL FIX: First check if the root element itself is navigable
    if (GetIsEnabled(root) && GetCanNavigate(root))
    {
        var rootGroup = GetNavigationGroup(root);
        if (group == null || rootGroup == group)
        {
            elements.Add(root);
        }
    }
    
    // Then collect child elements
    CollectNavigableElements(root, elements, group);
    
    return elements.OrderBy(e => GetNavigationOrder(e));
}
```

**Step 3**: Use IsFocused property with change notifications:
```csharp
public bool IsFocused
{
    get => _isFocused;
    set
    {
        if (_isFocused != value)
        {
            _isFocused = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ToggleFocusBrush));
        }
    }
}
```

**Step 4**: Add timing delay for page navigation:
```csharp
private void InitializeFanCurvePage()
{
    _fanCurvePage.Initialize();
    
    // Add delay to ensure control is fully loaded
    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High, () =>
    {
        _gamepadNavigationService.InitializePageNavigation(_fanCurvePage.FanCurveControl, isFromPageNavigation: true);
    });
}
```

**Debug Symptoms**:
- `Found 0 navigable elements` in debug output
- `IsEnabled: False` for all elements
- Focus border only appears after D-pad input
- `GetNavigableElements` never finds the target control

### Issue 2: ComboBox Selection Commits Unexpectedly

**Problem**: ComboBox selections commit immediately instead of allowing preview.

**Solution**: Track navigation state and original index:

```csharp
// Save original index when opening
ComboBoxOriginalIndex = comboBox.SelectedIndex;
IsNavigatingComboBox = true;

// On cancel (B button)
if (IsNavigatingComboBox && ComboBoxOriginalIndex >= 0)
{
    comboBox.SelectedIndex = ComboBoxOriginalIndex;
}
```

### Issue 3: Slider Adjustment Too Sensitive

**Problem**: Slider values change too quickly during adjustment.

**Solution**: Implement appropriate increment values and repeat delays:

```csharp
// Control-specific increments
const double TDP_INCREMENT = 1.0;      // 1W steps
const double BRIGHTNESS_INCREMENT = 5.0; // 5% steps
const double AUDIO_INCREMENT = 2.0;     // 2% steps
```

## Step-by-Step Implementation Guide

### Adding Gamepad Navigation to a New Control

1. **Implement IGamepadNavigable Interface**
```csharp
public sealed partial class YourControl : UserControl, IGamepadNavigable
{
    private bool _isFocused = false;
    private GamepadNavigationService? _gamepadNavigationService;
    
    // Interface implementation...
}
```

2. **Add Navigation Properties in XAML**
```xml
<UserControl
    x:Class="HUDRA.Controls.YourControl"
    xmlns:ap="using:HUDRA.AttachedProperties"
    ap:GamepadNavigation.IsEnabled="True"
    ap:GamepadNavigation.NavigationGroup="MainControls"
    ap:GamepadNavigation.NavigationOrder="10">
```

3. **Implement Focus Visualization**
```csharp
public Brush FocusBorderBrush
{
    get
    {
        if (_gamepadNavigationService == null)
            InitializeGamepadNavigationService();
            
        if (_isFocused && _gamepadNavigationService?.IsGamepadActive == true)
            return new SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
            
        return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }
}
```

4. **Add Focus Border in XAML**
```xml
<Border
    BorderBrush="{x:Bind FocusBorderBrush, Mode=OneWay}"
    BorderThickness="2"
    CornerRadius="6">
    <!-- Your control content -->
</Border>
```

5. **Handle Navigation Events**
```csharp
public void OnGamepadNavigateUp() 
{
    // Handle up navigation or leave empty if not needed
}

public void OnGamepadActivate()
{
    // Handle A button press
    PerformPrimaryAction();
}

public void OnGamepadFocusReceived()
{
    if (_gamepadNavigationService == null)
        InitializeGamepadNavigationService();
        
    _isFocused = true;
    UpdateFocusVisuals();
}
```

6. **Initialize Service Reference**
```csharp
private void InitializeGamepadNavigationService()
{
    if (Application.Current is App app && app.MainWindow is MainWindow mainWindow)
    {
        _gamepadNavigationService = mainWindow.GamepadNavigationService;
    }
}
```

7. **Update Focus Visuals**
```csharp
private void UpdateFocusVisuals()
{
    OnPropertyChanged(nameof(FocusBorderBrush));
    OnPropertyChanged(nameof(FocusBorderThickness));
}
```

## Debug Output

### Expected Console Output

**Gamepad Activation:**
```
🎮 Gamepad 0 connected
🎮 Starting gamepad polling timer
🎮 Gamepad activated on first input
🎮 Initialized page navigation with focus on: TdpPickerControl
```

**Navigation Between Controls:**
```
🎮 Navigating Down from current focus
🎮 Removed focus from: TdpPickerControl
🎮 Set focus to: BrightnessControlControl
🎮 Brightness: Received gamepad focus
```

**Page Navigation:**
```
🎮 Page navigation requested: Next
=== InitializeFanCurvePage called ===
🎮 InitializePageNavigation called for FanCurveControl
🎮 Found 1 navigable elements
🎮 Set focus to: FanCurveControl
🎮 FanCurve: Received gamepad focus
🎮 FanCurve: Lazy initialized gamepad navigation service
```

**ComboBox Navigation:**
```
🎮 Resolution: Opening ComboBox, saving original index: 2
🎮 Resolution: Navigating ComboBox down
🎮 Resolution: Selected index changed to 3
🎮 Resolution: A button - Processing current selection
🎮 Resolution: Committing selection at index 3
```

**Slider Adjustment:**
```
🎮 TDP: Activating slider mode
🎮 Slider activated: TdpPickerControl
🎮 Processing D-pad for slider adjustment: Left
🎮 TDP: Adjusted value to 24W (direction: -1)
```

### Troubleshooting Missing Focus

If focus borders don't appear:

1. **Check Service Initialization**
```csharp
System.Diagnostics.Debug.WriteLine($"🎮 Service null check: {_gamepadNavigationService == null}");
System.Diagnostics.Debug.WriteLine($"🎮 IsGamepadActive: {_gamepadNavigationService?.IsGamepadActive}");
System.Diagnostics.Debug.WriteLine($"🎮 IsFocused: {_isFocused}");
```

2. **Verify XAML Bindings**
```xml
<!-- Ensure Mode=OneWay for property updates -->
BorderBrush="{x:Bind FocusBorderBrush, Mode=OneWay}"
```

3. **Check Property Change Notifications**
```csharp
OnPropertyChanged(nameof(FocusBorderBrush)); // Must be called after state changes
```

## Performance Considerations

### Polling Rate
- **60Hz polling**: Balanced between responsiveness and CPU usage
- **DispatcherTimer**: Ensures UI thread execution
- **Early exit**: Skip processing if no gamepads connected

### Memory Management
- **Static service**: Single instance per application
- **Weak references**: Avoid memory leaks with event handlers
- **Lazy initialization**: Defer resource allocation until needed

### Input Processing
- **Button state tracking**: Prevent duplicate inputs
- **Repeat delays**: Reduce unnecessary processing
- **Batch updates**: Group UI updates when possible

## Future Enhancements

### Planned Improvements

1. **Haptic Feedback**
   - Vibration on focus change
   - Different patterns for different actions
   - User-configurable intensity

2. **Custom Navigation Paths**
   - Define explicit navigation routes
   - Skip disabled controls
   - Wrap-around navigation options

3. **Multi-Gamepad Support**
   - Handle multiple controllers
   - Player-specific focus
   - Split-screen scenarios

4. **Gesture Support**
   - Right stick for quick navigation
   - Trigger combinations for shortcuts
   - Long-press actions

5. **Accessibility Features**
   - Audio cues for navigation
   - High contrast focus modes
   - Configurable focus indicators

### Configuration System

Potential settings structure:
```json
{
  "gamepadNavigation": {
    "enableHaptics": true,
    "focusColor": "#8B00FF",
    "repeatDelayMs": 200,
    "repeatRateMs": 50,
    "wrapNavigation": true,
    "audioFeedback": false
  }
}
```

### Advanced Control Types

Support for complex controls:
- **Data grids**: Cell navigation
- **Tab controls**: Tab switching with bumpers
- **Tree views**: Expand/collapse with A button
- **Radial menus**: Analog stick navigation

## Critical Debugging Guide

When gamepad navigation fails, follow this systematic debugging approach:

### 1. Enable Debug Logging
Add comprehensive debug logging to track the navigation flow:

```csharp
// In GetNavigableElements
System.Diagnostics.Debug.WriteLine($"🎮 GetNavigableElements called for {root.GetType().Name}");
System.Diagnostics.Debug.WriteLine($"🎮 Found {elements.Count} navigable elements");

// In control focus handlers  
System.Diagnostics.Debug.WriteLine($"🔍 {ControlName} IsFocused changing from {_isFocused} to {value}");

// In property getters
System.Diagnostics.Debug.WriteLine($"🔍 ToggleFocusBrush getter - IsFocused: {IsFocused}, IsActive: {_gamepadNavigationService?.IsGamepadActive}");
```

### 2. Check Debug Output Patterns

**Healthy Navigation (Expected Output):**
```
🎮 InitializePageNavigation called for [ControlName], fromPageNav: True
🎮 Added root element: [ControlName] (Order: X, Group: MainControls)
🎮 Found 1 navigable elements
🎮 Set focus to: [ControlName]
🔍 [ControlName] IsFocused changing from False to True
```

**Failed Navigation (Problem Indicators):**
```
🎮 Found 0 navigable elements          // ❌ Root element not detected
🎮 No navigable elements found on page // ❌ No focus will be set
IsEnabled: False                       // ❌ Attached properties not set
```

### 3. Verification Checklist

For any new control implementing gamepad navigation:

**✅ Constructor Setup:**
```csharp
public MyControl()
{
    InitializeComponent();
    InitializeGamepadNavigation(); // MUST call this
}
```

**✅ Interface Implementation:**
```csharp
public sealed partial class MyControl : UserControl, IGamepadNavigable, INotifyPropertyChanged
```

**✅ Attached Properties Setup:**
```csharp
private void InitializeGamepadNavigation()
{
    GamepadNavigation.SetIsEnabled(this, true);        // CRITICAL
    GamepadNavigation.SetNavigationGroup(this, "MainControls");
    GamepadNavigation.SetNavigationOrder(this, [unique_number]);
}
```

**✅ XAML Properties (Alternative to code setup):**
```xml
<UserControl ap:GamepadNavigation.IsEnabled="True"
             ap:GamepadNavigation.NavigationGroup="MainControls" 
             ap:GamepadNavigation.NavigationOrder="1">
```

**✅ Focus Property with Change Notification:**
```csharp
public bool IsFocused { get; set; } // Use property, not field
```

**✅ Page Navigation Call:**
```csharp
_gamepadNavigationService.InitializePageNavigation(myControl, isFromPageNavigation: true);
```

### 4. Common Fix Patterns

**No Focus Border:** Check if `IsFocused` is a property with `OnPropertyChanged()` notifications.

**"Found 0 elements":** Ensure `SetIsEnabled(this, true)` is called in constructor.

**Settings Power Profile ComboBoxes (2025-05 fix):**
- ComboBox item templates now inherit the container foreground so gamepad navigation highlights selected entries in DarkViolet, matching the Main Page behavior.
- Removed glyph icons from power profile rows to keep text emphasis on selection state.
- Applied a dedicated `PowerProfileComboBoxItemStyle` that forces left-aligned item content, ensuring clarity when navigating via D-pad.

**Focus only after D-pad input:** Add dispatcher delay in page initialization.

**Elements found but no focus:** Check `isFromPageNavigation: true` parameter.

## Gamepad vs Mouse Navigation Handling (2025 Update)

Problem: After visiting a page via gamepad, then switching pages with gamepad, returning via mouse click on the navbar could still show the DarkViolet focus border, even though navigation was not from the gamepad.

Approach:
- Add a one‑shot suppression in `GamepadNavigationService` to prevent auto‑focus on the next gamepad activation after mouse/touch navigation. API: `SuppressAutoFocusOnNextActivation()`.
- Mark navbar click handlers (mouse/touch) to call the suppression API before navigating.
- Track if page navigation was initiated via gamepad: `_isGamepadPageNavPending` is set in `OnGamepadPageNavigationRequested`, then latched per page in `OnPageChanged` as `_isGamepadNavForCurrentPage`.
- On page init (e.g., Fan Curve), if navigation was not from gamepad and `IsGamepadActive` is true, call `DeactivateGamepadMode()` before `InitializePageNavigation`. Only pass `isFromPageNavigation: true` when `_isGamepadNavForCurrentPage` is true.

Result: Focus borders appear only for gamepad‑initiated navigation. Mouse/touch navigation shows no gamepad focus until the user provides gamepad input again.

## Conclusion

The HUDRA gamepad navigation system provides a comprehensive solution for controlling the application UI using gamepad input. Key success factors include:

1. **Consistent patterns**: All controls follow similar implementation patterns
2. **Visual feedback**: Clear focus indicators guide users
3. **Responsive feel**: Appropriate delays and repeat rates
4. **Flexibility**: Controls can customize navigation behavior
5. **Reliability**: Lazy initialization handles timing issues
6. **Root element detection**: Fixed critical bug in `GetNavigableElements()`

The system successfully enables handheld gaming device users to adjust performance settings without leaving their gamepad comfort zone, essential for the optimal gaming experience on devices like the ROG Ally and Legion Go.

**Implementation Checklist:**
- ✅ Implement IGamepadNavigable interface
- ✅ Add constructor gamepad navigation setup
- ✅ Create IsFocused property (not field) with change notifications
- ✅ Create focus border visualization with OneWay binding
- ✅ Handle navigation events appropriately
- ✅ Implement lazy service initialization
- ✅ Use dispatcher delays for page navigation timing
- ✅ Test with gamepad input and verify debug output
- ✅ Ensure GetNavigableElements finds the control
- ✅ Add comprehensive debug logging for troubleshooting

**Critical Fix Applied (2025):** Modified `GetNavigableElements()` to check the root element itself as navigable, not just its children. This was the primary cause of focus borders not appearing on page navigation.

This documentation will be updated as the gamepad navigation system evolves with new features and improvements.
