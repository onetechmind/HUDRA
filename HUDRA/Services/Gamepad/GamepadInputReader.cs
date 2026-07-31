using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Windows.Gaming.Input;
using Windows.System;

namespace HUDRA.Services.GamepadInput
{
    /// <summary>
    /// Owns all raw gamepad input: polling, connection tracking, edge detection,
    /// press/repeat timing, stick and trigger hysteresis, and haptics. Emits
    /// semantic <see cref="GamepadEvent"/>s plus the raw per-tick reading for
    /// legacy consumers. Knows nothing about focus, pages, or dialogs.
    ///
    /// All state is touched only on the UI thread: connection callbacks from
    /// Windows.Gaming.Input arrive on background threads and are marshaled via
    /// the DispatcherQueue before mutating the gamepad list (the previous
    /// implementation mutated the list directly from those callbacks while the
    /// poll timer iterated it).
    /// </summary>
    public sealed class GamepadInputReader : IDisposable
    {
        public const double StickPressThreshold = 0.5;
        public const double StickReleaseThreshold = 0.4;
        public const double TriggerPressThreshold = 0.6;
        public const double TriggerReleaseThreshold = 0.4;
        public const double RightStickDeadzone = 0.10;

        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(16);
        private static readonly TimeSpan InitialRepeatDelay = TimeSpan.FromMilliseconds(400);
        private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(110);
        private static readonly TimeSpan KeyboardDedupeWindow = TimeSpan.FromMilliseconds(50);

        /// <summary>Consecutive read failures before a device is evicted (~480ms at 16ms).</summary>
        private const int MaxConsecutiveFailures = 30;

        /// <summary>Upper bound on tracked devices, to bound damage from pathological re-enumeration.</summary>
        private const int MaxDevices = 8;

        private static readonly TimeSpan ReconcileInterval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ActiveDeviceTimeout = TimeSpan.FromSeconds(2);
        private const double ActivityThreshold = 0.2;

        /// <summary>
        /// One tracked controller. Holds its own failure count so a single dead
        /// device cannot stop the others from being read.
        /// </summary>
        private sealed class DeviceSlot
        {
            public required Windows.Gaming.Input.Gamepad Pad { get; init; }
            public required int Id { get; init; }
            public int ConsecutiveFailures { get; set; }
            public TimeSpan LastActivity { get; set; }
            public GamepadReading LastReading { get; set; }
            public bool ReadOkThisTick { get; set; }
        }

        private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;
        private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;
        private readonly List<DeviceSlot> _devices = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly GamepadRepeatTracker _repeatTracker = new(InitialRepeatDelay, RepeatInterval);
        private readonly StuckInputGuard _stuckGuard = new();
        private readonly HashSet<GamepadAction> _rawHeld = new();
        private readonly HashSet<GamepadAction> _heldActions = new();
        private readonly Dictionary<GamepadAction, TimeSpan> _lastKeyboardEmit = new();
        private int _nextDeviceId = 1;
        private DeviceSlot? _activeDevice;
        // Start one interval in the past so the first check runs immediately.
        // NOT TimeSpan.MinValue: "now - MinValue" overflows TimeSpan.
        private TimeSpan _lastReconcile = -ReconcileInterval;

        // Stick-as-dpad and trigger hysteresis state
        private bool _stickUp, _stickDown, _stickLeft, _stickRight;
        private bool _leftTriggerHeld, _rightTriggerHeld;
        private double _lastRightStickY;

        /// <summary>Semantic input events (presses and directional repeats).</summary>
        public event EventHandler<GamepadEvent>? ActionDispatched;

        /// <summary>
        /// Raw combined reading, fired once per poll tick AFTER any semantic
        /// events for that tick. Used for legacy raw forwarding (LibraryPage).
        /// </summary>
        public event EventHandler<GamepadReading>? ReadingAvailable;

        /// <summary>Right-stick analog frames for scroll consumers.</summary>
        public event EventHandler<GamepadStickFrame>? StickFrame;

