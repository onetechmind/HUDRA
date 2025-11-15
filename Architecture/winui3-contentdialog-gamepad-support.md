# WinUI 3 ContentDialog Gamepad Support System

## Overview

This document describes the gamepad input handling system for WinUI 3 `ContentDialog` components in HUDRA. The system enables users to interact with modal dialogs using gamepad A/B buttons, essential for handheld gaming devices where dialogs would otherwise require touch/mouse input.

**Key Features:**
- Automatic gamepad A/B button support for all ContentDialogs
- Single extension method for universal dialog support
- Primary button invocation via A button (confirmation)
- Cancel/Close via B button
- Background UI input blocking during dialog display
- Visual button glyphs (Ⓐ/Ⓑ) in dialog button text
- Automatic state cleanup on dialog close

## Problem Context

### The Challenge

WinUI 3's `ContentDialog` does not natively handle gamepad A/B button input reliably. When dialogs are shown:

1. **WinUI's native gamepad support is incomplete**: ContentDialog does not respond to A/B buttons in all scenarios
2. **Background UI remains active**: Without intervention, gamepad input continues to navigate background controls
3. **First input consumption**: The gamepad activation logic consumes the first button press, preventing immediate dialog interaction
4. **Manual state management**: Each dialog requires boilerplate setup/teardown code

### User Impact

On handheld gaming devices, this creates a frustrating experience:
- Users press A/B buttons → Nothing happens
- Background controls highlight/activate instead of dialog buttons
- Users must reach for touch screen or mouse to interact with simple yes/no dialogs
- Inconsistent behavior across different dialogs in the app

## Architecture

### System Components

```
┌──────────────────────────────────────────────────────────┐
│           ContentDialogExtensions.cs                     │
│  ShowWithGamepadSupportAsync() Extension Method          │
│  - Automatic SetDialogOpen/SetDialogClosed calls         │
│  - Exception-safe state cleanup via finally block        │
└────────────┬─────────────────────────────────────────────┘
             │
             │ Calls
             ▼
┌──────────────────────────────────────────────────────────┐
│        GamepadNavigationService.cs                       │
│                                                           │
│  SetDialogOpen(ContentDialog):                           │
│  - Sets _isDialogOpen flag                               │
│  - Stores dialog reference in _currentDialog             │
│  - Clears UI focus to block background navigation        │
│                                                           │
│  ProcessNavigationInput():                               │
│  - Detects A/B button presses when _isDialogOpen = true  │
│  - A button → TriggerDialogPrimaryButton()               │
│  - B button → _currentDialog.Hide()                      │
│  - Blocks all other gamepad input                        │
│                                                           │
│  TriggerDialogPrimaryButton():                           │
│  - Searches dialog visual tree for "PrimaryButton"       │
│  - Uses ButtonAutomationPeer + IInvokeProvider           │
│  - Programmatically invokes button click                 │
│                                                           │
│  SetDialogClosed():                                      │
│  - Clears _isDialogOpen flag                             │
│  - Clears _currentDialog reference                       │
│  - Resumes normal UI navigation                          │
└──────────────────────────────────────────────────────────┘
```

### State Flow

```
┌─────────────────┐
│  User triggers  │
│  dialog action  │
└────────┬────────┘
         │
         ▼
┌─────────────────────────────────────────────────┐
│  Create ContentDialog instance                  │
│  (set Title, Content, PrimaryButtonText, etc.)  │
└────────┬────────────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────────────────┐
│  Call ShowWithGamepadSupportAsync()             │
│  Extension method invoked                        │
└────────┬────────────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────────────────┐
│  SetDialogOpen(dialog)                          │
│  - _isDialogOpen = true                         │
│  - _currentDialog = dialog                      │
│  - ClearFocus() (block UI input)                │
└────────┬────────────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────────────────┐
│  await dialog.ShowAsync()                       │
│  Dialog visible, waiting for user input         │
└────────┬────────────────────────────────────────┘
         │
         ├──────────────┬──────────────┐
         │              │              │
         ▼              ▼              ▼
┌────────────┐  ┌──────────────┐  ┌───────────┐
│ A pressed  │  │  B pressed   │  │   User    │
│ Primary    │  │  Cancel      │  │  touches  │
│ invoked    │  │  Hide()      │  │  screen   │
└────┬───────┘  └──────┬───────┘  └─────┬─────┘
     │                 │                 │
     └─────────────────┴─────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────────┐
│  Dialog closes, ShowAsync() returns result      │
└────────┬────────────────────────────────────────┘
         │
         ▼
┌─────────────────────────────────────────────────┐
│  finally block: SetDialogClosed()               │
│  - _isDialogOpen = false                        │
│  - _currentDialog = null                        │
│  - Resume normal UI navigation                  │
└─────────────────────────────────────────────────┘
```

