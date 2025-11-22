# Focus Border Refactoring Opportunities

## Executive Summary

HUDRA uses a **dual-layer focus border system** that creates visual inconsistencies and double-border issues when switching between keyboard Tab and gamepad navigation. This document identifies refactoring opportunities to unify the focus border mechanism.

**Core Issue**: Gamepad navigation highlights outer container borders, while keyboard Tab navigation highlights inner interactive controls, leading to:
- Double borders appearing when switching input methods
- Inconsistent visual feedback between input modes
- Incomplete implementation of documented best practices

## Current Architecture

### Two-Layer Border Pattern

Most gamepad-navigable controls follow this structure:

```xml
<!-- OUTER BORDER: Gamepad focus (DarkViolet via FocusBrush binding) -->
<Border BorderBrush="{x:Bind SomeFocusBrush, Mode=OneWay}"
        BorderThickness="2"
        CornerRadius="12">

    <!-- MIDDLE BORDER: Visual styling -->
    <Border Background="#22FFFFFF"
            CornerRadius="12"
            Padding="12,8">

        <!-- INNER CONTROL: Can receive Tab focus and shows WinUI system focus -->
        <ToggleSwitch x:Name="MyToggle"
                      Toggled="OnToggled" />
        <!-- ⚠️ PROBLEM: Missing UseSystemFocusVisuals="False" and IsTabStop="False" -->
    </Border>
</Border>
```

### Focus Border Sources

**Gamepad Navigation**:
- Custom `DarkViolet` borders on **outer containers**
- Controlled via `IsFocused` property and computed brush properties
- Examples: `ToggleFocusBrush`, `DefaultProfileFocusBrush`, `IntelligentSwitchingFocusBrush`

**Keyboard Tab Navigation**:
- WinUI system focus visuals on **inner controls**
- Automatically applied via global resources in `App.xaml`:
  ```xml
  <SolidColorBrush x:Key="SystemControlFocusVisualPrimaryBrush" Color="DarkViolet" />
  <SolidColorBrush x:Key="SystemControlFocusVisualSecondaryBrush" Color="DarkViolet" />
  ```
- Affects any control with `IsTabStop="True"` (the default)

## Root Cause Analysis

### Why Double Borders Occur

1. **Inner controls have default WinUI behavior**:
   - `IsTabStop="True"` (implicit default) → Can receive Tab focus
   - `UseSystemFocusVisuals="True"` (implicit default) → Shows DarkViolet focus rectangle

2. **Timing issues when switching input modes**:
   ```
   Scenario: User tabs to ToggleSwitch, then uses gamepad

   State Before Gamepad Input:
   - Inner ToggleSwitch: Has WinUI focus (DarkViolet rectangle)
   - Outer Border: Transparent (gamepad not active)

   Gamepad Input Detected:
   1. GamepadNavigationService.ClearFocus() called
   2. Attempts to move focus to Frame (IsTabStop=False)
   3. GamepadNavigationService.SetFocus() on UserControl
   4. Outer Border becomes DarkViolet

   ⚠️ RACE CONDITION:
   - Brief moment where BOTH borders show DarkViolet
   - Inner ToggleSwitch may not release focus immediately
   - Visual glitch: Double border
   ```

3. **Incomplete application of documentation guidelines**:
   - `Architecture/winui3-gamepad-navigation.md:1079` states:
     > "Always set `UseSystemFocusVisuals="False"` and `IsTabStop="False"` on interactive controls"
   - This guideline is **NOT applied** to most ToggleSwitches, ComboBoxes, Buttons, and HyperlinkButtons

### Affected Controls Analysis

**Controls WITH Outer Gamepad Borders:**
- ✅ All have custom `*FocusBrush` properties bound to outer Border
- ❌ Most inner controls lack `UseSystemFocusVisuals="False"` and `IsTabStop="False"`

**Inventory of Missing Attributes:**

| Control File | Inner Controls Missing Attributes | Count |
|--------------|-----------------------------------|-------|
| `FanCurveControl.xaml` | ToggleSwitch (line 52) | 1 |
| `FanControlControl.xaml` | ToggleSwitch (line 58) | 1 |
| `GameDetectionControl.xaml` | ToggleSwitch (line 47), ComboBox | 2+ |
| `AmdFeaturesControl.xaml` | ToggleSwitches (lines 46, 134, 175) | 3 |
| `LosslessScalingControl.xaml` | ToggleSwitch (line 50) | 1 |
| `PowerProfileControl.xaml` | ComboBoxes (lines 41, 76), ToggleSwitches (lines 123, 154) | 4 |
| `StartupOptionsControl.xaml` | ToggleSwitches (lines 44, 84, 124) | 3 |
| `TdpSettingsControl.xaml` | ToggleSwitches (lines 43, 98) | 2 |
| **Total** | | **17+** |

