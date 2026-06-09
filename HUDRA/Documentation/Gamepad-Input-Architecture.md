# Gamepad Input Architecture

How HUDRA turns controller input into UI behavior, after the layered rewrite
(replacing the former monolithic `GamepadNavigationService` flag machine).

## Layers

```
hardware ──► GamepadInputReader ──► InputRouter (scope stack) ──► focus / actions
                (Services/Gamepad)        (Services/Gamepad)
```

### 1. `GamepadInputReader` (`Services/Gamepad/GamepadInputReader.cs`)

Owns everything raw: 16 ms polling, connection tracking (connection callbacks
are marshaled to the UI thread — they arrive on background threads), edge
detection, key repeat, hysteresis, haptics. Emits semantic `GamepadEvent`s
(`NavUp/NavDown/NavLeft/NavRight, Accept, Back, X, Y, LB, RB, LT, RT`):

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
- Multiple controllers are combined per tick (buttons OR'd, largest axis).

### 2. `InputRouter` + scopes (`Services/Gamepad/InputRouter.cs`, `Scopes/`)

An explicit stack; each event is offered to the top scope first and falls
through until consumed. **All modal behavior is a scope** — there are no
boolean mode flags.

| Scope | Pushed | Handles | Falls through |
|---|---|---|---|
| `ShellScope` | always (bottom) | LB/RB page cycling, LT/RT navbar selection, A/B on a selection | everything else |
| `PageScope` | always (above shell) | directional focus moves, A activate, B back/collapse, right-stick scroll | chrome (LB/RB/LT/RT) |
| `ValueEditScope` | A on a slider | left/right adjust, A/B exit | chrome |
| `DropdownScope` | A on a ComboBox | up/down browse, A commit, B cancel-and-restore | chrome |
| `DialogScope` | `GamepadDialog.ShowAsync` | d-pad between dialog buttons, A invokes focused, B cancels | nothing (modal) |
| `LegacyRawScope` | LibraryPage (transitional) | black-holes what the page reads raw | chrome |

Invariants the stack gives you for free:

- Chrome (page cycling, navbar) works in every mode because unconsumed events
  reach the shell.
- Page changes call `PopWhile(transient)`, so slider/dropdown edit state can
  never leak across pages.
- **Dialog safety net**: if a `ContentDialog` is open with no `DialogScope`
  (someone bypassed `GamepadDialog.ShowAsync`), the service detects the open
  popup and pushes a scope automatically, popping it on `Closed`. Always show
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

## Remaining transitional pieces (planned follow-ups)

- **LibraryPage** still consumes raw readings via `RawGamepadInput` behind
  `LegacyRawScope`. Plan: replace with a `LibraryGridScope` built on
  `GridMath` (X = game settings, A = launch, right-stick scroll, roulette as
  its own scope), move its static scroll/focus fields into a page-state
  cache, then delete `RawGamepadInput`/`PauseInputProcessing`/`LegacyRawScope`.
- **GameSettingsPage** and the remaining `IGamepadNavigable` controls migrate
  per the recipe above; the SGDB image grid becomes a `GridMath`-based scope.
- **Per-page focus memory** (restore last-focused element when returning to a
  page) via a `PageStateCache` keyed by page type.
- `Helpers/GamepadComboBoxHelper.cs` and `Interfaces/IGamepadNavigable.cs`
  are deleted once the last consumers are migrated.

## Testing

`HUDRA.Tests` (plain net8.0, runs on any OS) links the WinUI-free core
sources directly: repeat timing (`GamepadRepeatTracker`), dispatch/stack
semantics (`InputRouter`), directional scoring incl. the two-column
fixtures (`SpatialScorer`), and grid index math (`GridMath`). CI builds the
WinUI app on `windows-latest` and runs the tests on `ubuntu-latest`
(`.github/workflows/build.yml`).
