namespace HUDRA.Services.GamepadInput
{
    /// <summary>
    /// One layer of the gamepad input stack. The router dispatches each event to
    /// the top scope first; a scope either consumes the event (true) or lets it
    /// fall through to the scope below (false).
    ///
    /// Examples: the shell (page cycling, navbar) sits at the bottom; the active
    /// page above it; transient scopes (slider editing, open dropdown, modal
    /// dialog) are pushed on top. This replaces the old web of mutually
    /// exclusive boolean mode flags.
    ///
    /// A scope must never be able to wedge input by outliving its context, so
    /// every transient scope also declares <see cref="IsStillValid"/>; the
    /// router drops scopes whose context has gone away.
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

        /// <summary>
        /// Insertion band for this scope. See <see cref="ScopeLayer"/>.
        /// </summary>
        int Layer => ScopeLayer.Page;

        /// <summary>
        /// False once this scope's context is gone (dialog closed, dropdown
        /// closed, page navigated away, edit target detached) — the router then
        /// removes it, so a missed explicit removal self-heals on the next input
        /// instead of wedging the app until restart.
        ///
        /// Contract: must be pure, cheap and non-throwing. It MAY return false
        /// while the context is still opening (a dialog before its template is
        /// realized); the router grants an arming grace for that, so scopes do
        /// not need to special-case it. When in doubt, return true — a scope
        /// wrongly kept alive is a missed heal, a scope wrongly reaped aborts a
        /// live interaction.
        /// </summary>
        bool IsStillValid => true;

        /// <summary>
        /// True if this scope renders its own focus visuals and does not want the
        /// service's auto-focus behaviour on gamepad activation (the game library
        /// uses real WinUI focus on its tiles). Note this only suppresses
        /// auto-focus — gamepad mode still activates, because gating activation on
        /// the presence of a page scope is what previously made a leaked scope
        /// permanently disable the focus ring everywhere.
        /// </summary>
        bool OwnsFocusVisuals => false;
    }
}
