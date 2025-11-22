# Focus Border Refactoring - Current Code Analysis

## Critical Finding

**The architecture documentation is outdated.** Analysis of actual code reveals:

- ✅ `UseSystemFocusVisuals="False"` - **ZERO instances** in codebase
- ✅ Controls that "work correctly" (ResolutionPicker, FpsLimiter, AudioControls, BrightnessControl) - **NONE use these attributes**
- ✅ Only `IsTabStop="False"` usage: **Decorative elements** (TextBlocks, one non-interactive Button)

## Root Cause of Double Borders

Located in `/home/user/HUDRA/HUDRA/Services/GamepadNavigationService.cs:808`:

```csharp
public void SetFocus(FrameworkElement? element)
{
    // ... clear previous focus ...

    _currentFocusedElement = element;

    if (_currentFocusedElement != null)
    {
        GamepadNavigation.SetIsCurrentFocus(_currentFocusedElement, true); // ← Custom gamepad focus
        _currentFocusedElement.Focus(FocusState.Programmatic);             // ← PROBLEM LINE

        _currentFocusedElement.StartBringIntoView();
    }
}
```

### The Problem Flow

1. **SetFocus() calls `.Focus(FocusState.Programmatic)` on UserControl**
2. UserControls have `IsTabStop=False` (default)
3. **WinUI delegates programmatic focus to first child with `IsTabStop=True`**
4. First child is typically ToggleSwitch, ComboBox, Button, etc.
5. **Child receives WinUI focus** → shows `SystemControlFocusVisualPrimaryBrush` (DarkViolet)
6. **UserControl also has gamepad focus** → outer Border shows DarkViolet via binding
7. **Result: DOUBLE BORDERS** (outer + inner)

This happens **every time** gamepad navigation moves focus, not just when switching input methods.

## Why ClearFocus() "Does Not Work 100%"

From `GamepadNavigationService.cs:823-850`:

```csharp
public void ClearFocus()
{
    // Clears gamepad focus
    if (_currentFocusedElement != null)
    {
        GamepadNavigation.SetIsCurrentFocus(_currentFocusedElement, false);
        _currentFocusedElement = null;
    }

    // Tries to clear WinUI focus by focusing Frame (IsTabStop=False)
    var focusedElement = FocusManager.GetFocusedElement(_currentFrame.XamlRoot) as UIElement;
    if (focusedElement != null)
    {
        _currentFrame.Focus(FocusState.Programmatic);
    }
}
```

**This clears focus momentarily, but then SetFocus() is called immediately after**, which calls `.Focus()` again, re-creating the WinUI focus on an inner control.

## Actual Current Architecture

### What Controls Look Like

**Example: FanCurveControl.xaml (lines 24-62)**

```xml
<!-- OUTER BORDER: Gamepad focus binding -->
<Border BorderBrush="{x:Bind ToggleFocusBrush, Mode=OneWay}"
        BorderThickness="2"
        CornerRadius="12">

    <Border Background="#22FFFFFF" CornerRadius="12" Padding="12,8">
        <Grid>
            <TextBlock Text="Custom Fan Curve" ... />

            <!-- INNER CONTROL: Default WinUI behavior -->
            <ToggleSwitch x:Name="FanCurveToggle"
                          Toggled="FanCurveToggle_Toggled" />
            <!-- ⚠️ No UseSystemFocusVisuals, No IsTabStop attributes -->
            <!-- Defaults: IsTabStop=True, UseSystemFocusVisuals=True -->
        </Grid>
    </Border>
</Border>
```

### What Focus Brush Properties Do

**Example: FanCurveControl.xaml.cs (lines 120-140)**

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

- Returns DarkViolet when: gamepad focused on this sub-element
- Binds to outer Border's BorderBrush
- **Independent of WinUI focus system**

### Global Focus Visuals

**App.xaml (lines 16-17)**

```xml
<SolidColorBrush x:Key="SystemControlFocusVisualPrimaryBrush" Color="DarkViolet" />
<SolidColorBrush x:Key="SystemControlFocusVisualSecondaryBrush" Color="DarkViolet" />
```

- Applied automatically by WinUI to ALL controls that receive focus
- No way to disable globally without breaking everything
- Used for keyboard Tab navigation focus rectangles

