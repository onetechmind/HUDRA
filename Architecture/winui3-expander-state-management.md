# WinUI 3 Expander State Management

## Overview

This document provides a proven implementation for maintaining WinUI 3 Expander control state during page navigation within a single application session. The solution ensures that when users expand an Expander and navigate to other pages, the Expander remains in the same state when they return to the original page.

**Key Features:**
- Session-only persistence (state resets on app restart)
- Works with WinUI 3 navigation patterns
- Uses `Loaded`/`Unloaded` events for reliable state management
- Minimal code overhead
- Easily replicable for multiple Expanders

## Problem Context

In WinUI 3 applications, Expander controls lose their expanded/collapsed state when users navigate between pages. Standard navigation events (`OnNavigatedTo`/`OnNavigatedFrom`) may not fire reliably in all navigation implementations, making state preservation challenging.

## Working Implementation

### 1. Static Field for Session Storage

```csharp
// Session-only state storage for Game Detection Expander
private static bool _gameDetectionExpanderExpanded = false;
```

**Key Points:**
- Use `static` to persist across page instances
- Use descriptive naming for multiple Expanders
- Initializes to `false` (collapsed by default)

### 2. Event Handler Registration

```csharp
public SettingsPage()
{
    this.InitializeComponent();
    this.Loaded += SettingsPage_Loaded;
    this.Unloaded += SettingsPage_Unloaded;
    // ... other initialization code
}
```

**Key Points:**
- Register both `Loaded` and `Unloaded` events in constructor
- `Loaded` restores state when page becomes visible
- `Unloaded` saves state when page is removed from view

### 3. State Loading Implementation

```csharp
private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
{
    System.Diagnostics.Debug.WriteLine("[SettingsPage_Loaded] Page loaded, setting expander state");
    
    // Load Game Detection Expander state (session-only) after page UI is loaded
    if (GameDetectionExpander != null)
    {
        System.Diagnostics.Debug.WriteLine($"[SettingsPage_Loaded] Loading expander state: {_gameDetectionExpanderExpanded}");
        GameDetectionExpander.IsExpanded = _gameDetectionExpanderExpanded;
        System.Diagnostics.Debug.WriteLine($"[SettingsPage_Loaded] Expander IsExpanded set to: {GameDetectionExpander.IsExpanded}");
    }
    else
    {
        System.Diagnostics.Debug.WriteLine("[SettingsPage_Loaded] GameDetectionExpander is null!");
    }
}
```

**Key Points:**
- Always check for null before accessing Expander
- Set `IsExpanded` property directly
- Include debug logging for troubleshooting
- Runs after UI is fully loaded and accessible

### 4. State Saving Implementation

```csharp
private void SettingsPage_Unloaded(object sender, RoutedEventArgs e)
{
    System.Diagnostics.Debug.WriteLine("[SettingsPage_Unloaded] Page unloaded, saving expander state");
    
    // Save expander state when leaving the page (session-only)
    if (GameDetectionExpander != null)
    {
        System.Diagnostics.Debug.WriteLine($"[SettingsPage_Unloaded] Current expander state: {GameDetectionExpander.IsExpanded}");
        _gameDetectionExpanderExpanded = GameDetectionExpander.IsExpanded;
        System.Diagnostics.Debug.WriteLine($"[SettingsPage_Unloaded] Saved state to static field: {_gameDetectionExpanderExpanded}");
    }
    else
    {
        System.Diagnostics.Debug.WriteLine("[SettingsPage_Unloaded] GameDetectionExpander is null!");
    }
}
```

**Key Points:**
- Captures current `IsExpanded` value
- Saves to static field for session persistence
- Includes comprehensive debug logging
- Runs when page is removed from navigation stack

### 5. XAML Requirements

```xml
<Expander
    x:Name="GameDetectionExpander"
    Width="280"
    Padding="5"
    CornerRadius="12">
    <Expander.Header>
        <TextBlock
            VerticalAlignment="Center"
            FontFamily="Cascadia Code"
            FontSize="16"
            FontWeight="SemiBold"
            Text="Game Detection" />
    </Expander.Header>
    <!-- Expander content -->
</Expander>
```

**Key Requirements:**
- Must have `x:Name` attribute for code-behind access
- Name should match the static field naming convention

## Step-by-Step Implementation Guide

### For Adding State Management to New Expanders:

1. **Add Static Field**
   ```csharp
   private static bool _yourExpanderNameExpanded = false;
   ```

