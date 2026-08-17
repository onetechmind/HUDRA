using System;

namespace HUDRA.Services.Power
{
    /// <summary>
    /// The AC/DC state reported by GUID_ACDC_POWER_SOURCE.
    /// </summary>
    public enum PowerSourceState
    {
        Ac,
        Dc
    }

    /// <summary>
    /// Turns the raw stream of GUID_ACDC_POWER_SOURCE payload values into genuine
    /// AC/DC transitions.
    ///
    /// Deliberately dependency-free (no P/Invoke, no WinUI, no I/O) so it can be
    /// compiled into the test project and verified on any platform.
    ///
    /// Two behaviours are load-bearing:
    /// - The first observation only establishes the baseline. Windows delivers an
    ///   immediate callback when the notification is registered, and app startup has
    ///   already applied TDP by then, so treating it as a transition would double-apply.
    /// - Repeats of the current state are dropped. Windows can re-broadcast the same
    ///   value, and every reported transition costs an SMU write.
    ///
    /// Not thread-safe: it is driven only from the window procedure, i.e. the UI thread.
    /// </summary>
    public sealed class PowerSourceTransitionTracker
    {
        private bool _hasBaseline;
        private PowerSourceState _current;

        /// <summary>True once a first raw value has been observed.</summary>
        public bool HasBaseline => _hasBaseline;

        /// <summary>The last observed state, or null before the baseline is established.</summary>
        public PowerSourceState? Current => _hasBaseline ? _current : null;

        /// <summary>
        /// Maps a POWERBROADCAST_SETTING payload byte to a state. 0 = AC, 1 = DC,
        /// 2 = short-term UPS, which is battery-like and so is treated as DC.
        /// Anything else is unspecified by Windows; the conservative reading is DC.
        /// </summary>
        public static PowerSourceState Classify(int rawData) =>
            rawData == 0 ? PowerSourceState.Ac : PowerSourceState.Dc;

        /// <summary>
        /// Records a raw payload value. Returns true only when it represents a real
        /// change of power source, in which case <paramref name="state"/> is the new state.
        /// </summary>
        public bool TryObserve(int rawData, out PowerSourceState state)
        {
            state = Classify(rawData);

            if (!_hasBaseline)
            {
                _hasBaseline = true;
                _current = state;
                return false;
            }

            if (_current == state)
            {
                return false;
            }

            _current = state;
            return true;
        }
    }
}