## Implementation Details

### 1. ContentDialog Extension Method

**File:** `HUDRA/Extensions/ContentDialogExtensions.cs`

```csharp
public static async Task<ContentDialogResult> ShowWithGamepadSupportAsync(
    this ContentDialog dialog,
    GamepadNavigationService gamepadService)
{
    // Set up gamepad handling for this dialog
    gamepadService.SetDialogOpen(dialog);

    try
    {
        // Show the dialog
        return await dialog.ShowAsync();
    }
    finally
    {
        // Clean up gamepad state when dialog closes
        gamepadService.SetDialogClosed();
    }
}
```

**Design Decisions:**
- Extension method pattern for clean, reusable API
- `finally` block ensures cleanup even on exceptions
- Returns `ContentDialogResult` to maintain compatibility with normal ShowAsync()

### 2. Dialog State Management

**File:** `HUDRA/Services/GamepadNavigationService.cs`

**State Variables:**
```csharp
private bool _isDialogOpen = false;
private ContentDialog? _currentDialog = null;
```

**SetDialogOpen Method:**
```csharp
public void SetDialogOpen(ContentDialog dialog)
{
    _isDialogOpen = true;
    _currentDialog = dialog;
    // Clear focus from UI to prevent background controls from receiving input
    ClearFocus();
    System.Diagnostics.Debug.WriteLine("🎮 Dialog opened - UI navigation blocked, dialog has exclusive input");
}
```

**Why ClearFocus()?**
When a dialog is shown, background UI controls can still receive focus and process gamepad input. Clearing focus ensures all gamepad input is directed exclusively to the dialog.

### 3. Gamepad Input Interception

**File:** `HUDRA/Services/GamepadNavigationService.cs`

**ProcessNavigationInput Method:**
```csharp
private void ProcessNavigationInput(GamepadReading reading, List<GamepadButtons> newButtons, bool shouldProcessRepeats)
{
    // When dialog is open, only process A/B buttons to control the dialog
    if (_isDialogOpen && _currentDialog != null)
    {
        if (newButtons.Contains(GamepadButtons.A))
        {
            // A button = Primary button (Force Quit, Yes, OK, etc.)
            System.Diagnostics.Debug.WriteLine("🎮 A button pressed - triggering dialog primary action");
            _dispatcherQueue?.TryEnqueue(() =>
            {
                if (_currentDialog != null)
                {
                    // Programmatically click the primary button
                    TriggerDialogPrimaryButton(_currentDialog);
                }
            });
            return;
        }
        else if (newButtons.Contains(GamepadButtons.B))
        {
            // B button = Close/Cancel button
            System.Diagnostics.Debug.WriteLine("🎮 B button pressed - triggering dialog cancel");
            _dispatcherQueue?.TryEnqueue(() =>
            {
                _currentDialog?.Hide();
            });
            return;
        }
        // Ignore all other input when dialog is open
        return;
    }

    // ... rest of normal navigation logic
}
```

**Key Points:**
- Early return when `_isDialogOpen` is true blocks all normal navigation
- A button triggers primary button invocation
- B button calls `Hide()` which closes the dialog (returns `ContentDialogResult.None`)
- All work is queued on UI thread via `_dispatcherQueue`

### 4. Programmatic Button Invocation

**File:** `HUDRA/Services/GamepadNavigationService.cs`

**TriggerDialogPrimaryButton Method:**
```csharp
private void TriggerDialogPrimaryButton(ContentDialog dialog)
{
    try
    {
        // Find the primary button in the ContentDialog's visual tree and invoke it
        var primaryButton = FindPrimaryButtonInDialog(dialog);
        if (primaryButton != null)
        {
            // Use automation peer to invoke the button
            var peer = new ButtonAutomationPeer(primaryButton);
            var invokeProvider = peer.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
            invokeProvider?.Invoke();
            System.Diagnostics.Debug.WriteLine("🎮 Primary button invoked via automation");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("🎮 Warning: Could not find primary button in dialog");
        }
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"🎮 Error triggering dialog primary button: {ex.Message}");
    }
}
```

**FindPrimaryButtonInDialog Method:**
```csharp
private Button? FindPrimaryButtonInDialog(DependencyObject parent)
{
    // Search the visual tree for a button with specific names used by ContentDialog
    int childCount = VisualTreeHelper.GetChildrenCount(parent);
    for (int i = 0; i < childCount; i++)
    {
        var child = VisualTreeHelper.GetChild(parent, i);

        // ContentDialog typically names its buttons "PrimaryButton", "SecondaryButton", "CloseButton"
        if (child is Button button && child is FrameworkElement element)
        {
            if (element.Name == "PrimaryButton")
            {
                return button;
            }
        }

        // Recursively search children
        var result = FindPrimaryButtonInDialog(child);
        if (result != null)
        {
            return result;
        }
    }
    return null;
}
```

