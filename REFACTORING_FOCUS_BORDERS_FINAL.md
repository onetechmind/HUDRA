# Focus Border Unification - Final Solution

## Problem Statement

HUDRA experienced double borders when switching from keyboard Tab navigation to gamepad input:
- **Outer border** - DarkViolet gamepad focus (custom system)
- **Inner border** - DarkViolet WinUI system focus visual (from Tab)
- **Result**: Both borders visible simultaneously

Additionally, lingering focus would sometimes stick on controls when switching between input methods.

## Root Cause Analysis

### The Core Issue

**File**: `GamepadNavigationService.cs`, `SetFocus()` method

```csharp
public void SetFocus(FrameworkElement? element)
{
    _currentFocusedElement = element;

    if (_currentFocusedElement != null)
    {
        GamepadNavigation.SetIsCurrentFocus(_currentFocusedElement, true);  // Custom gamepad focus
        _currentFocusedElement.Focus(FocusState.Programmatic);              // ← PROBLEM
        _currentFocusedElement.StartBringIntoView();
    }
}
```

**The Problem Flow:**

1. Gamepad navigation calls `SetFocus()` on a UserControl
2. `.Focus(FocusState.Programmatic)` is called on the UserControl
3. UserControl has `IsTabStop=False` (default), so WinUI **delegates** focus to first child with `IsTabStop=True`
4. Child control (ToggleSwitch, ComboBox, Button) receives WinUI focus
5. Child shows WinUI system focus visual (DarkViolet rectangle)
6. UserControl ALSO has gamepad focus → outer Border shows DarkViolet
7. **Result: DOUBLE BORDERS** (outer gamepad + inner WinUI)

### Why Clicking in Open Space Worked

Clicking in open space focused `LayoutRoot` using `FocusState.Pointer`, which:
- Clears focus from all descendant controls
- Doesn't show a focus visual (LayoutRoot is a non-interactive Grid)
- Uses pointer focus state (natural click behavior)

## The Solution

### Change 1: Don't Set WinUI Focus During Gamepad Navigation

**File**: `GamepadNavigationService.cs:816-838`

```csharp
if (_isGamepadActive)
{
    // When gamepad is active, clear any existing WinUI focus on inner controls
    // to prevent double borders (Tab focus lingering + gamepad focus)
    try
    {
        if (_layoutRoot != null && _currentFrame?.XamlRoot != null)
        {
            var winuiFocusedElement = FocusManager.GetFocusedElement(_currentFrame.XamlRoot) as UIElement;
            if (winuiFocusedElement != null)
            {
                // Focus LayoutRoot using Pointer state to mimic clicking in open space
                // LayoutRoot now has Background="Transparent" so it can accept focus
                _layoutRoot.Focus(FocusState.Pointer);
                System.Diagnostics.Debug.WriteLine($"🎮 Cleared WinUI focus from: {winuiFocusedElement.GetType().Name}");
            }
        }
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"🎮 Failed to clear WinUI focus: {ex.Message}");
    }
}
else
{
    // Only set WinUI focus when gamepad is NOT active
    _currentFocusedElement.Focus(FocusState.Programmatic);
}
```

**Key Points:**
- **When gamepad active**: Focus LayoutRoot to clear any Tab focus, DON'T set new WinUI focus
- **When gamepad inactive**: Set WinUI focus normally for keyboard navigation
- Gamepad navigation uses its own custom focus system (`GamepadNavigation.IsCurrentFocus`)

### Change 2: Make LayoutRoot Focusable

**File**: `MainWindow.xaml:15`

```xml
<!-- Before -->
<Grid x:Name="LayoutRoot">

<!-- After -->
<Grid x:Name="LayoutRoot" Background="Transparent">
```

**Why `Background="Transparent"`:**
- Grids with no background cannot receive pointer events or focus
- Setting `Background="Transparent"` makes LayoutRoot focusable
- Transparent background is invisible but interactive
- This allows `_layoutRoot.Focus(FocusState.Pointer)` to succeed

