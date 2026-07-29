using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace HUDRA.Services.GamepadInput
{
    /// <summary>
    /// Ordered stack of input scopes. Events are dispatched top-down until a
    /// scope consumes them. Free of WinUI types so ordering, removal and reaping
    /// semantics can be unit tested cross-platform.
    ///
    /// Two invariants keep a leaked scope from wedging input:
    ///  * scopes are inserted by <see cref="IInputScope.Layer"/>, so a late push
    ///    can never land above a scope that must stay on top; and
    ///  * <see cref="Reap"/> drops scopes whose context has gone away, so a
    ///    missing explicit removal heals on the next event.
    /// </summary>
    public sealed class InputRouter
    {
        /// <summary>
        /// How long a freshly-pushed scope is exempt from reaping while it has
        /// never yet reported itself valid. Covers scopes pushed before their
        /// context finishes opening (a ContentDialog is pushed before ShowAsync
        /// realizes its template).
        /// </summary>
        internal static readonly TimeSpan ArmingGrace = TimeSpan.FromMilliseconds(750);

        private sealed class Entry
        {
            public required IInputScope Scope { get; init; }
            public required TimeSpan PushedAt { get; init; }

            /// <summary>Set once the scope has been observed valid at least once.</summary>
            public bool Armed { get; set; }
        }

        private readonly List<Entry> _entries = new();
        private readonly Func<TimeSpan> _clock;

        public InputRouter(Func<TimeSpan>? clock = null)
        {
            if (clock != null)
            {
                _clock = clock;
            }
            else
            {
                var stopwatch = Stopwatch.StartNew();
                _clock = () => stopwatch.Elapsed;
            }
        }

        public event EventHandler? StackChanged;

        /// <summary>
        /// Optional sink for edge-triggered stack transitions (push/remove/reap).
        /// Kept as a delegate rather than a direct logger reference so this class
        /// stays free of WinUI/app types and remains unit-testable.
        /// </summary>
        public Action<string>? DiagnosticLog { get; set; }

        /// <summary>Number of scopes reaped over the router's lifetime (diagnostics).</summary>
        public int ReapCount { get; private set; }

        /// <summary>Name of the most recently reaped scope, if any (diagnostics).</summary>
        public string? LastReaped { get; private set; }

        public IInputScope? Top => _entries.Count > 0 ? _entries[^1].Scope : null;

        public IReadOnlyList<IInputScope> Stack => _entries.Select(entry => entry.Scope).ToList();

        public bool Contains(IInputScope scope) => IndexOf(scope) >= 0;

        public bool HasScope<T>() where T : IInputScope => _entries.Any(entry => entry.Scope is T);

        public T? FindScope<T>() where T : class, IInputScope =>
            _entries.Select(entry => entry.Scope).OfType<T>().LastOrDefault();

        /// <summary>
        /// Insert a scope at the top of its own layer band. Idempotent by reference.
        /// </summary>
        public void Push(IInputScope scope)
        {
            if (Contains(scope)) return;

            int layer = scope.Layer;
            int index = _entries.Count;
            while (index > 0 && _entries[index - 1].Scope.Layer > layer)
            {
                index--;
            }

            _entries.Insert(index, new Entry { Scope = scope, PushedAt = _clock() });
            scope.OnPushed(this);
            Report($"pushed [{scope.Name}]");
            StackChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Remove exactly one scope, leaving everything else in place. No-op if the
        /// scope is not on the stack.
        ///
        /// Deliberately non-cascading: the previous behaviour also removed every
        /// scope above the target, which meant removing a transient scope could
        /// silently take a page scope with it and leave the page unnavigable.
        /// Layer ordering makes cascading unnecessary, and <see cref="Reap"/>
        /// handles scopes whose own context died.
        /// </summary>
        public void Remove(IInputScope scope)
        {
            int index = IndexOf(scope);
            if (index < 0) return;

            _entries.RemoveAt(index);
            scope.OnPopped();
            Report($"removed [{scope.Name}]");
            StackChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Remove every scope matching the predicate, from the top down.
        /// </summary>
        public void RemoveWhere(Func<IInputScope, bool> predicate)
        {
            var removed = new List<IInputScope>();
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var scope = _entries[i].Scope;
                if (!predicate(scope)) continue;

                _entries.RemoveAt(i);
                removed.Add(scope);
            }

            if (removed.Count == 0) return;

            foreach (var scope in removed)
            {
                scope.OnPopped();
            }
            Report($"removed [{string.Join(", ", removed.Select(s => s.Name))}]");
            StackChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Drop scopes whose context has gone away, top-down. Shell and Page are
        /// never reaped — they are permanent and their removal would disable all
        /// navigation. Returns the number of scopes removed.
        ///
        /// A scope that has never yet reported itself valid is left alone until
        /// <see cref="ArmingGrace"/> elapses, so scopes pushed slightly before
        /// their context opens are not killed on arrival.
        /// </summary>
        public int Reap()
        {
            var now = _clock();
            var reaped = new List<IInputScope>();

            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var entry = _entries[i];
                if (entry.Scope.Layer <= ScopeLayer.Page) continue;

                bool valid;
                try
                {
                    valid = entry.Scope.IsStillValid;
                }
                catch
                {
                    // A scope that cannot answer is assumed alive: reaping a live
                    // scope aborts a real interaction, keeping a dead one only
                    // delays the heal to the explicit removal path.
                    valid = true;
                }

                if (valid)
                {
                    entry.Armed = true;
                    continue;
                }

                // Still opening: not yet armed and within the grace period.
                if (!entry.Armed && now - entry.PushedAt < ArmingGrace) continue;

                _entries.RemoveAt(i);
                reaped.Add(entry.Scope);
            }

            if (reaped.Count == 0) return 0;

            foreach (var scope in reaped)
            {
                scope.OnPopped();
            }

            ReapCount += reaped.Count;
            LastReaped = reaped[0].Name;
            Report($"REAPED [{string.Join(", ", reaped.Select(s => s.Name))}] (context gone)");
            StackChanged?.Invoke(this, EventArgs.Empty);
            return reaped.Count;
        }

        /// <summary>
        /// Dispatch an event top-down. Returns the scope that consumed it, or
        /// null if no scope did. Stale scopes are reaped first.
        /// </summary>
        public IInputScope? Dispatch(in GamepadEvent e)
        {
            Reap();

            // Snapshot: a consuming scope may push/remove scopes from HandleEvent
            var snapshot = _entries.Select(entry => entry.Scope).ToArray();
            for (int i = snapshot.Length - 1; i >= 0; i--)
            {
                var scope = snapshot[i];
                // Skip scopes that were removed by an earlier handler this dispatch
                if (!Contains(scope)) continue;
                if (scope.HandleEvent(in e))
                {
                    return scope;
                }
            }
            return null;
        }

        /// <summary>
        /// Deliver an analog stick frame to the top scope only. Reaping is
        /// throttled here: stick frames arrive at ~60Hz and one validity check
        /// walks the visual tree.
        /// </summary>
        public void DispatchStickFrame(in GamepadStickFrame frame)
        {
            var now = _clock();
            if (now - _lastStickReap >= StickReapInterval)
            {
                _lastStickReap = now;
                Reap();
            }

            Top?.OnStickFrame(in frame);
        }

        private static readonly TimeSpan StickReapInterval = TimeSpan.FromMilliseconds(100);
        private TimeSpan _lastStickReap = TimeSpan.MinValue;

        /// <summary>Bottom-to-top description of the stack, for diagnostics.</summary>
        public string Describe()
        {
            if (_entries.Count == 0) return "(empty)";

            return string.Join(" / ", _entries.Select(entry =>
            {
                bool valid;
                try { valid = entry.Scope.IsStillValid; } catch { valid = true; }

                // An invalid scope that has never reported itself valid is still
                // opening (grace applies); only an armed one is genuinely stale.
                var flags = valid ? "" : (entry.Armed ? ":STALE" : ":opening");
                return $"{entry.Scope.Name}({entry.Scope.Layer}){flags}";
            }));
        }

        private int IndexOf(IInputScope scope)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (ReferenceEquals(_entries[i].Scope, scope)) return i;
            }
            return -1;
        }

        private void Report(string what)
        {
            var message = $"InputRouter: {what} → {Describe()}";
            Debug.WriteLine($"🎮 {message}");
            DiagnosticLog?.Invoke(message);
        }
    }
}