2. **Register Events in Constructor**
   ```csharp
   this.Loaded += YourPage_Loaded;
   this.Unloaded += YourPage_Unloaded;
   ```

3. **Implement Loaded Handler**
   ```csharp
   private void YourPage_Loaded(object sender, RoutedEventArgs e)
   {
       if (YourExpanderName != null)
       {
           YourExpanderName.IsExpanded = _yourExpanderNameExpanded;
       }
   }
   ```

4. **Implement Unloaded Handler**
   ```csharp
   private void YourPage_Unloaded(object sender, RoutedEventArgs e)
   {
       if (YourExpanderName != null)
       {
           _yourExpanderNameExpanded = YourExpanderName.IsExpanded;
       }
   }
   ```

5. **Add x:Name to XAML**
   ```xml
   <Expander x:Name="YourExpanderName">
   ```

## Multiple Expanders on Same Page

For multiple Expanders on the same page:

```csharp
// Static fields for each Expander
private static bool _gameDetectionExpanderExpanded = false;
private static bool _displaySettingsExpanderExpanded = false;
private static bool _advancedOptionsExpanderExpanded = false;

private void YourPage_Loaded(object sender, RoutedEventArgs e)
{
    if (GameDetectionExpander != null)
        GameDetectionExpander.IsExpanded = _gameDetectionExpanderExpanded;
    
    if (DisplaySettingsExpander != null)
        DisplaySettingsExpander.IsExpanded = _displaySettingsExpanderExpanded;
    
    if (AdvancedOptionsExpander != null)
        AdvancedOptionsExpander.IsExpanded = _advancedOptionsExpanderExpanded;
}

private void YourPage_Unloaded(object sender, RoutedEventArgs e)
{
    if (GameDetectionExpander != null)
        _gameDetectionExpanderExpanded = GameDetectionExpander.IsExpanded;
    
    if (DisplaySettingsExpander != null)
        _displaySettingsExpanderExpanded = DisplaySettingsExpander.IsExpanded;
    
    if (AdvancedOptionsExpander != null)
        _advancedOptionsExpanderExpanded = AdvancedOptionsExpander.IsExpanded;
}
```

## Debug Output

### Expected Console Output

When working correctly, you should see:

**When navigating TO the page:**
```
[SettingsPage_Loaded] Page loaded, setting expander state
[SettingsPage_Loaded] Loading expander state: true
[SettingsPage_Loaded] Expander IsExpanded set to: True
```

**When navigating AWAY from the page:**
```
[SettingsPage_Unloaded] Page unloaded, saving expander state
[SettingsPage_Unloaded] Current expander state: True
[SettingsPage_Unloaded] Saved state to static field: True
```

### Troubleshooting Missing Output

If you don't see the expected debug output:
1. Check that event handlers are registered in constructor
2. Verify `x:Name` is set in XAML
3. Ensure navigation system properly fires `Loaded`/`Unloaded` events
4. Check debug output window is configured correctly

## What Doesn't Work

### OnNavigatedTo/OnNavigatedFrom Events

```csharp
// ❌ These events may not fire reliably in all WinUI 3 navigation implementations
protected override void OnNavigatedTo(NavigationEventArgs e) { }
protected override void OnNavigatedFrom(NavigationEventArgs e) { }
```

**Why this fails:**
- Custom navigation services may not trigger these events
- Frame navigation patterns vary between implementations
- Events may fire at wrong lifecycle stage

### Dependency Properties with Two-Way Binding

```xml
<!-- ❌ This approach failed due to XAML compilation issues -->
<Expander IsExpanded="{x:Bind IsGameDetectionExpanded, Mode=TwoWay}">
```

**Why this fails:**
- Complex dependency property setup required
- XAML compilation errors with binding syntax
- More complex than static field approach

## Session vs Persistent Storage

### Session-Only (Current Implementation)
- ✅ State persists during app session
- ✅ State resets on app restart
- ✅ Minimal complexity
- ✅ No file I/O overhead

### Persistent Storage (Not Implemented)
- ❌ State survives app restarts
- ❌ Requires SettingsService integration
- ❌ Additional complexity
- ❌ File I/O overhead

**Note:** The current implementation uses session-only storage by design. If persistent storage is needed, integrate with `SettingsService.cs` instead of static fields.

## Controls Inside Collapsed Expanders

### The Problem

