# Gamepad Input Architecture

How HUDRA turns controller input into UI behavior, after the layered rewrite
(replacing the former monolithic `GamepadNavigationService` flag machine).

## Layers

```
hardware ──► GamepadInputReader ──► InputRouter (scope stack) ──► focus / actions
                (Services/Gamepad)        (Services/Gamepad)
```

### 1. `GamepadInputReader` (`Services/Gamepad/GamepadInputReader.cs`)

Owns everything raw: 16 ms polling of XInput, connection tracking, edge
detection, key repeat, hysteresis, haptics. Emits semantic `GamepadEvent`s
(`NavUp/NavDown/NavLeft/NavRight, Accept, Back, X, Y, LB, RB, LT, RT`):

- **Backend: XInput** (`Services/Gamepad/XInputNative.cs`, P/Invoke into
  `xinput1_4.dll`), not the WinRT gaming-input API. That API routes readings to
  the *foreground* process, which is fatal for an always-on-top overlay: the pad
  was withdrawn from HUDRA for 13 s when a clicked link brought the browser
  forward, and in another incident reads kept succeeding for 32 s while
  returning nothing but neutral values. XInput is not foreground-gated.
- **Four fixed slots** — the XInput user index (0-3) *is* the device identity, so
  slots are created once and only flip connected/disconnected. The reader's
  device id is `user index + 1`, keeping index 0 clear of the "no device"
  sentinel `StuckInputGuard` is keyed with. Raw bytes/shorts are normalized at
  the boundary (triggers `/255`, sticks `/32767` clamped to ±1), so every
  threshold below is unchanged.
- **Discovery is the poll loop.** XInput has no hotplug events, so the always-on
  16 ms timer *is* device discovery: connected slots are read every tick, empty
  user indexes are probed every 2 s (probing an empty index is comparatively
  slow, so it is not done per frame).
- **Repeat**: directional actions only — 400 ms initial delay, then every
  110 ms (`GamepadRepeatTracker`, pure & unit-tested). Action buttons never
  repeat.
- **Left stick** acts as d-pad (press > 0.5, release < 0.4 hysteresis); at
  most one direction per tick (dominant wins) so diagonals can't double-step.
- **Triggers** are digital LT/RT with 0.6/0.4 hysteresis.
- **Right stick** is forwarded as analog `GamepadStickFrame`s for scrolling.
- **Synthesized keys**: WinUI synthesizes `VirtualKey.Gamepad*` key events
  from controllers. The reader maps/dedupes them against polled input, and
  `MainWindow.OnNonGamepadInput` ignores them so a controller can never
  deactivate its own gamepad mode.
- **Key echoes** (`IsKeyEchoOfController`): mapper software (OneXConsole,
  Steam Input desktop configs) translates controller input into *plain*
  keyboard events — a field log showed every d-pad press arriving as an arrow
  KeyDown ~15 ms *before* the poll saw the button, deactivating gamepad mode
  and getting the polled press consumed as the wake press (flicker instead of
  navigation). A plain key (arrow/WASD/Enter/Space/Escape) that mirrors what a
  connected controller is physically pressing right now — per the held sets or
  a fresh `XInputGetState`, since the injected key beats the poll — is
  swallowed by `MainWindow.OnPreviewKeyDown` (tunneling), so it never reaches
  `OnNonGamepadInput` or WinUI's own key handling. Keys from a real keyboard
  while the controller is idle match nothing and keep their normal meaning.
- **Multiple controllers**: most-recently-active device wins (2 s handover).
  Readings are NOT merged — OR'ing buttons across devices meant one controller
  with a latched button or drifting stick overrode every other device forever.
- **Fault isolation**: each slot carries its own result code and failure count.
  `ERROR_DEVICE_NOT_CONNECTED` disconnects that slot immediately; any other error
  is treated as possibly transient and evicts only after 30 consecutive failures.
  The tick's state machine always advances, so release edges are guaranteed even
  when every read fails.
- **Ghost tolerance** (`StuckInputGuard`): masks a non-repeatable action held
  ≥ 8 s, a direction held ≥ 20 s, and everything from a device that has never
  reported neutral within 3 s. Masking is applied to the raw held set *before*
  `DirectionReducer` collapses it to one direction — masking afterwards would
  leave a stuck direction occupying the slot. Masks clear on release/neutral.
