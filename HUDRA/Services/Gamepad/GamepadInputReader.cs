using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Windows.System;

namespace HUDRA.Services.GamepadInput
{
    /// <summary>
    /// Owns all raw gamepad input: polling, connection tracking, edge detection,
    /// press/repeat timing, stick and trigger hysteresis, and haptics. Emits
    /// semantic <see cref="GamepadEvent"/>s. Knows nothing about focus, pages or dialogs.
    ///
    /// Readings come from XInput (<see cref="XInputNative"/>), not the WinRT
    /// gaming-input API (WGI). WGI routes readings to the FOREGROUND process,
    /// which is fatal for an always-on-top overlay: field logs show the pad
    /// withdrawn from this process for 13 seconds after a clicked link brought
    /// the browser forward, and a separate 32-second stretch where reads kept
    /// succeeding while returning nothing but neutral values. XInput is not
    /// foreground-gated, so the overlay keeps reading while a game owns the
    /// screen.
    ///
    /// A consequence of the swap: there are no connection callbacks at all any
    /// more, so every field here is touched only on the UI thread, from the poll
    /// timer. The DispatcherQueue is still needed to create that timer.
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

        private static readonly TimeSpan ReconcileInterval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan ActiveDeviceTimeout = TimeSpan.FromSeconds(2);
        private const double ActivityThreshold = 0.2;

        /// <summary>
        /// One normalized controller snapshot. XInput reports raw bytes and shorts;
        /// normalizing here, at the boundary, is what keeps every threshold
        /// downstream (StickPressThreshold and friends) expressed in the same
        /// 0..1 / -1..1 units it has always used.
        /// </summary>
        private readonly struct PadReading
        {
            public ushort Buttons { get; init; }
            public double LeftTrigger { get; init; }
            public double RightTrigger { get; init; }
            public double LeftThumbstickX { get; init; }
            public double LeftThumbstickY { get; init; }
            public double RightThumbstickX { get; init; }
            public double RightThumbstickY { get; init; }

            public static PadReading From(in XInputNative.XINPUT_GAMEPAD pad) => new()
            {
                Buttons = pad.wButtons,
                LeftTrigger = pad.bLeftTrigger / 255.0,
                RightTrigger = pad.bRightTrigger / 255.0,
                LeftThumbstickX = Axis(pad.sThumbLX),
                LeftThumbstickY = Axis(pad.sThumbLY),
                RightThumbstickX = Axis(pad.sThumbRX),
                RightThumbstickY = Axis(pad.sThumbRY)
            };

            // Clamped because the short range is asymmetric: -32768 / 32767 lands
            // just past -1, and an out-of-range magnitude would defeat hysteresis.
            private static double Axis(short raw) => Math.Clamp(raw / 32767.0, -1.0, 1.0);
        }

        /// <summary>
        /// One XInput user index. The slot object persists across disconnects -
        /// XInput's identity IS the index, so there is nothing to allocate or free
        /// on hotplug. Holds its own failure count so one misbehaving controller
        /// cannot stop the others from being read.
        /// </summary>
        private sealed class DeviceSlot
        {
            public required uint UserIndex { get; init; }

            /// <summary>
            /// User index + 1. The pipeline passes <c>_activeDevice?.Id ?? 0</c> to
            /// StuckInputGuard, so 0 is the "no device" sentinel and user index 0
            /// must not be able to collide with it.
            /// </summary>
            public required int Id { get; init; }

            public bool Connected { get; set; }
            public int ConsecutiveFailures { get; set; }
            public TimeSpan LastActivity { get; set; }
            public PadReading LastReading { get; set; }
            public uint PacketNumber { get; set; }
            public uint LastResult { get; set; }
            public bool ReadOkThisTick { get; set; }
        }

        private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;
        private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;
        private readonly DeviceSlot[] _slots;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly GamepadRepeatTracker _repeatTracker = new(InitialRepeatDelay, RepeatInterval);
        private readonly StuckInputGuard _stuckGuard = new();
        private readonly HashSet<GamepadAction> _rawHeld = new();
        private readonly HashSet<GamepadAction> _heldActions = new();
        private readonly Dictionary<GamepadAction, TimeSpan> _lastKeyboardEmit = new();
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

        /// <summary>Right-stick analog frames for scroll consumers.</summary>
        public event EventHandler<GamepadStickFrame>? StickFrame;

        public event EventHandler<GamepadConnectionEventArgs>? GamepadConnected;
        public event EventHandler<GamepadConnectionEventArgs>? GamepadDisconnected;