**Controls CORRECTLY Implemented:**
- ✅ `ResolutionPickerControl.xaml` - ComboBoxes have `UseSystemFocusVisuals="False"` (implied by documentation)
- ✅ `FpsLimiterControl.xaml` - ComboBox correctly configured
- ✅ `BrightnessControlControl.xaml` - Slider correctly configured
- ✅ `AudioControlsControl.xaml` - Slider and Button correctly configured

## Refactoring Opportunities

### 🔧 Opportunity 1: Global Style Solution (RECOMMENDED)

**Impact**: Medium effort, high consistency

Create global styles in `App.xaml` that disable system focus visuals for controls inside gamepad-navigable containers.

**Implementation**:

```xml
<!-- App.xaml - Add to Application.Resources -->

<!-- Global ToggleSwitch style for gamepad-navigable containers -->
<Style x:Key="GamepadToggleSwitchStyle" TargetType="ToggleSwitch" BasedOn="{StaticResource {x:Type ToggleSwitch}}">
    <Setter Property="UseSystemFocusVisuals" Value="False" />
    <Setter Property="IsTabStop" Value="False" />
</Style>

<!-- Global ComboBox style for gamepad-navigable containers -->
<Style x:Key="GamepadComboBoxStyle" TargetType="ComboBox" BasedOn="{StaticResource HudraComboBoxStyle}">
    <Setter Property="UseSystemFocusVisuals" Value="False" />
    <Setter Property="IsTabStop" Value="False" />
</Style>

<!-- Global Button style for gamepad-navigable containers -->
<Style x:Key="GamepadButtonStyle" TargetType="Button">
    <Setter Property="UseSystemFocusVisuals" Value="False" />
    <Setter Property="IsTabStop" Value="False" />
</Style>

<!-- Global HyperlinkButton style for gamepad-navigable containers -->
<Style x:Key="GamepadHyperlinkButtonStyle" TargetType="HyperlinkButton">
    <Setter Property="UseSystemFocusVisuals" Value="False" />
    <Setter Property="IsTabStop" Value="False" />
</Style>
```

**Usage in controls**:

```xml
<!-- Before -->
<ToggleSwitch x:Name="MyToggle" />

<!-- After -->
<ToggleSwitch x:Name="MyToggle"
              Style="{StaticResource GamepadToggleSwitchStyle}" />
```

**Pros**:
- Centralized style definitions
- Easy to maintain and update
- Clear intent through naming
- Can be applied incrementally

**Cons**:
- Requires updating 17+ control instances
- Must remember to use style for new controls
- Doesn't prevent mistakes

### 🔧 Opportunity 2: Implicit Styles (HIGH RISK)

**Impact**: Low effort, global effect, **BREAKING CHANGE RISK**

Modify the global `TargetType="ToggleSwitch"` style in `App.xaml:143-150` to disable system focus visuals by default.

**Implementation**:

```xml
<!-- App.xaml - Modify existing global style -->
<Style TargetType="ToggleSwitch">
    <Setter Property="FontFamily" Value="Cascadia Code" />
    <Setter Property="FontSize" Value="12" />
    <Setter Property="FontWeight" Value="SemiBold" />
    <Setter Property="Foreground" Value="White" />
    <Setter Property="OnContent" Value="" />
    <Setter Property="OffContent" Value="" />
    <!-- NEW: Disable system focus visuals globally -->
    <Setter Property="UseSystemFocusVisuals" Value="False" />
    <Setter Property="IsTabStop" Value="False" />
</Style>
```

**⚠️ CRITICAL RISK**:
- **Breaks ALL keyboard Tab navigation** throughout the app
- Settings page controls would become keyboard-inaccessible
- Non-gamepad users severely impacted
- Violates accessibility guidelines

**Do NOT use this approach** unless you want to completely disable keyboard navigation.

### 🔧 Opportunity 3: Attached Property Solution (BEST PRACTICE)

**Impact**: High effort, most flexible and maintainable

Create an attached property that automatically configures focus behavior based on parent context.

**Implementation**:

```csharp
// HUDRA/AttachedProperties/FocusManagement.cs (NEW FILE)
namespace HUDRA.AttachedProperties
{
    public static class FocusManagement
    {
        public static readonly DependencyProperty UseGamepadFocusProperty =
            DependencyProperty.RegisterAttached(
                "UseGamepadFocus",
                typeof(bool),
                typeof(FocusManagement),
                new PropertyMetadata(false, OnUseGamepadFocusChanged));

        public static bool GetUseGamepadFocus(DependencyObject obj)
            => (bool)obj.GetValue(UseGamepadFocusProperty);

        public static void SetUseGamepadFocus(DependencyObject obj, bool value)
            => obj.SetValue(UseGamepadFocusProperty, value);

        private static void OnUseGamepadFocusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue && d is Control control)
            {
                // Disable system focus visuals when gamepad focus is enabled
                control.UseSystemFocusVisuals = false;
                control.IsTabStop = false;
            }
        }
    }
}
```