**Why UI Automation?**
- WinUI ContentDialog does not expose button instances publicly
- Visual tree search finds the actual button control
- `ButtonAutomationPeer` + `IInvokeProvider` allows programmatic click
- This properly triggers all button click events and state changes
- Returns correct `ContentDialogResult.Primary` when invoked

### 5. First Input Activation Logic

**File:** `HUDRA/Services/GamepadNavigationService.cs`

**ProcessGamepadInput Method:**
```csharp
// Activate gamepad navigation on first input (unless dialog is open)
if (!_isGamepadActive && !_isDialogOpen)
{
    SetGamepadActive(true);
    System.Diagnostics.Debug.WriteLine("🎮 Gamepad activated on first input");

    // Initialize focus on first input if we have a current frame
    if (_currentFrame?.Content is FrameworkElement rootElement)
    {
        if (!_suppressAutoFocusOnActivation)
        {
            InitializePageNavigation(rootElement);
        }
    }

    // Don't process the activation input as navigation - just consume it for activation
    _lastInputTime = DateTime.Now;
    UpdatePressedButtonsState(reading.Buttons);

    return; // CONSUME THE INPUT
}

// If dialog is open but gamepad not active, activate it without consuming input
if (!_isGamepadActive && _isDialogOpen)
{
    SetGamepadActive(true);
    System.Diagnostics.Debug.WriteLine("🎮 Gamepad activated for dialog - input will be processed");
    // Don't return - let the input be processed below
}
```

**Critical Difference:**
- **Normal activation:** First input is consumed (returned early) to prevent accidental navigation
- **Dialog activation:** First input is NOT consumed, allowing immediate A/B button interaction
- This is why `!_isDialogOpen` check is crucial in the activation logic

## Usage Guide

### Basic Usage

**Before (manual state management):**
```csharp
_gamepadNavigationService.SetDialogOpen(dialog);
try
{
    var result = await dialog.ShowAsync();

    if (result == ContentDialogResult.Primary)
    {
        // Handle confirmation
    }
}
finally
{
    _gamepadNavigationService.SetDialogClosed();
}
```

**After (extension method):**
```csharp
var result = await dialog.ShowWithGamepadSupportAsync(_gamepadNavigationService);

if (result == ContentDialogResult.Primary)
{
    // Handle confirmation
}
```

### Example: Force Quit Confirmation

**File:** `HUDRA/MainWindow.xaml.cs`

```csharp
private async void ForceQuitButton_Click(object sender, RoutedEventArgs e)
{
    // Create dialog
    var dialog = new ContentDialog()
    {
        Title = "Force Quit Game",
        Content = $"Are you sure you want to force quit {gameName}?\n\n⚠️ Please save your game before proceeding to avoid losing progress.",
        PrimaryButtonText = "Ⓐ Force Quit",
        CloseButtonText = "Ⓑ Cancel",
        DefaultButton = ContentDialogButton.Close,
        XamlRoot = this.Content.XamlRoot
    };

    // Show with automatic gamepad support
    var result = await dialog.ShowWithGamepadSupportAsync(_gamepadNavigationService);

    if (result != ContentDialogResult.Primary)
    {
        return; // User cancelled
    }

    // User confirmed - proceed with force quit
    await ForceQuitGame();
}
```

### Example: Settings Page Dialog

**File:** `HUDRA/Pages/SettingsPage.xaml.cs`

```csharp
private async void ResetDatabaseButton_Click(object sender, RoutedEventArgs e)
{
    // Get MainWindow for gamepad service access
    var mainWindow = (Application.Current as App)?.m_window as MainWindow;

    // Create dialog
    var dialog = new ContentDialog
    {
        Title = "Reset Game Database",
        Content = "This will clear all detected games and perform a fresh scan. Continue?",
        PrimaryButtonText = "Ⓐ Yes",
        CloseButtonText = "Ⓑ No",
        DefaultButton = ContentDialogButton.Close,
        XamlRoot = this.XamlRoot
    };

    // Show with gamepad support (fallback to normal ShowAsync if service unavailable)
    var result = mainWindow != null
        ? await dialog.ShowWithGamepadSupportAsync(mainWindow.GamepadNavigationService)
        : await dialog.ShowAsync();

    if (result != ContentDialogResult.Primary)
    {
        return; // User cancelled
    }

    // Proceed with database reset
    await ResetDatabase();
}
```

