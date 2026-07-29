using System;
using System.Collections.Generic;

namespace HUDRA.Services.GamepadInput
{
    /// <summary>
    /// Masks inputs that are physically implausible, so a controller reporting a
    /// latched button or a pegged stick cannot silently disable navigation.
    ///
    /// Why this is needed: action buttons (LB/RB/A/B/X/Y/LT/RT) deliberately do not
    /// auto-repeat, so a permanently-held one fires exactly once and then never
    /// again — the reported "LB suddenly stops working". A permanently-held stick
    /// direction occupies the single directional slot and masks the others — the
    /// reported "cannot traverse anything". Neither recovers on its own, and a ghost
    /// device (a stale wrapper after sleep/resume, or a virtual pad left behind by
    /// an input remapper) is a common source of both.
    ///
    /// Thresholds are deliberately generous: users really do rest a thumb on A while
    /// reading, or hold a trigger while deciding. Masking too eagerly would
    /// manufacture a new intermittent bug that is harder to diagnose than the one
    /// being fixed. Masking is released as soon as the input is observed released.
    ///
    /// Pure and free of WinUI/WinRT types so it can be unit tested.
    /// </summary>
    public sealed class StuckInputGuard
    {
        public static readonly TimeSpan DefaultNonRepeatableStuckAfter = TimeSpan.FromSeconds(8);
        public static readonly TimeSpan DefaultDirectionalStuckAfter = TimeSpan.FromSeconds(20);
        public static readonly TimeSpan DefaultNeverNeutralSuspectAfter = TimeSpan.FromSeconds(3);

        private readonly TimeSpan _nonRepeatableStuckAfter;
        private readonly TimeSpan _directionalStuckAfter;
        private readonly TimeSpan _neverNeutralSuspectAfter;

        private sealed class DeviceState
        {
            public TimeSpan FirstSeen;
            public bool EverNeutral;
            public bool Suspect;
            public readonly Dictionary<GamepadAction, TimeSpan> HeldSince = new();
            public readonly HashSet<GamepadAction> Masked = new();
        }

        private readonly Dictionary<int, DeviceState> _devices = new();
        private readonly HashSet<GamepadAction> _maskedForDiagnostics = new();

        public StuckInputGuard()
            : this(DefaultNonRepeatableStuckAfter, DefaultDirectionalStuckAfter, DefaultNeverNeutralSuspectAfter)
        {
        }

        public StuckInputGuard(TimeSpan nonRepeatableStuckAfter, TimeSpan directionalStuckAfter,
                               TimeSpan neverNeutralSuspectAfter)
        {
            _nonRepeatableStuckAfter = nonRepeatableStuckAfter;
            _directionalStuckAfter = directionalStuckAfter;
            _neverNeutralSuspectAfter = neverNeutralSuspectAfter;
        }

        /// <summary>Actions currently masked across all devices (diagnostics).</summary>
        public IReadOnlyCollection<GamepadAction> MaskedActions => _maskedForDiagnostics;

        /// <summary>
        /// A device that has never once reported a neutral reading. Excluded from
        /// "most recently active device" selection, since it would win forever.
        /// </summary>
        public bool IsDeviceSuspect(int deviceId) =>
            _devices.TryGetValue(deviceId, out var state) && state.Suspect;

        /// <summary>Raised when masking starts or stops, for edge-triggered logging.</summary>
        public event Action<string>? MaskChanged;

        /// <summary>
        /// Returns <paramref name="held"/> with any input judged stuck removed.
        /// Must be applied to the RAW held set, before directional reduction.
        /// </summary>
        public IReadOnlySet<GamepadAction> Filter(int deviceId, IReadOnlySet<GamepadAction> held, TimeSpan now)
        {
            if (!_devices.TryGetValue(deviceId, out var state))
            {
                state = new DeviceState { FirstSeen = now };
                _devices[deviceId] = state;
            }

            if (held.Count == 0)
            {
                // A neutral reading clears everything: this is the only evidence that
                // the device is reporting honestly.
                if (state.Masked.Count > 0)
                {
                    ReportMaskChange($"stuck-input mask cleared for device {deviceId} (device went neutral)");
                }
                state.EverNeutral = true;
                state.Suspect = false;
                state.HeldSince.Clear();
                state.Masked.Clear();
                RebuildDiagnostics();
                return held;
            }

            // Forget actions that were released, so a genuine later press is fresh.
            var released = new List<GamepadAction>();
            foreach (var tracked in state.HeldSince.Keys)
            {
                if (!held.Contains(tracked)) released.Add(tracked);
            }
            foreach (var action in released)
            {
                state.HeldSince.Remove(action);
                if (state.Masked.Remove(action))
                {
                    ReportMaskChange($"stuck-input mask cleared for {action} on device {deviceId} (released)");
                }
            }

            foreach (var action in held)
            {
                if (!state.HeldSince.ContainsKey(action)) state.HeldSince[action] = now;
            }

            if (!state.EverNeutral && !state.Suspect && now - state.FirstSeen >= _neverNeutralSuspectAfter)
            {
                state.Suspect = true;
                ReportMaskChange($"device {deviceId} marked suspect (never reported neutral)");
            }

            if (state.Suspect)
            {
                // Nothing from this device can be trusted until it reports neutral.
                foreach (var action in held) state.Masked.Add(action);
            }
            else
            {
                foreach (var action in held)
                {
                    if (state.Masked.Contains(action)) continue;

                    var threshold = DirectionReducer.IsDirectional(action)
                        ? _directionalStuckAfter
                        : _nonRepeatableStuckAfter;

                    if (now - state.HeldSince[action] >= threshold)
                    {
                        state.Masked.Add(action);
                        ReportMaskChange($"masking stuck {action} on device {deviceId} " +
                                         $"(held {(now - state.HeldSince[action]).TotalSeconds:F0}s)");
                    }
                }
            }

            RebuildDiagnostics();

            if (state.Masked.Count == 0) return held;

            var filtered = new HashSet<GamepadAction>();
            foreach (var action in held)
            {
                if (!state.Masked.Contains(action)) filtered.Add(action);
            }
            return filtered;
        }

        /// <summary>Drop all state for an evicted device.</summary>
        public void ForgetDevice(int deviceId)
        {
            if (_devices.Remove(deviceId)) RebuildDiagnostics();
        }

        /// <summary>Drop all state (window hidden, suspend, hard reset).</summary>
        public void Reset()
        {
            _devices.Clear();
            RebuildDiagnostics();
        }

        private void RebuildDiagnostics()
        {
            _maskedForDiagnostics.Clear();
            foreach (var state in _devices.Values)
            {
                foreach (var action in state.Masked) _maskedForDiagnostics.Add(action);
            }
        }

        private void ReportMaskChange(string message) => MaskChanged?.Invoke(message);
    }
}
