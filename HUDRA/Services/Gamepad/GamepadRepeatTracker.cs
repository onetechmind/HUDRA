using System;
using System.Collections.Generic;

namespace HUDRA.Services.GamepadInput
{
    /// <summary>
    /// Pure press/repeat state machine. Fed the set of currently-held semantic
    /// actions every poll tick, it emits a press event on the tick an action
    /// first appears and, for directional actions only, repeat events after an
    /// initial delay at a fixed interval.
    ///
    /// Deliberately free of WinUI/WinRT types so it can be unit tested on any
    /// platform. Time is an arbitrary monotonic <see cref="TimeSpan"/> supplied
    /// by the caller.
    /// </summary>
    public sealed class GamepadRepeatTracker
    {
        private readonly record struct HeldState(TimeSpan PressedAt, TimeSpan LastEmitted);

        private readonly TimeSpan _initialDelay;
        private readonly TimeSpan _repeatInterval;
        private readonly Dictionary<GamepadAction, HeldState> _states = new();
        private readonly List<GamepadAction> _staleScratch = new();

        public GamepadRepeatTracker(TimeSpan initialDelay, TimeSpan repeatInterval)
        {
            _initialDelay = initialDelay;
            _repeatInterval = repeatInterval;
        }

        public static bool IsRepeatable(GamepadAction action) =>
            action is GamepadAction.NavUp or GamepadAction.NavDown or GamepadAction.NavLeft or GamepadAction.NavRight;

        /// <summary>
        /// Advance the state machine one tick. Returns the events to dispatch,
        /// in deterministic order. Actions absent from <paramref name="held"/>
        /// are treated as released and forgotten.
        /// </summary>
        public List<GamepadEvent> Update(IReadOnlySet<GamepadAction> held, TimeSpan now)
        {
            var events = new List<GamepadEvent>();

            foreach (var action in held)
            {
                if (!_states.TryGetValue(action, out var state))
                {
                    _states[action] = new HeldState(now, now);
                    events.Add(new GamepadEvent(action, IsRepeat: false, now));
                }
                else if (IsRepeatable(action) &&
                         now - state.PressedAt >= _initialDelay &&
                         now - state.LastEmitted >= _repeatInterval)
                {
                    _states[action] = state with { LastEmitted = now };
                    events.Add(new GamepadEvent(action, IsRepeat: true, now));
                }
            }

            _staleScratch.Clear();
            foreach (var tracked in _states.Keys)
            {
                if (!held.Contains(tracked))
                {
                    _staleScratch.Add(tracked);
                }
            }
            foreach (var stale in _staleScratch)
            {
                _states.Remove(stale);
            }

            return events;
        }

        /// <summary>Forget all held state (e.g. on gamepad disconnect or suspend).</summary>
        public void Reset() => _states.Clear();
    }
}
