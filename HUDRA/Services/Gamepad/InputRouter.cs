using System;
using System.Collections.Generic;
using System.Linq;

namespace HUDRA.Services.GamepadInput
{
    /// <summary>
    /// Explicit stack of input scopes. Events are dispatched top-down until a
    /// scope consumes them. Free of WinUI types so dispatch/stack semantics can
    /// be unit tested cross-platform.
    /// </summary>
    public sealed class InputRouter
    {
        private readonly List<IInputScope> _stack = new();

        public event EventHandler? StackChanged;

        /// <summary>
        /// Optional sink for edge-triggered stack transitions (push/remove/reap).
        /// Kept as a delegate rather than a direct logger reference so this class
        /// stays free of WinUI/app types and remains unit-testable.
        /// </summary>
        public Action<string>? DiagnosticLog { get; set; }

        public IInputScope? Top => _stack.Count > 0 ? _stack[^1] : null;

        public IReadOnlyList<IInputScope> Stack => _stack;

        public bool Contains(IInputScope scope) => _stack.Contains(scope);

        public bool HasScope<T>() where T : IInputScope => _stack.OfType<T>().Any();

        public T? FindScope<T>() where T : class, IInputScope => _stack.OfType<T>().LastOrDefault();

        public void Push(IInputScope scope)
        {
            if (_stack.Contains(scope)) return;

            _stack.Add(scope);
            scope.OnPushed(this);
            Report($"pushed [{scope.Name}]");
            StackChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Remove a scope. Tolerant: also pops anything stacked above it, since
        /// scopes above a removed scope have lost their context.
        /// No-op if the scope is not on the stack.
        /// </summary>
        public void Pop(IInputScope scope)
        {
            int index = _stack.IndexOf(scope);
            if (index < 0) return;

            for (int i = _stack.Count - 1; i >= index; i--)
            {
                var removed = _stack[i];
                _stack.RemoveAt(i);
                removed.OnPopped();
            }
            Report($"popped [{scope.Name}]");
            StackChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Pop scopes from the top while they match the predicate. Used to clear
        /// transient editing scopes (slider/dropdown) without disturbing the rest.
        /// </summary>
        public void PopWhile(Func<IInputScope, bool> predicate)
        {
            bool changed = false;
            while (_stack.Count > 0 && predicate(_stack[^1]))
            {
                var removed = _stack[^1];
                _stack.RemoveAt(_stack.Count - 1);
                removed.OnPopped();
                changed = true;
            }
            if (changed)
            {
                Report("popped transient scopes");
                StackChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Dispatch an event top-down. Returns the scope that consumed it, or
        /// null if no scope did.
        /// </summary>
        public IInputScope? Dispatch(in GamepadEvent e)
        {
            // Snapshot: a consuming scope may push/pop scopes from HandleEvent
            var snapshot = _stack.ToArray();
            for (int i = snapshot.Length - 1; i >= 0; i--)
            {
                var scope = snapshot[i];
                // Skip scopes that were removed by an earlier handler this dispatch
                if (!_stack.Contains(scope)) continue;
                if (scope.HandleEvent(in e))
                {
                    return scope;
                }
            }
            return null;
        }

        /// <summary>Deliver an analog stick frame to the top scope only.</summary>
        public void DispatchStickFrame(in GamepadStickFrame frame)
        {
            Top?.OnStickFrame(in frame);
        }

        /// <summary>Bottom-to-top description of the stack, for diagnostics.</summary>
        public string Describe() => _stack.Count == 0
            ? "(empty)"
            : string.Join(" / ", _stack.Select(s => s.Name));

        private void Report(string what)
        {
            var message = $"InputRouter: {what} → {Describe()}";
            System.Diagnostics.Debug.WriteLine($"🎮 {message}");
            DiagnosticLog?.Invoke(message);
        }
    }
}