- **Device reconciliation**: the 2 s probe of all four user indexes, also forced
  on window show and on resume from sleep, so a re-enumerated controller cannot
  leave a ghost.

### 2. `InputRouter` + scopes (`Services/Gamepad/InputRouter.cs`, `Scopes/`)

An explicit stack; each event is offered to the top scope first and falls
through until consumed. **All modal behavior is a scope** — there are no
boolean mode flags.

Two invariants keep a leaked scope from wedging input:

- **Layered insertion.** Every scope declares a `Layer` (`ScopeLayer`:
  Shell 0, Page 10, PageCustom 20, Edit 30, PageModal 40, Modal 50) and
  `Push` inserts at the top of that band. A scope arriving late — e.g. a page
  scope from an async init continuation — can never outrank an open modal.
  Removal is non-cascading (`Remove`/`RemoveWhere`); the old cascading `Pop`
  is what let a transient scope's removal silently delete a page scope.
- **Reaping.** Each scope declares `IsStillValid`; `Reap()` runs before every
  dispatch (throttled to 100 ms for 60 Hz stick frames) and drops scopes whose
  context is gone, so a missed removal self-heals on the next press instead of
  wedging until restart. Shell and Page are never reapable. A scope not yet
  observed valid gets a 750 ms arming grace (a dialog is pushed before its
  template exists), and a throwing predicate counts as valid.

| Scope | Pushed | Handles | Falls through |
|---|---|---|---|
| `ShellScope` | always (bottom) | LB/RB page cycling, LT/RT navbar selection, A/B on a selection | everything else |
| `PageScope` | always (above shell) | directional focus moves, A activate, B back/collapse, right-stick scroll | chrome (LB/RB/LT/RT) |
| `ValueEditScope` | A on a slider | left/right adjust, A/B exit | chrome |
| `DropdownScope` | A on a ComboBox | up/down browse, A commit, B cancel-and-restore | chrome |
| `DialogScope` | `GamepadDialog.ShowAsync` | d-pad between dialog buttons, A invokes focused, B cancels | nothing (modal) |
| LibraryPage (`IInputScope`) | while Library is active (`SetPageInputScope`) | grid/button-zone navigation, A launch, X game settings, stick scroll | chrome; A/B with a navbar selection |
| Roulette scope | while the roulette overlay is open | A spin/stop, B cancel | nothing (modal) |

Validity per scope: dialog → not closed and still has a `Popup` ancestor;
dropdown → `IsDropDownOpen` (plus a `DropDownClosed` subscription, so a
mouse/Escape dismissal removes it); value edit → edit target still loaded;
page-owned → wrapped in `PageOwnedScope`, valid only while its page is
`Frame.Content`; roulette → its overlay is visible.

Pages with bespoke navigation implement `IInputScope` themselves and are
installed with `SetPageInputScope(inner, owningPage)`, which wraps them in a
`PageOwnedScope` so the scope cannot outlive its page; page-modals use
`PushScope`/`PopScope`.

Invariants the stack gives you for free:

- Chrome (page cycling, navbar) works in every mode because unconsumed events
  reach the shell.
- Page changes drop all page-scoped scopes via `BeginPageTransition()`, so
  slider/dropdown/page state can never leak across pages. Modal scopes survive,
  since a dialog can outlive the page that opened it.
- A scope whose context dies is reaped on the next press, so a missed removal
  costs one press instead of requiring an app restart.
- **Dialog safety net**: if a `ContentDialog` is open with no `DialogScope`
  (someone bypassed `GamepadDialog.ShowAsync`), the service pushes one — but
  rate-limited to 250 ms and one-shot per dialog instance, with removal handled
  by reaping. It previously ran on every press and could push a scope for an
  already-closed dialog, which blocked all input permanently. Always show
  dialogs via `dialog.ShowWithGamepadSupportAsync(service)` anyway — it also
  serializes dialogs (WinUI throws on two at once).

### 3. Focus: spatial navigation + one shared ring

- **Candidates** are elements marked `ap:GamepadNavigation.IsEnabled="True"`
  (attached property, `AttachedProperties/GamepadNavigation.cs`). Collapsed or
  `IsEnabled=false` elements drop out automatically.
