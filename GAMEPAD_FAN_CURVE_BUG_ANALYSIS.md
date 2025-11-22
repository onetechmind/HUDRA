# Gamepad Fan Curve Control Point Movement - Root Cause Analysis

## Bug Description
When using gamepad (d-pad) to move fan curve control points in Custom mode, **some points don't move at all in certain directions**, even though they should be able to move.

## Root Cause Identified

### The Problem: Boundary Constraint + Delta Check + 1-Degree Increment

When a control point is at or near its minimum/maximum temperature constraint (2-degree gap from adjacent points), the gamepad code:

1. Increments temperature by ±1 degree
2. Applies constraint (clamps to valid range)
3. **Checks if constrained value differs from current value**
4. Only updates if difference > 0.01

**This fails when the point is at the boundary!**

### Example Scenario (Point Can't Move Left)

```
Given:
- Point[1] (previous) is at 50°C
- Point[2] (current) is at 52°C  ← At minimum gap (50 + 2 = 52)
- Point[3] (next) is at 60°C

User presses D-pad LEFT (direction = -1):

Step 1: currentTemp = 52.0
Step 2: newTemperature = ConstrainTemperature(52 - 1 = 51, pointIndex=2)

        Inside ConstrainTemperature:
        - minTemp = max(30, Points[1].Temperature + 2)
        - minTemp = max(30, 50 + 2) = 52
        - maxTemp = min(90, Points[3].Temperature - 2)
        - maxTemp = min(90, 60 - 2) = 58
        - return Clamp(51, 52, 58) = 52  ← CLAMPED BACK!

Step 3: if (Math.Abs(52 - 52) > 0.01)  ← FALSE!
        {
            // This block NEVER executes
            _currentCurve.Points[2].Temperature = 52;
        }

Result: Point doesn't move!
```

### Why Mouse Mode Doesn't Have This Problem

**Mouse dragging code:**
```csharp
// Get mouse position (can be ANY pixel value)
double newTemp = XToTemperature(position.X);  // e.g., 51.7°C

// Apply constraint AFTER getting position
if (_dragPointIndex > 0)
    newTemp = Math.Max(newTemp, Points[_dragPointIndex - 1].Temperature + 2);
if (_dragPointIndex < _dragPointIndex.Length - 1)
    newTemp = Math.Min(newTemp, Points[_dragPointIndex + 1].Temperature - 2);

// NO delta check - directly update
_currentCurve.Points[_dragPointIndex].Temperature = newTemp;
_currentCurve.Points[_dragPointIndex].FanSpeed = newSpeed;
```

**Key differences:**
1. Mouse gets continuous pixel positions (not discrete ±1 increments)
2. Mouse position can be anywhere in the valid range
3. **Mouse has NO delta check** - it always updates
4. Mouse moves smoothly because user can place it anywhere

**Gamepad code:**
```csharp
// Can only increment by ±1
double currentTemp = _currentCurve.Points[index].Temperature;
double newTemperature = ConstrainTemperature(currentTemp + direction, index);  // ±1

// Delta check prevents update when constraint clamps back to current value
if (Math.Abs(newTemperature - currentTemp) > 0.01)  ← BLOCKS BOUNDARY MOVEMENT
{
    _currentCurve.Points[index].Temperature = newTemperature;
    UpdateControlPointPosition(...);
    UpdateCurveLineOnly();
}
```

## The Fix

### Option 1: Remove Delta Check (Recommended)
Match mouse behavior - always update if the method is called.

```csharp
private void AdjustControlPoint(int direction)
{
    if (!_isControlPointActivated || _activeControlPointIndex < 0 || _activeControlPointIndex >= _currentCurve.Points.Length)
        return;

    double currentTemp = _currentCurve.Points[_activeControlPointIndex].Temperature;
    double newTemperature = ConstrainTemperature(currentTemp + direction, _activeControlPointIndex);

    // REMOVE delta check - always update (mouse does this)
    _currentCurve.Points[_activeControlPointIndex].Temperature = newTemperature;
    UpdateControlPointPosition(_activeControlPointIndex, _currentCurve.Points[_activeControlPointIndex]);
    UpdateCurveLineOnly();
}

private void AdjustControlPointVertically(int direction)
{
    if (!_isControlPointActivated || _activeControlPointIndex < 0 || _activeControlPointIndex >= _currentCurve.Points.Length)
        return;

    var currentPoint = _currentCurve.Points[_activeControlPointIndex];
    double newFanSpeed = Math.Clamp(currentPoint.FanSpeed + direction, 0, 100);

    // REMOVE delta check - always update (mouse does this)
    _currentCurve.Points[_activeControlPointIndex].FanSpeed = newFanSpeed;
    UpdateControlPointPosition(_activeControlPointIndex, _currentCurve.Points[_activeControlPointIndex]);
    UpdateCurveLineOnly();
}
```

### Why This Works
- When point is at boundary, constrained value equals current value
- Update still executes (setting same value is harmless)
- Visual update methods still run (UpdateControlPointPosition, UpdateCurveLineOnly)
- Matches mouse behavior exactly (no delta check)
- User gets consistent feedback even at boundaries

### Option 2: Increase Increment Size
Change `direction` from ±1 to ±5 or ±10, giving more "force" to overcome constraints.

**Rejected** because:
- Would make fine-tuning difficult
- Doesn't solve the fundamental boundary issue
- Mouse uses pixel-perfect precision, gamepad should too (within 1-degree steps)

## Additional Observations

### Performance Consideration
The original delta check may have been added to avoid unnecessary updates when value doesn't change. However:
- These are lightweight UI updates (canvas position changes)
- Mouse mode has NO such optimization
- User experience (responsive controls) > micro-optimization
- The check is `> 0.01` which would catch floating-point errors, but with integer increments (±1) there are no floating-point accumulation issues

### Input Repeat Rate
GamepadNavigationService repeats input every 150ms (`INPUT_REPEAT_DELAY_MS`), so even at boundaries where the value doesn't change, updates happen at most ~6-7 times per second. This is not a performance concern.

## Testing Scenarios

After applying fix, test these scenarios:

1. **Boundary Movement:**
   - Move point to minimum gap from previous point (e.g., 52°C when previous is 50°C)
   - Press d-pad LEFT - should NOT move (at constraint)
   - Press d-pad RIGHT - should move freely

2. **Vertical Boundaries:**
   - Move point to 0% fan speed
   - Press d-pad DOWN - should NOT move (at minimum)
   - Press d-pad UP - should move freely
   - Move point to 100% fan speed
   - Press d-pad UP - should NOT move (at maximum)
   - Press d-pad DOWN - should move freely

3. **Middle Point Freedom:**
   - Move middle points with plenty of room between neighbors
   - Should move smoothly in all 4 directions

4. **Comparison with Mouse:**
   - Drag point with mouse to boundary
   - Drag point with gamepad to same boundary
   - Both should behave identically

## Conclusion

The delta check `if (Math.Abs(newValue - currentValue) > 0.01)` was preventing updates when the constrained value equaled the current value (at boundaries). Removing this check aligns gamepad behavior with mouse behavior and allows consistent visual feedback.