        public event EventHandler<GamepadConnectionEventArgs>? GamepadConnected;
        public event EventHandler<GamepadConnectionEventArgs>? GamepadDisconnected;

        public bool HasConnectedGamepads => _devices.Count > 0;

        /// <summary>
        /// Optional sink for edge-triggered device/input transitions. A delegate
        /// rather than a logger reference so the reader stays app-type free.
        /// Never called from the poll hot path.
        /// </summary>
        public Action<string>? DiagnosticLog { get; set; }

        /// <summary>True while the poll timer is actually running.</summary>
        public bool IsPollingActive => _timer.IsRunning;

        /// <summary>Snapshot of the currently-held semantic actions (diagnostics).</summary>
        public IReadOnlyCollection<GamepadAction> HeldActionsSnapshot => _heldActions.ToArray();

        /// <summary>Human-readable device + input state for bug reports.</summary>
        public string DescribeDevices()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"devices={_devices.Count} polling={IsPollingActive} " +
                      $"active=#{_activeDevice?.Id.ToString() ?? "none"}");

            foreach (var slot in _devices)
            {
                sb.AppendLine();
                sb.Append($"  #{slot.Id} fails={slot.ConsecutiveFailures} " +
                          $"suspect={_stuckGuard.IsDeviceSuspect(slot.Id)} ");
                try
                {
                    var r = slot.Pad.GetCurrentReading();
                    sb.Append($"buttons={r.Buttons} LT={r.LeftTrigger:F2} RT={r.RightTrigger:F2} " +
                              $"LS=({r.LeftThumbstickX:F2},{r.LeftThumbstickY:F2}) " +
                              $"RS=({r.RightThumbstickX:F2},{r.RightThumbstickY:F2})");
                }
                catch (Exception ex)
                {
                    sb.Append($"READ FAILED: {ex.GetType().Name}: {ex.Message}");
                }
            }

