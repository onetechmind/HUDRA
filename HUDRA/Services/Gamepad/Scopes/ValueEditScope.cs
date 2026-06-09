using HUDRA.Interfaces;

namespace HUDRA.Services.GamepadInput
{
    /// <summary>
    /// Pushed while a slider-style control is in edit mode (entered with A).
    /// Left/right adjust the value (repeats allowed), A or B exits. Chrome
    /// actions fall through to the shell; if the page changes underneath, the
    /// router pops this scope and the control's edit state is cleaned up in
    /// OnPopped.
    /// </summary>
    public sealed class ValueEditScope : IInputScope
    {
        private readonly IGamepadNavigable _control;
        private InputRouter? _router;

        public ValueEditScope(IGamepadNavigable control)
        {
            _control = control;
        }

        public string Name => "ValueEdit";

        public void OnPushed(InputRouter router)
        {
            _router = router;
            _control.IsSliderActivated = true;
            System.Diagnostics.Debug.WriteLine($"🎮 Slider activated for {_control.GetType().Name}");
        }

        public void OnPopped()
        {
            _control.IsSliderActivated = false;
            System.Diagnostics.Debug.WriteLine($"🎮 Slider deactivated for {_control.GetType().Name}");
        }

        public bool HandleEvent(in GamepadEvent e)
        {
            switch (e.Action)
            {
                case GamepadAction.NavLeft:
                    _control.AdjustSliderValue(-1);
                    return true;

                case GamepadAction.NavRight:
                    _control.AdjustSliderValue(1);
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
                        _router?.Pop(this);
                    }
                    return true;
            }

            // LB/RB/LT/RT fall through to the shell
            return false;
        }
    }
}