        public bool HasConnectedGamepads
        {
            get
            {
                foreach (var slot in _slots)
                {
                    if (slot.Connected) return true;
                }
                return false;
            }
        }

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
            int connected = _slots.Count(slot => slot.Connected);
            sb.Append($"backend=XInput connected={connected} polling={IsPollingActive} " +
                      $"active=#{_activeDevice?.Id.ToString() ?? "none"}");

            // Every slot is listed, connected or not: "which indexes came back
            // ERROR_DEVICE_NOT_CONNECTED" is the first question in a dead-pad report.
            foreach (var slot in _slots)
            {
                sb.AppendLine();
                sb.Append($"  #{slot.Id} user={slot.UserIndex} connected={slot.Connected} " +
                          $"fails={slot.ConsecutiveFailures} " +
                          $"suspect={_stuckGuard.IsDeviceSuspect(slot.Id)} ");

                if (slot.Connected)
                {
                    var r = slot.LastReading;
                    sb.Append($"packet={slot.PacketNumber} buttons=0x{r.Buttons:X4} " +
                              $"LT={r.LeftTrigger:F2} RT={r.RightTrigger:F2} " +
                              $"LS=({r.LeftThumbstickX:F2},{r.LeftThumbstickY:F2}) " +
                              $"RS=({r.RightThumbstickX:F2},{r.RightThumbstickY:F2})");
                }
                else
                {
                    sb.Append($"error=0x{slot.LastResult:X4}");
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

            // Fixed slots, one per XInput user index: the index is the identity, so
            // slots are never added or removed, only marked connected/disconnected.
            _slots = new DeviceSlot[XInputNative.MaxUserCount];
            for (uint i = 0; i < XInputNative.MaxUserCount; i++)
            {
                _slots[i] = new DeviceSlot { UserIndex = i, Id = (int)i + 1 };
            }

            _stuckGuard.MaskChanged += message => DiagnosticLog?.Invoke(message);

            // No initial census here, deliberately: the reconcile throttle starts
            // one interval in the past, so the timer's FIRST tick (within 16 ms)
            // probes all four slots. Doing it synchronously in the constructor
            // would fire connect events and diagnostic lines before the owning
            // service has subscribed, losing the "device connected" session
            // markers that make field logs decodable.
            EnsureTimerState();
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
        /// Short haptic pulse on every connected controller. Runs entirely on a
        /// background thread: the vibration call can block for a long time on some
        /// (especially Bluetooth) controllers, and on the UI thread that showed up
        /// as random multi-second hitches on page navigation. XInputSetState is a
        /// plain P/Invoke with no thread affinity, so off-thread use is safe.
        /// </summary>
        public void PulseHaptics(double intensity = 0.2, int durationMs = 100)
        {
            // Snapshot on the calling (UI) thread; the background task must not
            // walk slot state that the poll timer is mutating.
            var userIndexes = _slots.Where(slot => slot.Connected)
                                    .Select(slot => slot.UserIndex)
                                    .ToArray();
            if (userIndexes.Length == 0) return;

            ushort speed = (ushort)(Math.Clamp(intensity, 0.0, 1.0) * ushort.MaxValue);

            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    var on = new XInputNative.XINPUT_VIBRATION
                    {
                        wLeftMotorSpeed = speed,
                        wRightMotorSpeed = speed
                    };
                    foreach (var index in userIndexes)
                    {
                        XInputNative.XInputSetState(index, ref on);
                    }

                    await System.Threading.Tasks.Task.Delay(durationMs);

                    var off = default(XInputNative.XINPUT_VIBRATION);
                    foreach (var index in userIndexes)
                    {
                        XInputNative.XInputSetState(index, ref off);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"🎮 Haptic feedback error: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Mark a slot live. Fired edge-triggered only - never per tick.
        /// </summary>
        private void MarkConnected(DeviceSlot slot, TimeSpan now)
        {
            slot.Connected = true;
            slot.ConsecutiveFailures = 0;
            slot.LastActivity = now;

            Debug.WriteLine($"🎮 Gamepad connected (XInput user index {slot.UserIndex})");
            DiagnosticLog?.Invoke($"device #{slot.Id} connected (XInput user index {slot.UserIndex})");
            GamepadConnected?.Invoke(this, new GamepadConnectionEventArgs(slot.Id));

            // Held state from before the topology change is no longer meaningful.
            ResetInputState();
            EnsureTimerState();
        }

        /// <summary>
        /// Mark a slot empty and drop everything derived from it. The slot object
        /// stays: the user index still exists, it just has nothing plugged into it.
        /// </summary>
        private void MarkDisconnected(DeviceSlot slot, string reason)
        {
            slot.Connected = false;
            slot.ConsecutiveFailures = 0;
            slot.ReadOkThisTick = false;
            slot.LastReading = default;
            slot.PacketNumber = 0;

            if (ReferenceEquals(_activeDevice, slot)) _activeDevice = null;
            _stuckGuard.ForgetDevice(slot.Id);

            Debug.WriteLine($"🎮 Gamepad removed (XInput user index {slot.UserIndex})");
            DiagnosticLog?.Invoke($"device #{slot.Id} removed ({reason})");
            GamepadDisconnected?.Invoke(this, new GamepadConnectionEventArgs(slot.Id));

            ResetInputState();
            EnsureTimerState();
        }

        /// <summary>
        /// The poll timer runs for the life of the reader, even with zero devices.
        /// XInput has no hotplug notification of any kind, so this timer IS device
        /// discovery: the 2 s probe of the empty user indexes only happens on a
        /// tick, and stopping the timer at zero devices would mean a controller
        /// plugged in afterwards is never noticed at all. (The same always-on timer
        /// is what recovered from the WGI foreground-gating incidents - a pad
        /// withdrawn for 13 s while a browser held the foreground, and 32 s of
        /// successful-but-neutral readings.) An idle tick with no devices is four
        /// slot checks and an early return.
        /// </summary>
        private void EnsureTimerState()
        {
            if (!_timer.IsRunning) _timer.Start();
        }

        /// <summary>
        /// Probe every XInput user index, empty ones included, so a controller that
        /// appeared or vanished since the last probe is picked up. Runs on the 2 s
        /// cadence from the tick, or immediately when forced (window shown, resume
        /// from sleep). Shares <see cref="PollSlots"/> with the tick so a forced
        /// reconcile on a tick boundary can never read a slot twice.
        /// </summary>
        public void ReconcileDevices(bool force = false)
        {
            var now = _clock.Elapsed;
            if (force) _lastReconcile = now;
            else if (!TryConsumeReconcile(now)) return;

            PollSlots(now, probeDisconnected: true);
            SelectActiveSlot(now);
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

            // Live slots are read every tick; empty user indexes only on the
            // reconcile cadence. One read per slot per tick either way.
            bool probe = TryConsumeReconcile(now);
            PollSlots(now, probeDisconnected: probe);

            var reading = SelectActiveSlot(now);

            if (!HasConnectedGamepads) return;

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
        }

        /// <summary>
        /// True at most once per <see cref="ReconcileInterval"/>, and consumes the
        /// slot when it returns true - so the tick and an unforced
        /// <see cref="ReconcileDevices"/> can never both probe in the same window.
        /// </summary>
        private bool TryConsumeReconcile(TimeSpan now)
        {
            if (now - _lastReconcile < ReconcileInterval) return false;
            _lastReconcile = now;
            return true;
        }

        /// <summary>
        /// The single read path. Connected slots are always read; empty user
        /// indexes only when <paramref name="probeDisconnected"/> is set, because
        /// XInputGetState on an index with nothing plugged in is markedly slower
        /// than on a live one (the documented reason XInput callers are told not to
        /// scan empty slots every frame) - hence the 2 s cadence.
        /// </summary>
        private void PollSlots(TimeSpan now, bool probeDisconnected)
        {
            foreach (var slot in _slots)
            {
                if (!slot.Connected && !probeDisconnected)
                {
                    slot.ReadOkThisTick = false;
                    continue;
                }

                ReadSlot(slot, now);
            }
        }

        /// <summary>
        /// Read one user index. A failing slot must never be able to abort the
        /// tick: aborting previously skipped the whole state machine, so no release
        /// edges were produced and the held set froze permanently.
        /// </summary>
        private void ReadSlot(DeviceSlot slot, TimeSpan now)
        {
            slot.ReadOkThisTick = false;

            uint result = XInputNative.XInputGetState(slot.UserIndex, out var state);
            slot.LastResult = result;

            if (result == XInputNative.ERROR_DEVICE_NOT_CONNECTED)
            {
                // Authoritative "nothing is plugged in here", not a fault: counting
                // it as a failure would drift a permanently-empty index towards an
                // eviction that means nothing for a fixed slot.
                slot.ConsecutiveFailures = 0;
                if (slot.Connected) MarkDisconnected(slot, "device not connected");
                return;
            }

            if (result != XInputNative.ERROR_SUCCESS)
            {
                // Some other Win32 error: could be transient, so fall back to the
                // consecutive-failure budget rather than dropping the pad at once.
                if (slot.Connected && ++slot.ConsecutiveFailures >= MaxConsecutiveFailures)
                {
                    MarkDisconnected(slot,
                        $"evicted after {slot.ConsecutiveFailures} read failures, error=0x{result:X4}");
                }
                return;
            }

            slot.ConsecutiveFailures = 0;
            slot.PacketNumber = state.dwPacketNumber;
            slot.LastReading = PadReading.From(state.Gamepad);
            slot.ReadOkThisTick = true;

            if (!slot.Connected) MarkConnected(slot, now);

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

        /// <summary>
        /// Return the reading the pipeline uses this tick: the most recently active
        /// controller's.
        ///
        /// Deliberately NOT a merge of all devices. Merging (OR'ing buttons, taking
        /// the largest axis) meant one controller with a latched button or drifting
        /// stick overrode every other device forever - the opposite of the intent.
        /// "Most recently active wins" achieves the original goal (an idle second pad
        /// cannot interfere) while letting a stuck one be ignored, and hands over
        /// instantly when the user picks up a different controller.
        /// </summary>
        private PadReading SelectActiveSlot(TimeSpan now)
        {
            // Release the active slot once it has been quiet for a while, so another
            // controller can take over immediately when it is used.
            if (_activeDevice != null &&
                (!_activeDevice.Connected || now - _activeDevice.LastActivity > ActiveDeviceTimeout))
            {
                _activeDevice = null;
            }

            _activeDevice ??= _slots.FirstOrDefault(slot => slot.Connected && slot.ReadOkThisTick);

            return _activeDevice is { ReadOkThisTick: true } ? _activeDevice.LastReading : default;
        }

        /// <summary>Any input at all, used to decide which device the user is holding.</summary>
        private static bool IsActive(in PadReading reading) =>
            reading.Buttons != 0 ||
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
        private void BuildRawHeld(in PadReading reading)
        {
            _rawHeld.Clear();
            ushort buttons = reading.Buttons;

            // Stick-as-dpad with per-direction hysteresis
            _stickUp = ApplyHysteresis(_stickUp, reading.LeftThumbstickY);
            _stickDown = ApplyHysteresis(_stickDown, -reading.LeftThumbstickY);
            _stickLeft = ApplyHysteresis(_stickLeft, -reading.LeftThumbstickX);
            _stickRight = ApplyHysteresis(_stickRight, reading.LeftThumbstickX);

            if (IsSet(buttons, XInputNative.XINPUT_GAMEPAD_DPAD_UP) || _stickUp) _rawHeld.Add(GamepadAction.NavUp);
            if (IsSet(buttons, XInputNative.XINPUT_GAMEPAD_DPAD_DOWN) || _stickDown) _rawHeld.Add(GamepadAction.NavDown);
            if (IsSet(buttons, XInputNative.XINPUT_GAMEPAD_DPAD_LEFT) || _stickLeft) _rawHeld.Add(GamepadAction.NavLeft);
            if (IsSet(buttons, XInputNative.XINPUT_GAMEPAD_DPAD_RIGHT) || _stickRight) _rawHeld.Add(GamepadAction.NavRight);

            if (IsSet(buttons, XInputNative.XINPUT_GAMEPAD_A)) _rawHeld.Add(GamepadAction.Accept);
            if (IsSet(buttons, XInputNative.XINPUT_GAMEPAD_B)) _rawHeld.Add(GamepadAction.Back);
            if (IsSet(buttons, XInputNative.XINPUT_GAMEPAD_X)) _rawHeld.Add(GamepadAction.X);
            if (IsSet(buttons, XInputNative.XINPUT_GAMEPAD_Y)) _rawHeld.Add(GamepadAction.Y);
            if (IsSet(buttons, XInputNative.XINPUT_GAMEPAD_LEFT_SHOULDER)) _rawHeld.Add(GamepadAction.LB);
            if (IsSet(buttons, XInputNative.XINPUT_GAMEPAD_RIGHT_SHOULDER)) _rawHeld.Add(GamepadAction.RB);

            _leftTriggerHeld = ApplyTriggerHysteresis(_leftTriggerHeld, reading.LeftTrigger);
            _rightTriggerHeld = ApplyTriggerHysteresis(_rightTriggerHeld, reading.RightTrigger);
            if (_leftTriggerHeld) _rawHeld.Add(GamepadAction.LT);
            if (_rightTriggerHeld) _rawHeld.Add(GamepadAction.RT);
        }

        private static bool IsSet(ushort buttons, ushort mask) => (buttons & mask) != 0;

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

            // No hotplug subscriptions to unhook any more; just drop derived state.
            foreach (var slot in _slots)
            {
                slot.Connected = false;
                slot.ReadOkThisTick = false;
                slot.LastReading = default;
            }
            _activeDevice = null;
        }
    }
}