            var held = HeldActionsSnapshot;
            var masked = _stuckGuard.MaskedActions;
            sb.AppendLine();
            sb.Append($"  held=[{(held.Count == 0 ? "none" : string.Join(",", held))}] " +
                      $"masked=[{(masked.Count == 0 ? "none" : string.Join(",", masked))}]");
            return sb.ToString();
        }

        /// <summary>Inputs currently suppressed as implausibly stuck (diagnostics).</summary>
        public IReadOnlyCollection<GamepadAction> MaskedActions => _stuckGuard.MaskedActions;

        public GamepadInputReader()
        {
            _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()
                ?? throw new InvalidOperationException("GamepadInputReader must be created on the UI thread");

            _timer = _dispatcherQueue.CreateTimer();
            _timer.Interval = PollInterval;
            _timer.Tick += OnTick;

            Windows.Gaming.Input.Gamepad.GamepadAdded += OnGamepadAdded;
            Windows.Gaming.Input.Gamepad.GamepadRemoved += OnGamepadRemoved;

            _stuckGuard.MaskChanged += message => DiagnosticLog?.Invoke(message);

            foreach (var gamepad in Windows.Gaming.Input.Gamepad.Gamepads)
            {
                AddGamepad(gamepad);
            }
        }

        /// <summary>True for the VirtualKey.Gamepad* keys WinUI synthesizes from gamepad hardware.</summary>
        public static bool IsGamepadVirtualKey(VirtualKey key) =>
            key >= VirtualKey.GamepadA && key <= VirtualKey.GamepadRightThumbstickLeft;

        /// <summary>
        /// Map a key press (physical keyboard fallback or synthesized Gamepad* key)
        /// to a semantic event. Returns true if the key maps to a gamepad action,
        /// whether or not an event was dispatched (duplicates of recent polled
        /// input are swallowed but still count as handled).
        /// </summary>
        public bool ProcessKeyDown(VirtualKey key)
        {
            if (!TryMapKey(key, out var action)) return false;

            var now = _clock.Elapsed;

            // If polling already delivered this press (action currently held),
            // the synthesized key is a duplicate - swallow it.
            if (_heldActions.Contains(action)) return true;

            _lastKeyboardEmit[action] = now;
            ActionDispatched?.Invoke(this, new GamepadEvent(action, IsRepeat: false, now, GamepadEventSource.Keyboard));
            return true;
        }

        /// <summary>
        /// Short haptic pulse on all connected gamepads. Runs entirely on a
        /// background thread: the Vibration setter can block for a long time on
        /// some (especially Bluetooth) controllers, and on the UI thread that
        /// showed up as random multi-second hitches on page navigation.
        /// Windows.Gaming.Input objects are agile, so off-thread access is safe.
        /// </summary>
        public void PulseHaptics(double intensity = 0.2, int durationMs = 100)
        {
            var gamepads = _devices.Select(slot => slot.Pad).ToArray();
            if (gamepads.Length == 0) return;

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    foreach (var gamepad in gamepads)
                    {
                        gamepad.Vibration = new GamepadVibration
                        {
                            LeftMotor = intensity,
                            RightMotor = intensity
                        };
                    }

                    await System.Threading.Tasks.Task.Delay(durationMs);

                    foreach (var gamepad in gamepads)
                    {
                        gamepad.Vibration = new GamepadVibration();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"🎮 Haptic feedback error: {ex.Message}");
                }
            });
        }

        private void OnGamepadAdded(object? sender, Windows.Gaming.Input.Gamepad gamepad)
        {
            // Fires on a background thread - marshal before touching state
            _dispatcherQueue.TryEnqueue(() => AddGamepad(gamepad));
        }

        private void OnGamepadRemoved(object? sender, Windows.Gaming.Input.Gamepad gamepad)
        {
            _dispatcherQueue.TryEnqueue(() => RemoveGamepad(gamepad));
        }

        private void AddGamepad(Windows.Gaming.Input.Gamepad gamepad)
        {
            if (FindSlot(gamepad) != null) return;
            if (_devices.Count >= MaxDevices)
            {
                DiagnosticLog?.Invoke($"device add ignored: already tracking {_devices.Count}");
                return;
            }

            var slot = new DeviceSlot { Pad = gamepad, Id = _nextDeviceId++, LastActivity = _clock.Elapsed };
            _devices.Add(slot);
            Debug.WriteLine($"🎮 Gamepad connected ({_devices.Count} total)");
            DiagnosticLog?.Invoke($"device #{slot.Id} added (now {_devices.Count})");
            GamepadConnected?.Invoke(this, new GamepadConnectionEventArgs(gamepad));

            // Held state from before the topology change is no longer meaningful.
            ResetInputState();
            EnsureTimerState();
        }

        private void RemoveGamepad(Windows.Gaming.Input.Gamepad gamepad)
        {
            var slot = FindSlot(gamepad);
            if (slot == null)
            {
                // The removal notification did not match any tracked wrapper.
                // Windows.Gaming.Input can hand out a different wrapper for the
                // same physical device, so trust the platform list instead of
                // silently doing nothing (which used to leave a dead device in
                // place, latching input forever).
                DiagnosticLog?.Invoke("device removal did not match a tracked device - reconciling");
                ReconcileDevices(force: true);
                return;
            }

            EvictSlot(slot, "disconnected");
        }

        private void EvictSlot(DeviceSlot slot, string reason)
        {
            if (!_devices.Remove(slot)) return;

            if (ReferenceEquals(_activeDevice, slot)) _activeDevice = null;
            _stuckGuard.ForgetDevice(slot.Id);

            Debug.WriteLine($"🎮 Gamepad removed ({_devices.Count} remaining)");
            DiagnosticLog?.Invoke($"device #{slot.Id} removed ({reason}, now {_devices.Count})");
            GamepadDisconnected?.Invoke(this, new GamepadConnectionEventArgs(slot.Pad));

            ResetInputState();
            EnsureTimerState();
        }

        private DeviceSlot? FindSlot(Windows.Gaming.Input.Gamepad pad)
        {
            foreach (var slot in _devices)
            {
                if (ReferenceEquals(slot.Pad, pad)) return slot;
            }
            return null;
        }

        /// <summary>
        /// Start or stop the poll timer to match the tracked device set. Called on
        /// every topology change so the timer can never be left stopped while a
        /// controller is connected (or running with none).
        /// </summary>
        private void EnsureTimerState()
        {
            if (_devices.Count > 0 && !_timer.IsRunning) _timer.Start();
            else if (_devices.Count == 0 && _timer.IsRunning) _timer.Stop();
        }

        /// <summary>
        /// Re-sync the tracked devices against the platform's own list. This is the
        /// recovery path for ghost devices: entries the platform no longer reports
        /// are evicted, and newly present ones are added. Both sides come from
        /// Windows.Gaming.Input, so wrapper identity matches within a session.
        /// </summary>
        public void ReconcileDevices(bool force = false)
        {
            var now = _clock.Elapsed;
            if (!force && now - _lastReconcile < ReconcileInterval) return;
            _lastReconcile = now;

            IReadOnlyList<Windows.Gaming.Input.Gamepad> current;
            try
            {
                current = Windows.Gaming.Input.Gamepad.Gamepads;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"🎮 Device reconcile failed: {ex.Message}");
                return;
            }

            for (int i = _devices.Count - 1; i >= 0; i--)
            {
                var slot = _devices[i];
                bool stillPresent = false;
                foreach (var pad in current)
                {
                    if (ReferenceEquals(pad, slot.Pad)) { stillPresent = true; break; }
                }
                if (!stillPresent) EvictSlot(slot, "absent from platform list");
            }

            foreach (var pad in current)
            {
                if (FindSlot(pad) == null) AddGamepad(pad);
            }

            EnsureTimerState();
        }

        /// <summary>Force an immediate device re-sync (window shown, resume from sleep).</summary>
        public void ReconcileDevicesNow() => ReconcileDevices(force: true);

        /// <summary>
        /// Forget all transient input state. Safe to call at any time; used on
        /// device changes, window hide and system suspend so that a press held
        /// across the gap cannot be mistaken for a fresh one (or latch forever).
        /// </summary>
        public void ResetInputState()
        {
            _repeatTracker.Reset();
            _stuckGuard.Reset();
            _activeDevice = null;
            _rawHeld.Clear();
            _heldActions.Clear();
            _stickUp = _stickDown = _stickLeft = _stickRight = false;
            _leftTriggerHeld = _rightTriggerHeld = false;
            _lastRightStickY = 0;
        }

        private void OnTick(object? sender, object e)
        {
            var now = _clock.Elapsed;

            // Periodic self-check so a ghost device cannot persist indefinitely.
            ReconcileDevices();

            if (_devices.Count == 0) return;

            // Read every device independently. A failing device must never be able
            // to abort the tick: doing so previously skipped the whole state machine,
            // so no release edges were produced and the held set froze permanently.
            var reading = ReadDevices(now);

            BuildRawHeld(reading);

            // Mask implausibly-stuck inputs on the RAW set, before reducing to a
            // single direction - otherwise a stuck direction keeps occupying the
            // directional slot and masks the ones the user is actually pressing.
            var deviceId = _activeDevice?.Id ?? 0;
            var usable = _stuckGuard.Filter(deviceId, _rawHeld, now);

            DirectionReducer.Reduce(usable, _heldActions);

            foreach (var ev in _repeatTracker.Update(_heldActions, now))
            {
                // A synthesized Gamepad* key may have already delivered this press
                if (!ev.IsRepeat &&
                    _lastKeyboardEmit.TryGetValue(ev.Action, out var keyTime) &&
                    now - keyTime < KeyboardDedupeWindow)
                {
                    continue;
                }

                ActionDispatched?.Invoke(this, ev);
            }

            double rightY = reading.RightThumbstickY;
            if (Math.Abs(rightY) > RightStickDeadzone || Math.Abs(_lastRightStickY) > RightStickDeadzone)
            {
                StickFrame?.Invoke(this, new GamepadStickFrame(rightY));
            }
            _lastRightStickY = rightY;

            ReadingAvailable?.Invoke(this, reading);
        }

        /// <summary>
        /// Read all devices (each isolated from the others' failures) and return the
        /// reading of the most recently active one.
        ///
        /// Deliberately NOT a merge of all devices. Merging (OR'ing buttons, taking
        /// the largest axis) meant one controller with a latched button or drifting
        /// stick overrode every other device forever - the opposite of the intent.
        /// "Most recently active wins" achieves the original goal (an idle second pad
        /// cannot interfere) while letting a stuck one be ignored, and hands over
        /// instantly when the user picks up a different controller.
        /// </summary>
        private GamepadReading ReadDevices(TimeSpan now)
        {
            for (int i = _devices.Count - 1; i >= 0; i--)
            {
                var slot = _devices[i];
                slot.ReadOkThisTick = false;

                try
                {
                    slot.LastReading = slot.Pad.GetCurrentReading();
                    slot.ConsecutiveFailures = 0;
                    slot.ReadOkThisTick = true;
                }
                catch (Exception ex)
                {
                    if (++slot.ConsecutiveFailures >= MaxConsecutiveFailures)
                    {
                        DiagnosticLog?.Invoke($"device #{slot.Id} evicted after " +
                                              $"{slot.ConsecutiveFailures} read failures ({ex.GetType().Name})");
                        EvictSlot(slot, "read failures");
                    }
                    continue;
                }

                if (IsActive(slot.LastReading) && !_stuckGuard.IsDeviceSuspect(slot.Id))
                {
                    slot.LastActivity = now;
                    if (!ReferenceEquals(_activeDevice, slot))
                    {
                        _activeDevice = slot;
                        DiagnosticLog?.Invoke($"active device is now #{slot.Id}");
                    }
                }
            }

            // Release the active slot once it has been quiet for a while, so another
            // controller can take over immediately when it is used.
            if (_activeDevice != null &&
                (!_devices.Contains(_activeDevice) || now - _activeDevice.LastActivity > ActiveDeviceTimeout))
            {
                _activeDevice = null;
            }

            _activeDevice ??= _devices.FirstOrDefault(slot => slot.ReadOkThisTick);

            return _activeDevice is { ReadOkThisTick: true } ? _activeDevice.LastReading : default;
        }

        /// <summary>Any input at all, used to decide which device the user is holding.</summary>
        private static bool IsActive(in GamepadReading reading) =>
            reading.Buttons != GamepadButtons.None ||
            reading.LeftTrigger > ActivityThreshold ||
            reading.RightTrigger > ActivityThreshold ||
            Math.Abs(reading.LeftThumbstickX) > ActivityThreshold ||
            Math.Abs(reading.LeftThumbstickY) > ActivityThreshold ||
            Math.Abs(reading.RightThumbstickX) > ActivityThreshold ||
            Math.Abs(reading.RightThumbstickY) > ActivityThreshold;

        /// <summary>
        /// Map a reading to the full set of held semantic actions, including every
        /// direction that is held. Reduction to a single direction happens later, so
        /// that stuck-input masking can act on the complete picture.
        /// </summary>
        private void BuildRawHeld(in GamepadReading reading)
        {
            _rawHeld.Clear();
            var buttons = reading.Buttons;

            // Stick-as-dpad with per-direction hysteresis
            _stickUp = ApplyHysteresis(_stickUp, reading.LeftThumbstickY);
            _stickDown = ApplyHysteresis(_stickDown, -reading.LeftThumbstickY);
            _stickLeft = ApplyHysteresis(_stickLeft, -reading.LeftThumbstickX);
            _stickRight = ApplyHysteresis(_stickRight, reading.LeftThumbstickX);

            if (buttons.HasFlag(GamepadButtons.DPadUp) || _stickUp) _rawHeld.Add(GamepadAction.NavUp);
            if (buttons.HasFlag(GamepadButtons.DPadDown) || _stickDown) _rawHeld.Add(GamepadAction.NavDown);
            if (buttons.HasFlag(GamepadButtons.DPadLeft) || _stickLeft) _rawHeld.Add(GamepadAction.NavLeft);
            if (buttons.HasFlag(GamepadButtons.DPadRight) || _stickRight) _rawHeld.Add(GamepadAction.NavRight);

            if (buttons.HasFlag(GamepadButtons.A)) _rawHeld.Add(GamepadAction.Accept);
            if (buttons.HasFlag(GamepadButtons.B)) _rawHeld.Add(GamepadAction.Back);
            if (buttons.HasFlag(GamepadButtons.X)) _rawHeld.Add(GamepadAction.X);
            if (buttons.HasFlag(GamepadButtons.Y)) _rawHeld.Add(GamepadAction.Y);
            if (buttons.HasFlag(GamepadButtons.LeftShoulder)) _rawHeld.Add(GamepadAction.LB);
            if (buttons.HasFlag(GamepadButtons.RightShoulder)) _rawHeld.Add(GamepadAction.RB);

            _leftTriggerHeld = ApplyTriggerHysteresis(_leftTriggerHeld, reading.LeftTrigger);
            _rightTriggerHeld = ApplyTriggerHysteresis(_rightTriggerHeld, reading.RightTrigger);
            if (_leftTriggerHeld) _rawHeld.Add(GamepadAction.LT);
            if (_rightTriggerHeld) _rawHeld.Add(GamepadAction.RT);
        }

        private static bool ApplyHysteresis(bool wasHeld, double value) =>
            wasHeld ? value > StickReleaseThreshold : value > StickPressThreshold;

        private static bool ApplyTriggerHysteresis(bool wasHeld, double value) =>
            wasHeld ? value > TriggerReleaseThreshold : value > TriggerPressThreshold;

        private static bool TryMapKey(VirtualKey key, out GamepadAction action)
        {
            switch (key)
            {
                case VirtualKey.Up:
                case VirtualKey.W:
                case VirtualKey.GamepadDPadUp:
                case VirtualKey.GamepadLeftThumbstickUp:
                    action = GamepadAction.NavUp; return true;
                case VirtualKey.Down:
                case VirtualKey.S:
                case VirtualKey.GamepadDPadDown:
                case VirtualKey.GamepadLeftThumbstickDown:
                    action = GamepadAction.NavDown; return true;
                case VirtualKey.Left:
                case VirtualKey.A:
                case VirtualKey.GamepadDPadLeft:
                case VirtualKey.GamepadLeftThumbstickLeft:
                    action = GamepadAction.NavLeft; return true;
                case VirtualKey.Right:
                case VirtualKey.D:
                case VirtualKey.GamepadDPadRight:
                case VirtualKey.GamepadLeftThumbstickRight:
                    action = GamepadAction.NavRight; return true;
                case VirtualKey.Enter:
                case VirtualKey.Space:
                case VirtualKey.GamepadA:
                    action = GamepadAction.Accept; return true;
                case VirtualKey.Escape:
                case VirtualKey.GamepadB:
                    action = GamepadAction.Back; return true;
                case VirtualKey.GamepadX:
                    action = GamepadAction.X; return true;
                case VirtualKey.GamepadY:
                    action = GamepadAction.Y; return true;
                case VirtualKey.GamepadLeftShoulder:
                    action = GamepadAction.LB; return true;
                case VirtualKey.GamepadRightShoulder:
                    action = GamepadAction.RB; return true;
                case VirtualKey.GamepadLeftTrigger:
                    action = GamepadAction.LT; return true;
                case VirtualKey.GamepadRightTrigger:
                    action = GamepadAction.RT; return true;
                default:
                    action = default; return false;
            }
        }

        public void Dispose()
        {
            _timer.Stop();
            Windows.Gaming.Input.Gamepad.GamepadAdded -= OnGamepadAdded;
            Windows.Gaming.Input.Gamepad.GamepadRemoved -= OnGamepadRemoved;
            _devices.Clear();
        }
    }
}