## Refactoring Opportunities (Actual Code)

### ⭐ Option 1: Remove .Focus() Call When Gamepad Active (RECOMMENDED)

**Change: GamepadNavigationService.cs:808**

```csharp
public void SetFocus(FrameworkElement? element)
{
    if (_currentFocusedElement != null)
    {
        GamepadNavigation.SetIsCurrentFocus(_currentFocusedElement, false);
    }

    _currentFocusedElement = element;

    if (_currentFocusedElement != null)
    {
        GamepadNavigation.SetIsCurrentFocus(_currentFocusedElement, true);

        // ONLY call .Focus() if NOT using gamepad navigation
        // Gamepad navigation uses custom IsCurrentFocus, doesn't need WinUI focus
        if (!_isGamepadActive)
        {
            _currentFocusedElement.Focus(FocusState.Programmatic);
        }

        // Scroll into view regardless
        _currentFocusedElement.StartBringIntoView();
    }
}
```

**Pros:**
- ✅ Fixes double border at the source
- ✅ No XAML changes needed
- ✅ Minimal code change (1 if statement)
- ✅ Gamepad navigation doesn't need WinUI focus - it has custom focus tracking
- ✅ Keyboard focus still works when gamepad inactive

**Cons:**
- ❓ Unknown if any code depends on WinUI focus being set during gamepad navigation
- ❓ Might affect keyboard event routing (needs testing)

**Testing needed:**
- Verify gamepad navigation still works (likely yes - uses custom system)
- Verify A button activation still works
- Verify scrolling still works (uses StartBringIntoView separately)
- Verify no regressions when switching between input methods

---

### Option 2: Add IsTabStop="False" to Inner Controls

**Would need to add to 17+ controls across 8 files.**

**Example change:**

```xml
<ToggleSwitch x:Name="FanCurveToggle"
              IsTabStop="False"
              Toggled="FanCurveToggle_Toggled" />
```

**Pros:**
- ✅ Prevents WinUI from delegating focus to inner controls
- ✅ When UserControl.Focus() is called, nothing receives focus (UserControl itself is not tabbable)