### Change 3: Pass LayoutRoot to GamepadNavigationService

**File**: `MainWindow.xaml.cs:185-187`

```csharp
_gamepadNavigationService = new GamepadNavigationService();
_gamepadNavigationService.SetCurrentFrame(ContentFrame);
_gamepadNavigationService.SetLayoutRoot(LayoutRoot);  // NEW
```

**File**: `GamepadNavigationService.cs:24` (field)

```csharp
private UIElement? _layoutRoot;
```

**File**: `GamepadNavigationService.cs:116-120` (method)

```csharp
public void SetLayoutRoot(UIElement layoutRoot)
{
    _layoutRoot = layoutRoot;
    System.Diagnostics.Debug.WriteLine($"🎮 Set layout root: {layoutRoot?.GetType().Name}");
}
```

### Change 4: Use Same Clearing Logic in ClearFocus()

**File**: `GamepadNavigationService.cs:867-884`

```csharp
// Also clear any WinUI system focus (keyboard Tab focus) to prevent double borders
try
{
    if (_layoutRoot != null && _currentFrame?.XamlRoot != null)
    {
        var focusedElement = FocusManager.GetFocusedElement(_currentFrame.XamlRoot) as UIElement;
        if (focusedElement != null)
        {
            // Focus LayoutRoot using Pointer state to mimic clicking in open space
            _layoutRoot.Focus(FocusState.Pointer);
            System.Diagnostics.Debug.WriteLine($"🎮 Cleared WinUI system focus from: {focusedElement.GetType().Name}");
        }
    }
}
catch (Exception ex)
{
    System.Diagnostics.Debug.WriteLine($"🎮 Failed to clear WinUI focus: {ex.Message}");
}
```

## What Didn't Work

### ❌ Attempt 1: Focus the Frame

```csharp
_currentFrame.Focus(FocusState.Programmatic);
```

**Problem**: Frame is a child of LayoutRoot, not high enough in visual tree to clear descendant focus.

### ❌ Attempt 2: Set IsTabStop="False" on LayoutRoot

```csharp
<Grid x:Name="LayoutRoot" IsTabStop="False">
_layoutRoot.Focus(FocusState.Programmatic);
```

**Problem**: LayoutRoot with `IsTabStop="False"` still delegated focus to first TabIndex element (Home button).

### ❌ Attempt 3: TryMoveFocus(None)

```csharp
FocusManager.TryMoveFocus(FocusNavigationDirection.None);
```

**Problem**: WinUI Desktop requires `FindNextElementOptions` parameter:
```
Catastrophic failure

In a WinUI Desktop app, the app must call TryMoveFocus/TryMoveFocusAsync overload
with the FindNextElementOptions parameter, and the FindNextElementOptions.SearchRoot
must be set to a loaded DependencyObject.
```

### ✅ What Finally Worked: FocusState.Pointer

```csharp
<Grid x:Name="LayoutRoot" Background="Transparent">
_layoutRoot.Focus(FocusState.Pointer);
```

**Why this works:**
- `Background="Transparent"` makes Grid focusable
- `FocusState.Pointer` mimics clicking behavior (not Tab)
- LayoutRoot is highest in visual tree, clears all descendant focus
- Pointer focus on a Grid doesn't show visual focus rectangle
- Exactly replicates what happens when clicking in open space

## Architecture Overview

### Dual Focus Systems

HUDRA uses two independent focus systems:

**1. Gamepad Navigation Focus (Custom)**
- Tracked via `GamepadNavigation.IsCurrentFocus` attached property
- Computed brush properties in controls (e.g., `ToggleFocusBrush`)
- Returns `DarkViolet` when gamepad active AND control focused
- Bound to outer Border elements
- Completely separate from WinUI focus

