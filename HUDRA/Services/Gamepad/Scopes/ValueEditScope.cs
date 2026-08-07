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

        // Hold-to-accelerate state: consecutive repeats in the SAME direction.
        // A fresh press or a direction change resets the ramp.
        private GamepadAction _repeatAction;
        private int _repeatStreak;

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
                    Adjust(-1, in e);
                    return true;

                case GamepadAction.NavRight:
                    Adjust(1, in e);
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

        /// <summary>
        /// Apply one adjustment event, accelerating while the direction is held:
        /// repeats arrive every 110 ms (after the 400 ms initial delay), so the
        /// ramp reaches x2 after ~1.3 s, x4 after ~2.2 s and x8 after ~3 s of
        /// holding. Without it, crossing a wide range (the 0-120 FPS slider) at
        /// 1 step per repeat took over 13 seconds. The multiplier is applied as
        /// N unit steps rather than one big step, so clamping at the range ends
        /// stays exact and legacy sign-based AdjustSliderValue implementations
        /// accelerate identically.
        /// </summary>
        private void Adjust(int direction, in GamepadEvent e)
        {
            if (!e.IsRepeat || e.Action != _repeatAction)
            {
                _repeatAction = e.Action;
                _repeatStreak = 0;
            }
            else
            {
                _repeatStreak++;
            }

            int steps = HoldRamp.Multiplier(_repeatStreak);

            for (int i = 0; i < steps; i++)
            {
                _editable.AdjustValue(direction);
            }
        }
    }
}