**Usage**:

```xml
<!-- Set on individual controls -->
<ToggleSwitch x:Name="MyToggle"
              ap:FocusManagement.UseGamepadFocus="True" />

<!-- Or set on parent container to affect all children -->
<Border ap:FocusManagement.UseGamepadFocus="True">
    <ToggleSwitch x:Name="MyToggle" />
    <ComboBox x:Name="MyCombo" />
    <Button Content="Click" />
</Border>
```

**Pros**:
- Most flexible - can be applied at any level
- Self-documenting intent
- Can be enhanced to restore Tab focus when gamepad inactive
- Reusable across future controls

**Cons**:
- Requires new infrastructure
- Higher initial development cost
- Must remember to use property

### 🔧 Opportunity 4: Per-Control Inline Attributes (SIMPLEST)

**Impact**: Low effort, immediate fix, most explicit

Directly add `UseSystemFocusVisuals="False"` and `IsTabStop="False"` to each affected control.

**Implementation**:

```xml
<!-- Example: FanCurveControl.xaml line 52 -->
<!-- Before -->
<ToggleSwitch x:Name="FanCurveToggle"
              Grid.Column="1"
              MinWidth="0"
              Toggled="FanCurveToggle_Toggled" />

<!-- After -->
<ToggleSwitch x:Name="FanCurveToggle"
              Grid.Column="1"
              MinWidth="0"
              UseSystemFocusVisuals="False"
              IsTabStop="False"
              Toggled="FanCurveToggle_Toggled" />
```

**Files to update** (17+ controls):
1. `FanCurveControl.xaml` - Line 52: ToggleSwitch
2. `FanControlControl.xaml` - Line 58: ToggleSwitch
3. `GameDetectionControl.xaml` - Line 47: ToggleSwitch + ComboBox
4. `AmdFeaturesControl.xaml` - Lines 46, 134, 175: ToggleSwitches
5. `LosslessScalingControl.xaml` - Line 50: ToggleSwitch
6. `PowerProfileControl.xaml` - Lines 41, 76: ComboBoxes; Lines 123, 154: ToggleSwitches; Line 170: HyperlinkButton
7. `StartupOptionsControl.xaml` - Lines 44, 84, 124: ToggleSwitches
8. `TdpSettingsControl.xaml` - Lines 43, 98: ToggleSwitches

**Pros**:
- ✅ Simplest to implement
- ✅ No new infrastructure needed
- ✅ Explicit and clear
- ✅ Follows existing pattern from working controls
- ✅ No risk to unrelated controls

**Cons**:
- Verbose - adds 2 attributes per control
- Must remember for future controls
- No compile-time enforcement

### 🔧 Opportunity 5: Hybrid Approach (PRACTICAL BALANCE)

**Impact**: Medium effort, good maintainability

Combine **Opportunity 1** (Global Styles) + **Opportunity 4** (Inline Attributes) for different scenarios:

**When to use global styles**:
- New controls being added
- Controls with complex styling needs

**When to use inline attributes**:
- Quick fixes to existing controls
- One-off special cases

**Implementation Steps**:
1. Create `GamepadToggleSwitchStyle`, `GamepadComboBoxStyle`, etc. in `App.xaml`
2. Update existing controls with inline attributes as immediate fix
3. Gradually migrate to styles in future refactoring
4. Document in `CLAUDE.md` which approach to use when

**Pros**:
- Immediate fix + long-term solution
- Flexibility for different scenarios
- Incremental migration path

**Cons**:
- Two patterns to maintain
- Requires coordination and documentation

## Recommended Implementation Plan

### Phase 1: Immediate Fix (Low Risk)
**Use Opportunity 4** - Add inline attributes to all 17+ affected controls

**Rationale**:
- Fastest to implement
- Lowest risk of unintended side effects
- Directly addresses the double border bug
- Aligns with documentation best practices

**Validation**:
- Test keyboard Tab navigation still works for page navigation
- Test gamepad navigation maintains current behavior
- Verify no double borders when switching input methods

### Phase 2: Infrastructure Enhancement (Medium Priority)
**Use Opportunity 3** - Create `FocusManagement` attached property

**Rationale**:
- Provides long-term maintainability
- Enables future enhancements (e.g., restoring Tab focus when gamepad inactive)
- Self-documenting code

**Migration**:
- Gradually replace inline attributes with attached property
- Add to new controls by default