**Cons:**
- ❌ Requires 17+ XAML edits
- ❌ Changes control behavior (can't be focused directly)
- ⚠️ If .Focus() doesn't work on non-tabbable UserControl, might cause other issues
- ⚠️ Disables any potential keyboard Tab navigation within pages (might be intentional?)

**Current keyboard navigation:**
- Navigation buttons (sidebar) have `TabIndex="1"` through `TabIndex="4"`
- Allows Tab to cycle through pages
- **Within-page Tab navigation appears intentionally unused** (only gamepad D-pad)

---

### Option 3: Conditionally Clear WinUI Focus After SetFocus()

**Add to GamepadNavigationService after SetFocus() calls**

```csharp
private void SetFocus(FrameworkElement? element)
{
    // ... existing code ...
    _currentFocusedElement.Focus(FocusState.Programmatic);

    // NEW: If gamepad active, immediately clear WinUI focus from delegated children
    if (_isGamepadActive && _currentFrame?.XamlRoot != null)
    {
        var focusedChild = FocusManager.GetFocusedElement(_currentFrame.XamlRoot);
        if (focusedChild != _currentFocusedElement && focusedChild is UIElement)
        {
            // WinUI delegated focus to a child - clear it
            _currentFocusedElement.Focus(FocusState.Programmatic);
            System.Diagnostics.Debug.WriteLine($"🎮 Cleared delegated focus from child control");
        }
    }

    _currentFocusedElement.StartBringIntoView();
}
```

**Pros:**
- ✅ No XAML changes
- ✅ Keeps .Focus() call (safe if something depends on it)
- ✅ Specifically targets the delegation issue

**Cons:**
- ❌ Hacky - fighting WinUI's focus system
- ❌ Might cause focus flicker
- ❌ Still unclear why .Focus() is needed during gamepad navigation

---

### Option 4: Don't Call Focus During Gamepad, Restore Tab Accessibility

**More complex: Make controls keyboard-accessible when gamepad inactive**

```csharp
public void SetGamepadActive(bool active)
{
    _isGamepadActive = active;

    if (active)
    {
        // Disable Tab stops on all inner controls
        SetInnerControlsTabStop(false);
    }
    else
    {
        // Re-enable Tab stops for keyboard users
        SetInnerControlsTabStop(true);
    }
}

private void SetInnerControlsTabStop(bool enabled)
{
    // Find all ToggleSwitch, ComboBox, Button, Slider in current page
    // Set IsTabStop dynamically
}
```

**Pros:**
- ✅ Maximum flexibility
- ✅ Supports both input methods properly

**Cons:**
- ❌ High complexity
- ❌ Performance impact (tree walking)
- ❌ Might conflict with existing tab order
- ❌ Unclear if keyboard Tab within pages is desired

---

## Recommended Approach

### Phase 1: Test Option 1 (Don't call .Focus() when gamepad active)

**Why:**
1. Simplest change (3 lines of code)
2. Addresses root cause directly
3. Gamepad navigation uses custom focus system - doesn't need WinUI focus
4. Can be reverted easily if issues found

**Implementation:**

```csharp
// GamepadNavigationService.cs, line ~808
if (_currentFocusedElement != null)
{
    GamepadNavigation.SetIsCurrentFocus(_currentFocusedElement, true);

    // NEW: Only set WinUI focus when gamepad is NOT active
    if (!_isGamepadActive)
    {
        _currentFocusedElement.Focus(FocusState.Programmatic);
    }

    _currentFocusedElement.StartBringIntoView();
}
```

**Testing checklist:**
- [ ] Gamepad D-pad navigation works
- [ ] Gamepad A button activates controls (toggle switches, open comboboxes, press buttons)
- [ ] Gamepad B button cancels
- [ ] Scrolling into view works
- [ ] Switching from keyboard to gamepad - no double borders
- [ ] Switching from gamepad to keyboard - no lingering borders
- [ ] Page navigation with L1/R1 works
- [ ] Keyboard Tab through navigation buttons works

### Phase 2: If Phase 1 Causes Issues

Consider **Option 2** (add `IsTabStop="False"` to inner controls) as fallback.

### Phase 3: Document Findings

Update `/home/user/HUDRA/Architecture/winui3-gamepad-navigation.md` with actual working implementation, removing outdated `UseSystemFocusVisuals` guidance.

---

## Additional Findings

### GamepadComboBoxHelper

Located at `/home/user/HUDRA/HUDRA/Helpers/GamepadComboBoxHelper.cs`

- Does NOT set `UseSystemFocusVisuals` or `IsTabStop`
- Only handles keyboard events (GamepadA, GamepadB, D-pad)
- Prevents default D-pad behavior when ComboBox collapsed
- Implements A=select, B=cancel for dropdown

### Controls Using GamepadComboBoxHelper

- ResolutionPickerControl (2 ComboBoxes)
- FpsLimiterControl (1 ComboBox)
- PowerProfileControl (2 ComboBoxes)
- GameDetectionControl (ComboBox)

**These controls do NOT have special focus handling** - they rely on the same SetFocus() mechanism that causes double borders.

---

## Why Documentation Was Wrong

The architecture doc likely described an **intended** implementation that was never completed, or was based on an earlier prototype that was refactored out. The actual current code uses a different approach:

**Documented (but not implemented):**
- UseSystemFocusVisuals="False" on controls
- IsTabStop="False" on controls
- Complete separation of focus systems

**Actually implemented:**
- Custom gamepad focus tracking (GamepadNavigation.IsCurrentFocus)
- WinUI focus still used (via .Focus() call)
- No separation → double borders when both active

---

## Questions for Consideration

1. **Why does SetFocus() call .Focus() at all?**
   - Originally for scrolling? (now done separately with StartBringIntoView)
   - For keyboard event routing? (test if gamepad events still work without it)
   - Legacy code from earlier implementation?

2. **Is within-page keyboard Tab navigation intentionally disabled?**
   - Only navigation buttons have explicit TabIndex
   - No evidence of Tab navigation design within pages
   - Gamepad D-pad seems to be the primary navigation method

3. **What is the intended keyboard-only user experience?**
   - Mouse/touch for clicking controls?
   - Gamepad required for full keyboard-only navigation?
   - Or should Tab work within pages?

---

**Analysis Date:** 2025-11-22
**Based On:** Actual codebase inspection, not documentation
