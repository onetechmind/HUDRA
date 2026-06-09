using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;
        private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;
        private readonly List<Windows.Gaming.Input.Gamepad> _gamepads = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly GamepadRepeatTracker _repeatTracker = new(InitialRepeatDelay, RepeatInterval);
        private readonly HashSet<GamepadAction> _heldActions = new();
        private readonly Dictionary<GamepadAction, TimeSpan> _lastKeyboardEmit = new();

        // Stick-as-dpad and trigger hysteresis state
        private bool _stickUp, _stickDown, _stickLeft, _stickRight;
        private bool _leftTriggerHeld, _rightTriggerHeld;
        private double _lastRightStickY;
        private bool _suspended;

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

        public bool HasConnectedGamepads => _gamepads.Count > 0;

        public GamepadInputReader()
        {
            _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()
                ?? throw new InvalidOperationException("GamepadInputReader must be created on the UI thread");

            _timer = _dispatcherQueue.CreateTimer();
            _timer.Interval = PollInterval;
            _timer.Tick += OnTick;

            Windows.Gaming.Input.Gamepad.GamepadAdded += OnGamepadAdded;
            Windows.Gaming.Input.Gamepad.GamepadRemoved += OnGamepadRemoved;

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

        /// <summary>Short haptic pulse on all connected gamepads.</summary>
        public void PulseHaptics(double intensity = 0.2, int durationMs = 100)
        {
            try
            {
                foreach (var gamepad in _gamepads)
                {
                    gamepad.Vibration = new GamepadVibration
                    {
                        LeftMotor = intensity,
                        RightMotor = intensity
                    };

                    var timer = _dispatcherQueue.CreateTimer();
                    timer.Interval = TimeSpan.FromMilliseconds(durationMs);
                    timer.Tick += (s, e) =>
                    {
                        gamepad.Vibration = new GamepadVibration();
                        timer.Stop();
                    };
                    timer.Start();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"🎮 Haptic feedback error: {ex.Message}");
            }
        }

        /// <summary>Stop polling (e.g. while a blocking modal suspends gamepad input).</summary>
        public void SuspendPolling()
        {
            if (_suspended) return;
            _suspended = true;
            _timer.Stop();
            ResetInputState();
        }

        /// <summary>Resume polling after <see cref="SuspendPolling"/>.</summary>
        public void ResumePolling()
        {
            if (!_suspended) return;
            _suspended = false;
            if (_gamepads.Count > 0)
            {
                _timer.Start();
            }
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
            if (_gamepads.Contains(gamepad)) return;

            _gamepads.Add(gamepad);
            Debug.WriteLine($"🎮 Gamepad connected ({_gamepads.Count} total)");
            GamepadConnected?.Invoke(this, new GamepadConnectionEventArgs(gamepad));

            if (_gamepads.Count == 1 && !_suspended)
            {
                _timer.Start();
            }
        }

        private void RemoveGamepad(Windows.Gaming.Input.Gamepad gamepad)
        {
            if (!_gamepads.Remove(gamepad)) return;

            Debug.WriteLine($"🎮 Gamepad disconnected ({_gamepads.Count} remaining)");
            GamepadDisconnected?.Invoke(this, new GamepadConnectionEventArgs(gamepad));

            if (_gamepads.Count == 0)
            {
                _timer.Stop();
                ResetInputState();
            }
        }

        private void ResetInputState()
        {
            _repeatTracker.Reset();
            _heldActions.Clear();
            _stickUp = _stickDown = _stickLeft = _stickRight = false;
            _leftTriggerHeld = _rightTriggerHeld = false;
            _lastRightStickY = 0;
        }

        private void OnTick(object? sender, object e)
        {
            if (_gamepads.Count == 0) return;

            GamepadReading reading;
            try
            {
                reading = ReadCombined();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"🎮 Error reading gamepad: {ex.Message}");
                return;
            }

            var now = _clock.Elapsed;
            UpdateHeldActions(reading);

            var events = _repeatTracker.Update(_heldActions, now);
            foreach (var ev in events)
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
        /// Combine all connected gamepads into one reading (buttons OR'd, axes
        /// by largest magnitude) so a second idle controller can't mask input
        /// from the active one.
        /// </summary>
        private GamepadReading ReadCombined()
        {
            if (_gamepads.Count == 1)
            {
                return _gamepads[0].GetCurrentReading();
            }

            var combined = new GamepadReading();
            foreach (var gamepad in _gamepads)
            {
                var r = gamepad.GetCurrentReading();
                combined.Buttons |= r.Buttons;
                combined.LeftTrigger = Math.Max(combined.LeftTrigger, r.LeftTrigger);
                combined.RightTrigger = Math.Max(combined.RightTrigger, r.RightTrigger);
                if (Math.Abs(r.LeftThumbstickX) > Math.Abs(combined.LeftThumbstickX)) combined.LeftThumbstickX = r.LeftThumbstickX;
                if (Math.Abs(r.LeftThumbstickY) > Math.Abs(combined.LeftThumbstickY)) combined.LeftThumbstickY = r.LeftThumbstickY;
                if (Math.Abs(r.RightThumbstickX) > Math.Abs(combined.RightThumbstickX)) combined.RightThumbstickX = r.RightThumbstickX;
                if (Math.Abs(r.RightThumbstickY) > Math.Abs(combined.RightThumbstickY)) combined.RightThumbstickY = r.RightThumbstickY;
            }
            return combined;
        }

        private void UpdateHeldActions(in GamepadReading reading)
        {
            _heldActions.Clear();
            var buttons = reading.Buttons;

            // Stick-as-dpad with per-direction hysteresis
            _stickUp = ApplyHysteresis(_stickUp, reading.LeftThumbstickY);
            _stickDown = ApplyHysteresis(_stickDown, -reading.LeftThumbstickY);
            _stickLeft = ApplyHysteresis(_stickLeft, -reading.LeftThumbstickX);
            _stickRight = ApplyHysteresis(_stickRight, reading.LeftThumbstickX);

            // At most ONE directional action per tick. Linear/spatial navigation
            // moves one step per event; emitting two directions on a diagonal
            // would double-step. Priority order matches the old service.
            if (buttons.HasFlag(GamepadButtons.DPadUp) || _stickUp) _heldActions.Add(GamepadAction.NavUp);
            else if (buttons.HasFlag(GamepadButtons.DPadDown) || _stickDown) _heldActions.Add(GamepadAction.NavDown);
            else if (buttons.HasFlag(GamepadButtons.DPadLeft) || _stickLeft) _heldActions.Add(GamepadAction.NavLeft);
            else if (buttons.HasFlag(GamepadButtons.DPadRight) || _stickRight) _heldActions.Add(GamepadAction.NavRight);

            if (buttons.HasFlag(GamepadButtons.A)) _heldActions.Add(GamepadAction.Accept);
            if (buttons.HasFlag(GamepadButtons.B)) _heldActions.Add(GamepadAction.Back);
            if (buttons.HasFlag(GamepadButtons.X)) _heldActions.Add(GamepadAction.X);
            if (buttons.HasFlag(GamepadButtons.Y)) _heldActions.Add(GamepadAction.Y);
            if (buttons.HasFlag(GamepadButtons.LeftShoulder)) _heldActions.Add(GamepadAction.LB);
            if (buttons.HasFlag(GamepadButtons.RightShoulder)) _heldActions.Add(GamepadAction.RB);

            _leftTriggerHeld = ApplyTriggerHysteresis(_leftTriggerHeld, reading.LeftTrigger);
            _rightTriggerHeld = ApplyTriggerHysteresis(_rightTriggerHeld, reading.RightTrigger);
            if (_leftTriggerHeld) _heldActions.Add(GamepadAction.LT);
            if (_rightTriggerHeld) _heldActions.Add(GamepadAction.RT);
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
            _gamepads.Clear();
        }
    }
}
