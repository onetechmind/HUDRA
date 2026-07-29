namespace HUDRA.Services.GamepadInput
{
    /// <summary>
    /// Default page-level input: directional focus movement plus activate/back
    /// on the focused element. Sits directly above the shell; chrome actions
    /// (LB/RB/LT/RT) and accept/back while a navbar button is selected fall
    /// through to the shell.
    /// </summary>
    public sealed class PageScope : IInputScope
    {
        private readonly Services.GamepadNavigationService _service;
        private readonly ShellScope _shell;

        public PageScope(Services.GamepadNavigationService service, ShellScope shell)
        {
            _service = service;
            _shell = shell;
        }

        public string Name => "Page";

        public int Layer => ScopeLayer.Page;

        // Right stick smoothly scrolls the page (per-tick frames, ~60/s).
        // Stick up = positive Y = scroll up.
        private const double ScrollPixelsPerFrame = 18.0;

        public void OnStickFrame(in GamepadStickFrame frame)
        {
            var scrollViewer = _service.ContentScrollViewer;
            if (scrollViewer == null || frame.RightStickY == 0) return;

            double target = scrollViewer.VerticalOffset - frame.RightStickY * ScrollPixelsPerFrame;
            scrollViewer.ChangeView(null, target, null, disableAnimation: true);
        }

        public bool HandleEvent(in GamepadEvent e)
        {
            switch (e.Action)
            {
                case GamepadAction.NavUp:
                case GamepadAction.NavDown:
                case GamepadAction.NavLeft:
                case GamepadAction.NavRight:
                    // D-pad/analog use dismisses any navbar selection
                    _shell.ClearNavbarSelection();
                    _service.HandleNavigationAction(e.Action switch
                    {
                        GamepadAction.NavUp => Interfaces.GamepadNavigationAction.Up,
                        GamepadAction.NavDown => Interfaces.GamepadNavigationAction.Down,
                        GamepadAction.NavLeft => Interfaces.GamepadNavigationAction.Left,
                        _ => Interfaces.GamepadNavigationAction.Right
                    });
                    return true;

                case GamepadAction.Accept when !_shell.HasNavbarSelection:
                    if (e.IsRepeat) return true;
                    _service.InputReader.PulseHaptics();
                    _service.HandleNavigationAction(Interfaces.GamepadNavigationAction.Activate);
                    return true;

                case GamepadAction.Back when !_shell.HasNavbarSelection:
                    if (e.IsRepeat) return true;
                    _service.HandleNavigationAction(Interfaces.GamepadNavigationAction.Back);
                    return true;
            }

            return false;
        }
    }
}