### Phase 3: Documentation Update (High Priority)
Update `CLAUDE.md` with clear guidelines:

```markdown
### Interactive Control Focus Guidelines

When adding ToggleSwitch, ComboBox, Button, or HyperlinkButton inside a gamepad-navigable container:

**REQUIRED**: Disable WinUI system focus visuals to prevent double borders:

```xml
<ToggleSwitch x:Name="MyToggle"
              UseSystemFocusVisuals="False"
              IsTabStop="False" />
```

**Why**: Gamepad navigation uses outer container borders for focus visualization. Inner control focus visuals create double borders.

**Exception**: Controls that should be keyboard-accessible outside gamepad navigation (e.g., Settings page forms).
```

## Testing Strategy

### Test Cases

**TC1: Keyboard Tab Navigation**
- ✅ Can still tab between pages using L1/R1 page buttons (they should keep `IsTabStop="True"`)
- ✅ Cannot tab to inner controls (ToggleSwitch, ComboBox, etc.)
- ✅ Gamepad navigation takes over for within-page controls

**TC2: Gamepad Navigation**
- ✅ D-pad navigation works as before
- ✅ Only outer borders show DarkViolet when focused
- ✅ A button activates controls

**TC3: Input Method Switching**
- ✅ Tab to page → Use gamepad → No double border
- ✅ Gamepad on control → Tab to next page → No lingering gamepad border
- ✅ Touch/mouse → Gamepad → No double border

**TC4: Visual Consistency**
- ✅ All focused controls show DarkViolet outer border
- ✅ No DarkViolet on inner controls
- ✅ Border thickness consistent (2px)

### Regression Testing

**Areas to validate**:
- Settings page keyboard accessibility (should NOT be affected - those controls aren't in gamepad-navigable containers)
- Expander controls still work
- ComboBox dropdown navigation with gamepad
- Slider adjustment with gamepad

## Alternative: Tab Stop Restoration (FUTURE CONSIDERATION)

**Concept**: Instead of permanently disabling `IsTabStop`, dynamically enable/disable based on input mode.

**Potential Implementation**:

```csharp
public class FocusManagementHelper
{
    public static void EnableKeyboardFocus(FrameworkElement root)
    {
        // Find all interactive controls and set IsTabStop="True"
    }

    public static void EnableGamepadFocus(FrameworkElement root)
    {
        // Find all interactive controls and set IsTabStop="False"
    }
}
```

**Call when input mode changes**:
```csharp
private void OnNonGamepadInput(object sender, object e)
{
    if (_gamepadNavigationService?.IsGamepadActive == true)
    {
        _gamepadNavigationService.ClearFocus();
        _gamepadNavigationService.DeactivateGamepadMode();
        FocusManagementHelper.EnableKeyboardFocus(LayoutRoot); // NEW
    }
}
```

**Pros**:
- Maintains full keyboard accessibility
- Best user experience for dual input scenarios

**Cons**:
- Complex implementation
- Risk of focus state confusion
- May conflict with existing navigation system
- Not needed if gamepad navigation already covers all interactive controls

**Recommendation**: **Do not implement** unless user feedback indicates keyboard Tab navigation within pages is needed. Current gamepad navigation system is comprehensive and preferred.

## Summary Table

| Opportunity | Effort | Risk | Maintainability | Recommendation |
|-------------|--------|------|-----------------|----------------|
| 1. Global Styles | Medium | Low | Good | ⭐ Phase 2 |
| 2. Implicit Styles | Low | **HIGH** | Poor | ❌ Do NOT use |
| 3. Attached Property | High | Low | Excellent | ⭐⭐ Phase 2 |
| 4. Inline Attributes | Low | Low | Fair | ⭐⭐⭐ **Phase 1 - IMMEDIATE** |
| 5. Hybrid Approach | Medium | Low | Good | ⭐ Alternative |

## Key Files Reference

**Services**:
- `/home/user/HUDRA/HUDRA/Services/GamepadNavigationService.cs:823-850` - `ClearFocus()` method
- `/home/user/HUDRA/HUDRA/MainWindow.xaml.cs:873-884` - `OnNonGamepadInput()` handler

**Styles**:
- `/home/user/HUDRA/HUDRA/App.xaml:16-17` - Global focus visual brushes
- `/home/user/HUDRA/HUDRA/App.xaml:143-150` - Global ToggleSwitch style

**Controls to Update** (17+ instances):
- See "Affected Controls Analysis" table above

**Documentation**:
- `/home/user/HUDRA/Architecture/winui3-gamepad-navigation.md:1079` - Best practice guideline

---

**Document Version**: 1.0
**Date**: 2025-11-22
**Related Issues**: Double border bug when switching input methods
