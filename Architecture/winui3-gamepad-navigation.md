# WinUI 3 Gamepad Navigation System

## Overview

This document provides a comprehensive guide to the gamepad navigation system implemented in HUDRA for AMD Ryzen handheld gaming devices. The system enables full UI control using gamepad input, essential for handheld gaming devices where touch/mouse input may be unavailable or inconvenient during gaming sessions.

**Key Features:**
- Full D-pad navigation between controls
- L1/R1 shoulder button page navigation
- L2/R2 trigger shortcuts for navbar buttons (Back to Game, Lossless Scaling)
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

### Focus Visualization System

HUDRA uses a unified focus visual system that combines WinUI's built-in focus visuals with custom border overlays:

#### Global System Focus Visual Styling (App.xaml)

All WinUI system focus visuals are styled purple to match the app theme:

```xml
<!-- In App.xaml Resources -->
<SolidColorBrush x:Key="SystemControlFocusVisualPrimaryBrush" Color="DarkViolet" />
<SolidColorBrush x:Key="SystemControlFocusVisualSecondaryBrush" Color="DarkViolet" />
<Thickness x:Key="FocusVisualThickness">2</Thickness>
<x:Double x:Key="FocusVisualPrimaryThickness">2</x:Double>
<x:Double x:Key="FocusVisualSecondaryThickness">1</x:Double>
```

**Benefits:**
- Consistent purple theme across all controls
- Automatic focus visuals for controls without custom implementations
- No white/default focus rectangles competing with custom borders

#### Custom Focus Borders with Gamepad Detection

Controls with gamepad-aware custom borders implement conditional visualization:

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

**CRITICAL**: Controls with custom borders must disable system focus visuals to prevent conflicts:

```xml
<ComboBox x:Name="MyComboBox"
          UseSystemFocusVisuals="False"
          IsTabStop="False"
          ... />
```

**Why This Matters:**
- `UseSystemFocusVisuals="False"` - Disables WinUI's default white focus rectangle
- `IsTabStop="False"` - Prevents keyboard tab navigation interference
- Custom border only shows when gamepad is active
- System focus visual (now purple) serves as fallback for other input methods

#### Focus Visual Colors by Control State

Controls with multiple states use different colors:
- **DarkViolet**: Standard focused control
- **DodgerBlue**: Activated slider (adjusting value)
- **MediumOrchid**: Selected preset button

#### Controls with Custom Focus Borders

The following controls have `UseSystemFocusVisuals="False"` and custom gamepad-aware borders:
- `FpsLimiterControl` - ComboBox
- `ResolutionPickerControl` - 2 ComboBoxes (Resolution & RefreshRate)
- `BrightnessControlControl` - Slider
- `AudioControlsControl` - Button, Slider
- `PowerProfileControl` - 2 ComboBoxes (Default & Gaming profiles)
- `GameDetectionControl` - ComboBox, Button

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

### Navbar Button Shortcuts (L2/R2)

**Purpose**: Provide instant access to context-sensitive navbar buttons without navigating away from focused controls.

**Implementation**: L2/R2 triggers invoke navbar button actions directly, bypassing normal navigation flow. Buttons are only invoked when visible (game detection triggers visibility).

```csharp
// In GamepadNavigationService.cs - Analog trigger state tracking
private bool _leftTriggerPressed = false;
private bool _rightTriggerPressed = false;
private const double TRIGGER_THRESHOLD = 0.8;

// In ProcessNavigationInput()
bool leftTriggerActive = reading.LeftTrigger > TRIGGER_THRESHOLD;
bool rightTriggerActive = reading.RightTrigger > TRIGGER_THRESHOLD;

if (leftTriggerActive && !_leftTriggerPressed)
{
    _leftTriggerPressed = true;
    NavbarButtonRequested?.Invoke(this,
        new GamepadNavbarButtonEventArgs(GamepadNavbarButton.BackToGame));
    return;
}
else if (!leftTriggerActive && _leftTriggerPressed)
{
    _leftTriggerPressed = false;
}

if (rightTriggerActive && !_rightTriggerPressed)
{
    _rightTriggerPressed = true;
    NavbarButtonRequested?.Invoke(this,
        new GamepadNavbarButtonEventArgs(GamepadNavbarButton.LosslessScaling));
    return;
}
else if (!rightTriggerActive && _rightTriggerPressed)
{
    _rightTriggerPressed = false;
}
```

```csharp
// In MainWindow.xaml.cs - Event handler
private void OnGamepadNavbarButtonRequested(object sender, GamepadNavbarButtonEventArgs e)
{
    switch (e.Button)
    {
        case GamepadNavbarButton.BackToGame:
            // Only invoke if button is visible (game detected)
            if (AltTabButton.Visibility == Visibility.Visible)
            {
                AltTabButton_Click(AltTabButton, new RoutedEventArgs());
            }
            break;

        case GamepadNavbarButton.LosslessScaling:
            // Only invoke if button is visible (LS detected)
            if (LosslessScalingButton.Visibility == Visibility.Visible)
            {
                LosslessScalingButton_Click(LosslessScalingButton, new RoutedEventArgs());
            }
            break;
    }
}
```