- **Movement** is geometric (`SpatialScorer`, pure & unit-tested): candidates
  strictly in the pressed direction, scored by primary-axis distance with a
  4× cross-axis penalty when projections don't overlap. Column layouts align
  naturally — no hardcoded routing. Bounds are recomputed on every press, so
  nothing goes stale across page recreation or scrolling. Focus stops at page
  edges (no wrap). Legacy linear traversal remains behind
  `HudraSettings.UseSpatialNavigation` as an escape hatch.
- **Activation** (`ControlAdapters`): standard controls work with zero
  code-behind — Button/ToggleButton invoke, ToggleSwitch/CheckBox/RadioButton
  toggle, Slider pushes `ValueEditScope` (steps by `SmallChange`), ComboBox
  pushes `DropdownScope`. An `IGamepadElementHost` ancestor gets first chance
  for custom semantics (see `ResolutionPickerControl`'s deferred-commit
  ComboBoxes).
- **Visuals** (`Controls/FocusIndicatorLayer.cs`): one ring drawn above all
  content — DarkViolet focused, DodgerBlue while value-editing — repositioned
  via `EffectiveViewportChanged`/`SizeChanged`. No per-control brush plumbing.

## Adding gamepad support to new UI

Mark the interactive element:

```xaml
<Slider ap:GamepadNavigation.IsEnabled="True" SmallChange="5" ... />
```

That's it for standard controls. For custom semantics implement one of the
small capability interfaces (`Services/Gamepad/Capabilities.cs`):
`IGamepadElementHost` (own activation of inner elements),
`IGamepadValueEditable` (custom value editing), `IDropdownOwner`
(deferred-commit ComboBox), `IGamepadBackHandler` (page-level B).

## Migrating a remaining legacy control

`IGamepadNavigable` (the old fat interface) still backs the not-yet-migrated
controls (TdpPicker, FanCurve, NavigableExpander, GameSettingsPage, …); they
work through the router but self-render focus visuals. To migrate one, follow
the Phase 3c pattern (`AudioControlsControl` is the simplest example):

1. Move `ap:GamepadNavigation.IsEnabled` from the control root onto each
   interactive inner element.
2. Delete the `IGamepadNavigable` implementation, the internal focus-index
   bookkeeping, and every `FocusBorderBrush`-style property/binding (the
   shared ring takes over automatically once the candidate is not an
   `IGamepadNavigable`).
3. If an inner element needs non-generic activation, implement
   `IGamepadElementHost` on the control (see `ResolutionPickerControl`).

## Gamepad mode (activation / deactivation)

Gamepad mode (`IsGamepadActive`) gates the focus ring and all gamepad-driven
focus. Its transitions are field-hardened:

- **Activation** happens on the first semantic event while inactive, and is
  never conditional on which scopes are installed (a stranded page scope used
  to permanently disable the ring). The **wake press is consumed** — it just
  summons the ring — with two exceptions: chrome inputs (LB/RB/LT/RT) act even
  on the wake press, and when auto-focus is suppressed (last navigation was
  mouse/touch) the press is **dispatched instead of eaten**, so a directional
  press establishes focus via the spatial no-current-focus fallback on that
  same press. Eating it used to make the first press after any mouse
  interaction feel dead.
- **Deactivation** is driven by `MainWindow.OnNonGamepadInput`
  (pointer/tap/plain-keyboard input) plus hide and last-controller-disconnect.
  Controller input can never deactivate its own mode: synthesized `Gamepad*`
  keys are filtered, and mapper-injected key echoes are swallowed before the
  handler sees them (see **Key echoes** above).
- **Keyboard-sourced semantic events** (`GamepadEventSource.Keyboard`) only
  participate while mode is already active; they never activate it.
- Input while the window is hidden is blocked by HUDRA's own `IsVisible` gate
  on reader events — product intent, enforced by us rather than accidentally
  by the OS (XInput keeps reading regardless of visibility).

## B-button semantics (one level at a time)

Dialog → dropdown/slider edit → expander collapse → navbar selection →
page-level `IGamepadBackHandler` (e.g. GameSettingsPage back to Library) →
no-op. Every layer is tried in that order; nothing else is hardcoded.

**LB/RB on a detail page.** The game settings page is not a peer in the LB/RB
page cycle (it requires a selected game), so shoulder buttons there leave the
page via the same `IGamepadBackHandler`. They must never be silently inert:
that state — shoulder press does nothing while the controller still buzzes —
is indistinguishable from "LB/RB stopped working".

## Focus memory