### Adding Button Glyphs

To provide visual indication of gamepad button mapping, add Ⓐ/Ⓑ Unicode glyphs to button text:

```csharp
var dialog = new ContentDialog
{
    Title = "Confirmation",
    Content = "Are you sure?",
    PrimaryButtonText = "Ⓐ Yes",      // Circle-A glyph
    CloseButtonText = "Ⓑ No",         // Circle-B glyph
    XamlRoot = this.XamlRoot
};
```

**Glyph Unicode:**
- `Ⓐ` = U+24B6 (CIRCLED LATIN CAPITAL LETTER A)
- `Ⓑ` = U+24B7 (CIRCLED LATIN CAPITAL LETTER B)

### Getting GamepadNavigationService Reference

**From MainWindow:**
```csharp
// Direct access
_gamepadNavigationService.Method();

// Public property
public GamepadNavigationService GamepadNavigationService => _gamepadNavigationService;
```

**From Pages:**
```csharp
// Access via App's MainWindow
var mainWindow = (Application.Current as App)?.m_window as MainWindow;
if (mainWindow != null)
{
    await dialog.ShowWithGamepadSupportAsync(mainWindow.GamepadNavigationService);
}
```

**From Controls:**
```csharp
// Walk visual tree to MainWindow
var mainWindow = Window.Current as MainWindow;
// or
var mainWindow = XamlRoot.Content as MainWindow;
```

## Testing Checklist

When implementing a new dialog with gamepad support:

- [ ] Dialog created with `PrimaryButtonText` and/or `CloseButtonText`
- [ ] `ShowWithGamepadSupportAsync()` used instead of `ShowAsync()`
- [ ] GamepadNavigationService reference obtained correctly
- [ ] Button glyphs (Ⓐ/Ⓑ) added to button text
- [ ] Tested A button triggers primary action
- [ ] Tested B button cancels/closes dialog
- [ ] Tested background UI does not receive input while dialog is open
- [ ] Tested dialog works correctly even if gamepad not connected (falls back to touch/mouse)
- [ ] Tested exception handling (dialog cleanup in error scenarios)

## Performance Considerations

### Minimal Overhead

The gamepad dialog system has negligible performance impact:

1. **State variables:** Two fields (`_isDialogOpen`, `_currentDialog`) - 16 bytes
2. **Early return check:** Single boolean check in `ProcessNavigationInput()` when dialog is open
3. **Visual tree search:** Only executed once when A button is pressed, not continuously
4. **UI thread dispatch:** Uses existing `_dispatcherQueue`, no additional thread creation

### Visual Tree Search Optimization

The `FindPrimaryButtonInDialog()` method:
- Searches depth-first, so typically finds button quickly
- Only runs when user presses A button (not on every frame)
- Caches nothing (dialog lifetime is short, search is fast enough)
- Could be optimized with breadth-first search if needed

## Known Limitations

### 1. Secondary Button Not Supported

Currently only Primary and Close buttons are mapped:
- **A button** → Primary button
- **B button** → Close button
- **Secondary button** → Not mapped (no X/Y button handling)

**Workaround:** If three-button dialogs are needed, map Secondary to X button in future enhancement.

### 2. Button Search Assumes Standard Template

The `FindPrimaryButtonInDialog()` method searches for buttons named "PrimaryButton" and "CloseButton". If Microsoft changes the ContentDialog template in future WinUI versions, this could break.

**Mitigation:** Comprehensive error handling and debug logging when button not found.

### 3. Requires GamepadNavigationService Reference

Pages and controls must have access to the `GamepadNavigationService` instance. This creates a slight coupling.

**Design Choice:** Acceptable coupling given the centralized nature of the service and rare use of dialogs.

## Future Enhancements

### Potential Improvements

1. **X/Y Button Support**
   - Map X button to Secondary button
   - Map Y button to custom actions

2. **Haptic Feedback on Dialog Actions**
   - Vibrate controller when A/B pressed
   - Different patterns for confirm vs cancel

3. **Dialog Animation Control**
   - Disable animations when using gamepad for faster response

4. **Automatic Button Glyph Injection**
   - Extension method automatically adds Ⓐ/Ⓑ to button text
   - Controlled via optional parameter

5. **Service Locator Pattern**
   - Remove need to pass `GamepadNavigationService` reference
   - Automatic service discovery via DI or singleton

## Related Documentation

- [WinUI 3 Gamepad Navigation System](winui3-gamepad-navigation.md) - Main navigation architecture
- [Expander State Management](winui3-expander-state-management.md) - NavigableExpander pattern

## Revision History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2025-01-15 | Claude | Initial documentation of ContentDialog gamepad support system |