**Button Mapping**:
| Trigger | Navbar Button | Visibility Condition |
|---------|---------------|---------------------|
| **L2 (Left Trigger)** | Back to Game | Game process detected |
| **R2 (Right Trigger)** | Lossless Scaling | Lossless Scaling + game detected |

**Design Rationale**:
- **L2/R2 chosen over L1/R1**: Shoulder buttons (L1/R1) are reserved for page navigation. Triggers provide conflict-free quick access.
- **No navigation required**: Users can access critical actions (switching to game, enabling LS) without leaving current control focus.
- **Context-sensitive**: Buttons only work when visible, preventing accidental presses when features unavailable.
- **Analog tracking**: Triggers are analog (0.0-1.0), not digital buttons. Separate state tracking prevents input repeats.
- **Haptic feedback**: Trigger presses include controller vibration for tactile confirmation.

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

### Gamepad Mode Activation

When gamepad input is first detected (or re-detected after mouse/keyboard input), the system activates gamepad mode. **The activating input is consumed and NOT processed as navigation.**

#### Activation Flow (GamepadNavigationService.cs, lines 171-200)

```csharp
private void ProcessGamepadInput(GamepadReading reading)
{
    // Check for any input
    bool hasInput = reading.Buttons != GamepadButtons.None || /* thumbstick checks */;

    if (!hasInput)
    {
        UpdatePressedButtonsState(reading.Buttons);
        return;
    }

    // ACTIVATION CHECK
    if (!_isGamepadActive)
    {
        SetGamepadActive(true);  // Set IsGamepadActive = true
        System.Diagnostics.Debug.WriteLine("🎮 Gamepad activated on first input");

        // Initialize focus if not suppressed
        if (_currentFrame?.Content is FrameworkElement rootElement)
        {
            if (!_suppressAutoFocusOnActivation)
            {
                InitializePageNavigation(rootElement);
            }
        }

        _suppressAutoFocusOnActivation = false;
        _lastInputTime = DateTime.Now;

        // CRITICAL: Update button state to prevent held buttons from being
        // processed as "new" in the next frame
        UpdatePressedButtonsState(reading.Buttons);

        return;  // Consume the activation input - don't process as navigation
    }

    // Process navigation only after activation
    var newButtons = GetNewlyPressedButtons(reading.Buttons);
    // ... navigation processing
}
```

#### Why Button State Update is Critical

**Problem Without State Update:**
1. User presses and holds D-pad Down
2. Frame 1: Gamepad activates, focus border appears, returns without updating `_pressedButtons`
3. Frame 2 (~16ms later): User still holding D-pad Down
4. `GetNewlyPressedButtons()` sees D-pad Down is NOT in `_pressedButtons`
5. **Incorrectly treats held button as "newly pressed"**
6. Processes unwanted navigation action

**Solution:**
By calling `UpdatePressedButtonsState(reading.Buttons)` during activation, held buttons are tracked immediately. The next frame correctly identifies them as "held" rather than "new", preventing unintended navigation.

#### Activation Behavior

**Initial Activation:**
- App starts → user presses D-pad Down → gamepad activates → focus appears → D-pad Down is consumed
- Next input (release and press again) triggers navigation

**Re-activation After Mouse/Keyboard:**
- User clicks with mouse → gamepad deactivates → user presses D-pad Down → gamepad activates → focus appears → D-pad Down is consumed
- Next input triggers navigation

This ensures predictable, intentional navigation that only occurs on deliberate button presses after activation.

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

### Issue 1: Focus Border Not Showing Reliably (Intermittent White Box Issue)

**Problem**: Purple focus border doesn't appear consistently when navigating with gamepad. Sometimes a white default focus rectangle appears instead, or focus borders don't show at all.

**Root Cause**: Multiple timing and synchronization issues:
1. Property change notifications not processed on UI thread
2. System focus visuals competing with custom borders
3. Binding updates not synchronized with rendering pipeline

**Complete Solution**:

**Step 1**: Wrap property change notifications in `DispatcherQueue.TryEnqueue()`:

```csharp
private void UpdateFocusVisuals()
{
    // Dispatch on UI thread to ensure bindings update reliably with gamepad navigation
    DispatcherQueue.TryEnqueue(() =>
    {
        OnPropertyChanged(nameof(FocusBorderBrush));
        OnPropertyChanged(nameof(FocusBorderThickness));
    });
}
```

**Why This Works:**
- `x:Bind Mode=OneWay` bindings need property changes processed at the correct time in the rendering cycle
- Rapid gamepad navigation events can cause timing mismatches
- `DispatcherQueue.TryEnqueue()` ensures notifications are processed synchronously with UI rendering

**Step 2**: Disable system focus visuals on interactive controls:

