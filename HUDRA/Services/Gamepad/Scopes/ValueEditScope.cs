namespace HUDRA.Services.GamepadInput
{
    /// <summary>
    /// Pushed while a slider-style control is in edit mode (entered with A).
    /// Left/right adjust the value (repeats allowed), A or B exits. Chrome
    /// actions fall through to the shell; if the page changes underneath, the
    /// router pops this scope and edit state is cleaned up in OnPopped.
    /// </summary>
    public sealed class ValueEditScope : IInputScope
    {
        private readonly IGamepadValueEditable _editable;
        private InputRouter? _router;

        public ValueEditScope(IGamepadValueEditable editable)
        {
            _editable = editable;
        }

        public string Name => "ValueEdit";

        public int Layer => ScopeLayer.Edit;

        /// <summary>Invalid once the control being edited leaves the visual tree.</summary>
        public bool IsStillValid => _editable.IsEditTargetAlive;

        public void OnPushed(InputRouter router)
        {
            _router = router;
            _editable.OnEditingChanged(true);
        }

        public void OnPopped()
        {
            _editable.OnEditingChanged(false);
        }

        public bool HandleEvent(in GamepadEvent e)
        {
            switch (e.Action)
            {
                case GamepadAction.NavLeft:
                    _editable.AdjustValue(-1);
                    return true;

                case GamepadAction.NavRight:
                    _editable.AdjustValue(1);
                    return true;

                case GamepadAction.NavUp:
                case GamepadAction.NavDown:
                case GamepadAction.X:
                case GamepadAction.Y:
                    return true; // blocked while editing

                case GamepadAction.Accept:
                case GamepadAction.Back:
                    if (!e.IsRepeat)
                    {
                        _router?.Remove(this);
                    }
                    return true;
            }

            // LB/RB/LT/RT fall through to the shell
            return false;
        }
    }
}