When controls are placed inside a collapsed Expander and initialized before the Expander is expanded, they may not display correctly. This is especially problematic for controls that require accurate measurement and scroll positioning, such as:
- ScrollViewer-based controls
- ItemsRepeater with dynamic positioning
- Custom pickers/selectors with scroll-to-item functionality

**Root Cause:** When a control is inside a collapsed Expander, it's not in the visual tree during initialization. ScrollViewer dimensions (`ViewportWidth`, `ExtentWidth`) are not available, causing scroll positioning to fail.

### The Solution

Use the control's `Loaded` event to ensure proper initialization when the control enters the visual tree:

```csharp
public TdpPickerControl()
{
    this.InitializeComponent();
    InitializeData();
    this.Loaded += TdpPickerControl_Loaded;
}

private void TdpPickerControl_Loaded(object sender, RoutedEventArgs e)
{
    // When control is loaded (especially after being in a collapsed expander),
    // ensure scroll position is correct
    if (_isInitialized)
    {
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            ScrollToSelectedItem();
        });
    }
}
```

**Key Points:**
- The `Loaded` event fires when the control enters the visual tree
- This happens when a collapsed Expander is expanded for the first time
- Use `DispatcherQueue.TryEnqueue` with `Low` priority to ensure layout is complete
- Only refresh positioning/scrolling, don't re-initialize the entire control
- Check `_isInitialized` flag to avoid running before control is ready

### Example: TDP Picker Display Issue

**Problem:** TdpPickerControl in SettingsPage was showing "5" and "6" instead of the saved default TDP value (e.g., 15W).

**Why:** The control was initialized inside a collapsed `TdpSettingsExpander`, so `ScrollToSelectedItem()` failed because ScrollViewer dimensions were unavailable.

**Fix:** Added `Loaded` event handler to refresh scroll position when the Expander is expanded and the control enters the visual tree.

### When to Use This Pattern

Use the `Loaded` event pattern for controls inside Expanders when:
1. Control has scroll positioning or centering logic
2. Control needs accurate measurement for layout
3. Control's `Initialize()` method is called before the Expander is expanded
4. Control displays wrong initial state when Expander is first expanded

### Additional Considerations

**Complement with Expander State Callbacks:**
```csharp
// In parent page
if (TdpSettingsExpander != null)
{
    _tdpExpanderCallbackToken = TdpSettingsExpander.RegisterPropertyChangedCallback(
        Microsoft.UI.Xaml.Controls.Expander.IsExpandedProperty,
        OnTdpExpanderStateChanged);
}

private void OnTdpExpanderStateChanged(DependencyObject sender, DependencyProperty dp)
{
    // Only refresh when expander is expanded
    if (TdpSettingsExpander != null && TdpSettingsExpander.IsExpanded)
    {
        RefreshControlDisplay();
    }
}
```

This provides an additional opportunity to refresh control display when the Expander expands, complementing the control's own `Loaded` event.

## Performance Considerations

- **Minimal overhead**: Only saves/loads boolean values
- **No file I/O**: Session storage avoids disk operations
- **Event efficiency**: `Loaded`/`Unloaded` events fire only when necessary
- **Memory usage**: Static fields persist in memory for app lifetime

## Future Enhancements

### Potential Improvements:
1. **Generic Helper Class**: Create reusable utility for Expander state management
2. **Configuration-based**: Use settings file to define which Expanders need state management
3. **Animation Support**: Maintain expansion animations during state restoration
4. **Nested Expanders**: Support for Expanders within Expanders

### Integration with Settings Service:
If persistent storage is ever needed, modify the pattern to use:
```csharp
// Instead of static field
_gameDetectionExpanderExpanded = SettingsService.GetExpanderState("GameDetection");

// Save state
SettingsService.SetExpanderState("GameDetection", GameDetectionExpander.IsExpanded);
```

## Conclusion

This implementation provides reliable, session-persistent Expander state management for WinUI 3 applications. The `Loaded`/`Unloaded` event pattern works consistently across different navigation implementations and requires minimal code changes.

**Key Success Factors:**
1. Use static fields for session storage
2. Register `Loaded`/`Unloaded` events in constructor
3. Always check for null before accessing controls
4. Include debug logging for troubleshooting
5. Ensure `x:Name` is set in XAML
6. **For controls inside Expanders**: Use control's `Loaded` event to refresh layout/positioning when entering visual tree

This pattern can be easily replicated across multiple pages and Expanders in the HUDRA application.