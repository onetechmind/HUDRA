using HUDRA.Interfaces;
using Microsoft.UI.Xaml.Controls;

namespace HUDRA.Services.GamepadInput
{
    /// <summary>
    /// Pushed while a ComboBox dropdown is open. Up/down move the highlighted
    /// item without applying it (the control's IsNavigatingComboBox flag keeps
    /// SelectionChanged inert), A commits the selection, B cancels and restores
    /// the original selection. Chrome actions fall through to the shell.
    /// </summary>
    public sealed class DropdownScope : IInputScope
    {
        private readonly IGamepadNavigable _control;
        private InputRouter? _router;

        public DropdownScope(IGamepadNavigable control)
        {
            _control = control;
        }

        public string Name => "Dropdown";

        public void OnPushed(InputRouter router)
        {
            _router = router;
            _control.IsComboBoxOpen = true;

            // Remember the original selection for cancellation
            var comboBox = _control.GetFocusedComboBox();
            if (comboBox != null)
            {
                _control.ComboBoxOriginalIndex = comboBox.SelectedIndex;
                _control.IsNavigatingComboBox = false;
            }
            System.Diagnostics.Debug.WriteLine($"🎮 ComboBox activated for {_control.GetType().Name}, original index: {_control.ComboBoxOriginalIndex}");
        }

        public void OnPopped()
        {
            // If we are popped without an explicit commit/cancel (e.g. page
            // change), close the dropdown and leave the current selection as-is
            var comboBox = _control.GetFocusedComboBox();
            if (comboBox != null && comboBox.IsDropDownOpen)
            {
                comboBox.IsDropDownOpen = false;
            }
            _control.IsComboBoxOpen = false;
            System.Diagnostics.Debug.WriteLine($"🎮 ComboBox deactivated for {_control.GetType().Name}");
        }

        public bool HandleEvent(in GamepadEvent e)
        {
            var comboBox = _control.GetFocusedComboBox();
            if (comboBox == null)
            {
                if (!e.IsRepeat)
                {
                    _router?.Pop(this);
                }
                return true;
            }

            switch (e.Action)
            {
                case GamepadAction.NavUp:
                    MoveSelection(comboBox, -1);
                    return true;

                case GamepadAction.NavDown:
                    MoveSelection(comboBox, 1);
                    return true;

                case GamepadAction.NavLeft:
                case GamepadAction.NavRight:
                case GamepadAction.X:
                case GamepadAction.Y:
                    return true; // blocked while the dropdown is open

                case GamepadAction.Accept:
                    if (e.IsRepeat) return true;
                    // Commit: clear navigation flag and process the selection
                    _control.IsNavigatingComboBox = false;
                    _control.ProcessCurrentSelection();
                    comboBox.IsDropDownOpen = false;
                    System.Diagnostics.Debug.WriteLine($"🎮 ComboBox A button - confirmed selection: {comboBox.SelectedIndex}");
                    _router?.Pop(this);
                    return true;

                case GamepadAction.Back:
                    if (e.IsRepeat) return true;
                    // Cancel: restore the original selection before closing
                    comboBox.SelectedIndex = _control.ComboBoxOriginalIndex;
                    _control.IsNavigatingComboBox = false;
                    comboBox.IsDropDownOpen = false;
                    System.Diagnostics.Debug.WriteLine($"🎮 ComboBox B button - cancelled, restored to index: {_control.ComboBoxOriginalIndex}");
                    _router?.Pop(this);
                    return true;
            }

            // LB/RB/LT/RT fall through to the shell
            return false;
        }

        private void MoveSelection(ComboBox comboBox, int direction)
        {
            if (comboBox.Items.Count == 0) return;

            int currentIndex = comboBox.SelectedIndex;
            int newIndex = direction > 0
                ? (currentIndex + 1) % comboBox.Items.Count
                : currentIndex <= 0 ? comboBox.Items.Count - 1 : currentIndex - 1;

            // Navigation flag prevents SelectionChanged from applying the change
            _control.IsNavigatingComboBox = true;
            comboBox.SelectedIndex = newIndex;
            System.Diagnostics.Debug.WriteLine($"🎮 ComboBox navigated to item {newIndex} (direction: {direction})");
        }
    }
}
