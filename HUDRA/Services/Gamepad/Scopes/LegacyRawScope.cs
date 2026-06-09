namespace HUDRA.Services.GamepadInput
{
    /// <summary>
    /// Transitional scope for pages that consume raw gamepad readings directly
    /// (LibraryPage). Black-holes the semantic events the page handles itself
    /// (directional, A/B/X/Y) - the page receives them through the raw reading
    /// forwarding instead - while letting chrome actions (LB/RB/LT/RT) and
    /// accept/back on a navbar selection fall through to the shell.
    ///
    /// Removed in Phase 4 when the Library grid becomes a real scope.
    /// </summary>
    public sealed class LegacyRawScope : IInputScope
    {
        private readonly ShellScope _shell;

        public LegacyRawScope(ShellScope shell)
        {
            _shell = shell;
        }

        public string Name => "LegacyRaw";

        public bool HandleEvent(in GamepadEvent e)
        {
            switch (e.Action)
            {
                case GamepadAction.LB:
                case GamepadAction.RB:
                case GamepadAction.LT:
                case GamepadAction.RT:
                    return false; // shell handles chrome

                case GamepadAction.Accept:
                case GamepadAction.Back:
                    // With a navbar selection active the shell owns A/B
                    return !_shell.HasNavbarSelection;

                default:
                    return true; // page consumes via raw readings
            }
        }
    }
}