Returning to a page restores focus to the element the user last focused
there (remembered by candidate index per page type, since pages are
recreated on each navigation). The Library additionally remembers its
focused game tile and scroll offset.

## Recovery and diagnostics

- `Settings → Copy Debug Info` includes a `=== Gamepad Input ===` section:
  active state, page scope, focused element + liveness, the router stack with
  layers and staleness, and per-device readings/faults/masks.
- `Settings → Reset Gamepad Input` (`HardResetInput()`) drops every scope above
  the page scope, clears focus, resets and re-syncs the reader, and re-inits the
  page. Mouse/touch reachable on purpose — it must work when the pad does not.
- Edge-triggered `DebugLogger` entries under the `GPAD` category record scope
  push/remove/reap, device add/remove/evict, stuck-mask changes, navigation
  generation changes, dropped stale init callbacks, gamepad mode activation
  (with trigger action and suppress/dialog/chrome disposition), deactivation
  with the exact offending input named (key code / pointer type — this is what
  identified the mapper key-echo bug), and swallowed key echoes (throttled to
  one line per 2 s against key repeat). Never called from the poll path (it
  appends synchronously under a lock).

## Window hide/show

`WindowManagementService` raises `WindowHidden` as well as `WindowShown`, and
`ToggleVisibility` is debounced (250 ms) against multi-fire from any source (a
held hotkey, tray double-click). `GamepadNavigationService.OnWindowHidden()` is
the single teardown point — the hotkey and tray paths previously skipped the
cleanup the navbar buttons did — and it clears `_suppressAutoFocusOnActivation`,
which otherwise survived a hide/show and silently swallowed the first press.
`OnWindowShownFromHidden()` re-syncs controllers but deliberately sets no focus:
the ring should appear only once the user presses something. The Low-priority
`LayoutRoot.Focus` assist on show now yields if gamepad mode is already active,
so it cannot steal focus the user just established.

Foreground is **UX-only** since the XInput swap: the show path still calls
`ForceForegroundWindow` (AttachThreadInput with try/finally detach) so the
keyboard works in the overlay, but nothing about gamepad input depends on
being foreground, and the old tap-time foreground assertion — which stole
foreground from the game on every tap — is gone.

## Page transitions

`MainWindow.OnPageChanged` calls `BeginPageTransition()`, which bumps a
navigation generation and tears down all page-scoped input state in one place.
Deferred init callbacks go through `EnqueueForPage(generation, …)` and are
dropped if superseded — page init spans dispatcher callbacks and, for the
library, a multi-second await, so without this a continuation could apply its
focus and input scope to the wrong page. Navigation origin travels as data
(`PageChangedEventArgs.FromGamepad`) rather than a mutable flag that could leak
when a navigation was rejected.

## Remaining transitional pieces (planned follow-ups)

- The remaining `IGamepadNavigable` controls (TdpPickerControl,
  FanCurveControl, NavigableExpander and the expander-body controls,
  GameProfileControl, etc.) still use the legacy interface through the
  router's compatibility path with self-rendered focus visuals. They migrate
  per the recipe above; FanCurve's control-point editor would become its own
  scope. `Interfaces/IGamepadNavigable.cs` and
  `Helpers/GamepadComboBoxHelper.cs` are deleted once the last consumers are
  migrated.
- Library scroll/focus state still lives in static fields on the page; a
  page-state cache service would be cleaner.
- `PulseHaptics` starts an unbounded `Task.Run` per press, and overlapping
  pulses cancel each other (pulse N's zero-write lands during pulse N+1), so
  under button-mashing the rumble stops even though navigation still works.
  Coalescing it behind a single in-flight flag is a one-line fix, deferred.
- `TdpPickerControl.CanNavigateLeft/Right` return true unconditionally. That is
  no longer able to wedge navigation (a detached focus target is dropped before
  dispatch), but making them honest at the ends would still be an improvement.

## Testing

`HUDRA.Tests` (plain net8.0, runs on any OS) links the WinUI-free core
sources directly: repeat timing (`GamepadRepeatTracker`), dispatch/stack
semantics (`InputRouter`), directional scoring incl. the two-column
fixtures (`SpatialScorer`), and grid index math (`GridMath`). CI builds the
WinUI app on `windows-latest` and runs the tests on `ubuntu-latest`
(`.github/workflows/build.yml`).