**2. WinUI System Focus (Built-in)**
- Managed by WinUI's `FocusManager`
- Shows `SystemControlFocusVisualPrimaryBrush` (DarkViolet) on focused controls
- Applies to any control with `IsTabStop=True` (default for interactive controls)
- Used for keyboard Tab navigation
- Automatically shown by WinUI framework

### Control Structure

```xml
<UserControl> <!-- Gamepad navigable, IsTabStop=False -->
    <!-- OUTER BORDER: Gamepad focus -->
    <Border BorderBrush="{x:Bind ToggleFocusBrush, Mode=OneWay}"
            BorderThickness="2">

        <!-- MIDDLE BORDER: Visual styling -->
        <Border Background="#22FFFFFF">

            <!-- INNER CONTROL: Can receive WinUI Tab focus -->
            <ToggleSwitch x:Name="MyToggle" />
            <!-- Default: IsTabStop=True, UseSystemFocusVisuals=True -->

        </Border>
    </Border>
</UserControl>
```

**When gamepad active:**
- Outer border shows DarkViolet (gamepad focus)
- Inner control has NO WinUI focus (cleared by LayoutRoot.Focus)
- Result: Single outer border

**When keyboard Tab active:**
- Outer border is transparent (gamepad inactive)
- Inner control has WinUI focus (shows system focus visual)
- Result: Single inner focus rectangle

**When switching Tab → Gamepad:**
- `SetFocus()` focuses LayoutRoot with Pointer state
- Clears WinUI focus from inner control
- Sets gamepad focus on UserControl
- Result: Only outer border, no double borders

## Key Files Modified

1. **GamepadNavigationService.cs**
   - Added `_layoutRoot` field
   - Added `SetLayoutRoot()` method
   - Modified `SetFocus()` to clear WinUI focus when gamepad active
   - Modified `ClearFocus()` to use LayoutRoot

2. **MainWindow.xaml**
   - Added `Background="Transparent"` to LayoutRoot

3. **MainWindow.xaml.cs**
   - Added `SetLayoutRoot(LayoutRoot)` call during initialization

## Testing Checklist

- [x] **Gamepad D-pad navigation** - works correctly
- [x] **A button activation** - toggles, opens comboboxes, presses buttons
- [x] **B button cancellation** - works in comboboxes
- [x] **Scrolling into view** - controls scroll correctly
- [x] **No double borders** - during gamepad navigation
- [x] **Tab → Gamepad switch** - clears Tab focus, no double borders
- [x] **Gamepad → Tab switch** - clears gamepad focus properly
- [x] **No stuck focus** - Home button doesn't stay outlined
- [x] **Page navigation L1/R1** - works correctly
- [x] **Keyboard Tab** - still works for navigation buttons

## Summary

**Problem**: Double borders when switching from keyboard Tab to gamepad input.

**Root Cause**: `SetFocus()` was calling `.Focus()` on UserControls, which delegated WinUI focus to inner controls, creating double borders alongside gamepad focus.

**Solution**:
1. Don't set WinUI focus during gamepad navigation (gamepad uses custom focus system)
2. Clear existing Tab focus by focusing LayoutRoot with `FocusState.Pointer`
3. Make LayoutRoot focusable with `Background="Transparent"`

**Result**: Unified border system - only one DarkViolet border shows at a time, regardless of input method.

---

**Date**: 2025-11-22
**Status**: ✅ **WORKING - Tested and Verified**
**Commits**:
- `e0b2b52` - Initial attempt to skip .Focus() during gamepad
- `7d467b9` - Added WinUI focus clearing in SetFocus()
- `1ce3c0f` - Switched to LayoutRoot for focus clearing
- `57872be` - Added IsTabStop="False" to LayoutRoot (didn't work)
- `d1a9c04` - Tried TryMoveFocus(None) (caused errors)
- `11f9194` - **Final working solution with Background="Transparent" and FocusState.Pointer**
