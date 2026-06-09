namespace HUDRA.Services.GamepadInput
{
    /// <summary>
    /// One layer of the gamepad input stack. The router dispatches each event to
    /// the top scope first; a scope either consumes the event (true) or lets it
    /// fall through to the scope below (false).
    ///
    /// Examples: the shell (page cycling, navbar) sits at the bottom; the active
    /// page above it; transient scopes (slider editing, open dropdown, modal
    /// dialog) are pushed on top while they are active. This replaces the old
    /// web of mutually exclusive boolean mode flags.
    /// </summary>
    public interface IInputScope
    {
        /// <summary>Diagnostic name shown in debug logging.</summary>
        string Name { get; }

        /// <summary>Handle a semantic event. Return true to consume it.</summary>
        bool HandleEvent(in GamepadEvent e);

        /// <summary>Per-tick analog input (right stick) for scroll consumers.</summary>
        void OnStickFrame(in GamepadStickFrame frame) { }

        /// <summary>Called when this scope is pushed onto the router.</summary>
        void OnPushed(InputRouter router) { }

        /// <summary>Called when this scope is removed from the router.</summary>
        void OnPopped() { }
    }
}