```xml
<ComboBox x:Name="FpsLimitComboBox"
          UseSystemFocusVisuals="False"
          IsTabStop="False"
          helpers:GamepadComboBoxHelper.IsGamepadEnabled="True" />

<Slider x:Name="BrightnessSlider"
        UseSystemFocusVisuals="False"
        IsTabStop="False" />

<Button x:Name="MuteButton"
        UseSystemFocusVisuals="False"
        IsTabStop="False" />
```

**Step 3**: Style system focus visuals globally (App.xaml):

```xml
<SolidColorBrush x:Key="SystemControlFocusVisualPrimaryBrush" Color="DarkViolet" />
<SolidColorBrush x:Key="SystemControlFocusVisualSecondaryBrush" Color="DarkViolet" />
```

**Result**: Consistent purple focus indicators with no white boxes. Controls with custom borders show only custom borders. Controls without custom implementations use purple system focus visuals.

### Issue 2: Focus Border Not Appearing on Page Navigation (CRITICAL)

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

### Issue 3: Activation Input Processed as Navigation (FIXED 2025)

**Problem**: When gamepad mode is activated (or re-activated after mouse/keyboard input), if the user holds the activation button, the held button is incorrectly processed as navigation input in the next polling frame (~16ms later), causing unintended navigation immediately after activation.

**Example:**
1. App starts with gamepad inactive
2. User presses and holds D-pad Down
3. Gamepad activates, focus border appears correctly
4. **BUG**: 16ms later, focus moves down unexpectedly (user still holding button)

**Root Cause**:
The activation block in `ProcessGamepadInput()` was correctly consuming the activation input by returning early, but it wasn't updating the `_pressedButtons` HashSet before returning. This caused the next frame's `GetNewlyPressedButtons()` to see the held button as "newly pressed" since it wasn't tracked.

**Solution (GamepadNavigationService.cs, line 198)**:

```csharp
if (!_isGamepadActive)
{
    SetGamepadActive(true);
    // ... initialization code ...

    _lastInputTime = DateTime.Now;

    // CRITICAL FIX: Update pressed buttons state to prevent held buttons
    // from being processed as "new" in the next frame
    UpdatePressedButtonsState(reading.Buttons);

    return;  // Consume the activation input - don't process as navigation
}
```

**Why This Works:**
- `UpdatePressedButtonsState()` adds the activating button to the `_pressedButtons` HashSet
- Next frame's `GetNewlyPressedButtons()` sees the button is already tracked
- Correctly identifies it as "held" rather than "newly pressed"
- Button is not processed as navigation until released and pressed again

**Result**: The activation input is truly consumed. Navigation only occurs on deliberate button presses after gamepad mode is active.

### Issue 4: ComboBox Selection Commits Unexpectedly

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

### Issue 5: Slider Adjustment Too Sensitive

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
    <ComboBox x:Name="MyComboBox"
              UseSystemFocusVisuals="False"
              IsTabStop="False"
              ... />
    <!-- Your control content -->
</Border>
```

**Important**: Always set `UseSystemFocusVisuals="False"` and `IsTabStop="False"` on interactive controls to prevent system focus visual conflicts.

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
    // CRITICAL: Dispatch on UI thread to ensure bindings update reliably
    DispatcherQueue.TryEnqueue(() =>
    {
        OnPropertyChanged(nameof(FocusBorderBrush));
        OnPropertyChanged(nameof(FocusBorderThickness));
    });
}
```

**Critical**: Always wrap property change notifications in `DispatcherQueue.TryEnqueue()` to ensure reliable focus border updates during rapid gamepad navigation.

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
7. **Intentional activation**: Fixed activation input consumption to prevent unintended navigation

The system successfully enables handheld gaming device users to adjust performance settings without leaving their gamepad comfort zone, essential for the optimal gaming experience on devices like the ROG Ally and Legion Go.

**Implementation Checklist:**
- ✅ Implement IGamepadNavigable interface
- ✅ Add constructor gamepad navigation setup
- ✅ Create IsFocused property (not field) with change notifications
- ✅ Create focus border visualization with OneWay binding
- ✅ **Set `UseSystemFocusVisuals="False"` on interactive controls**
- ✅ **Set `IsTabStop="False"` on interactive controls**
- ✅ **Wrap UpdateFocusVisuals property notifications in `DispatcherQueue.TryEnqueue()`**
- ✅ Handle navigation events appropriately
- ✅ Implement lazy service initialization
- ✅ Use dispatcher delays for page navigation timing
- ✅ Test with gamepad input and verify debug output
- ✅ Ensure GetNavigableElements finds the control
- ✅ Add comprehensive debug logging for troubleshooting

**Critical Fixes Applied (2025):**
1. Modified `GetNavigableElements()` to check the root element itself as navigable, not just its children. This was the primary cause of focus borders not appearing on page navigation.
2. Added `UpdatePressedButtonsState()` call during gamepad activation to prevent held buttons from being processed as "newly pressed" in subsequent frames, ensuring the activating input is truly consumed.

This documentation will be updated as the gamepad navigation system evolves with new features and improvements.
