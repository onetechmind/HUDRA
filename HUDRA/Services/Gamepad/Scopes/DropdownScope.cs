using HUDRA.Interfaces;
using Microsoft.UI.Xaml.Controls;

namespace HUDRA.Services.GamepadInput
{
    /// <summary>
    /// Pushed while a ComboBox dropdown is open. Up/down move the selection,
    /// A commits it, B cancels and restores the original selection. Works for
    /// any standard ComboBox; an optional legacy IGamepadNavigable owner gets
    /// its suppression flags driven (IsNavigatingComboBox keeps the owner's
    /// SelectionChanged inert until commit). Chrome actions fall through.
    /// </summary>
    public sealed class DropdownScope : IInputScope
    {
        private readonly ComboBox _comboBox;
        private readonly IGamepadNavigable? _legacyControl;
        private InputRouter? _router;
        private int _originalIndex;

        public DropdownScope(ComboBox comboBox, IGamepadNavigable? legacyControl = null)
        {
            _comboBox = comboBox;
            _legacyControl = legacyControl;
        }

        public string Name => "Dropdown";

        public void OnPushed(InputRouter router)
        {
            _router = router;
            _originalIndex = _comboBox.SelectedIndex;

            if (_legacyControl != null)
            {
                _legacyControl.IsComboBoxOpen = true;
                _legacyControl.ComboBoxOriginalIndex = _originalIndex;
                _legacyControl.IsNavigatingComboBox = false;
            }
            System.Diagnostics.Debug.WriteLine($"🎮 Dropdown opened, original index: {_originalIndex}");
        }

        public void OnPopped()
        {
            // Popped without commit/cancel (page change): close, keep selection
            if (_comboBox.IsDropDownOpen)
            {
                _comboBox.IsDropDownOpen = false;
            }
            if (_legacyControl != null)
            {
                _legacyControl.IsComboBoxOpen = false;
            }
            System.Diagnostics.Debug.WriteLine("🎮 Dropdown closed");
        }

        public bool HandleEvent(in GamepadEvent e)
        {
            switch (e.Action)
            {
                case GamepadAction.NavUp:
                    MoveSelection(-1);
                    return true;

                case GamepadAction.NavDown:
                    MoveSelection(1);
                    return true;

                case GamepadAction.NavLeft:
                case GamepadAction.NavRight:
                case GamepadAction.X:
                case GamepadAction.Y:
                    return true; // blocked while the dropdown is open

                case GamepadAction.Accept:
                    if (e.IsRepeat) return true;
                    // Commit the current selection
                    if (_legacyControl != null)
                    {
                        _legacyControl.IsNavigatingComboBox = false;
                        _legacyControl.ProcessCurrentSelection();
                    }
                    _comboBox.IsDropDownOpen = false;
                    System.Diagnostics.Debug.WriteLine($"🎮 Dropdown A - confirmed selection: {_comboBox.SelectedIndex}");
                    _router?.Pop(this);
                    return true;

                case GamepadAction.Back:
                    if (e.IsRepeat) return true;
                    // Cancel: restore the original selection before closing
                    _comboBox.SelectedIndex = _originalIndex;
                    if (_legacyControl != null)
                    {
                        _legacyControl.IsNavigatingComboBox = false;
                    }
                    _comboBox.IsDropDownOpen = false;
                    System.Diagnostics.Debug.WriteLine($"🎮 Dropdown B - cancelled, restored index: {_originalIndex}");
                    _router?.Pop(this);
                    return true;
            }

            // LB/RB/LT/RT fall through to the shell
            return false;
        }

        private void MoveSelection(int direction)
        {
            if (_comboBox.Items.Count == 0) return;

            int currentIndex = _comboBox.SelectedIndex;
            int newIndex = direction > 0
                ? (currentIndex + 1) % _comboBox.Items.Count
                : currentIndex <= 0 ? _comboBox.Items.Count - 1 : currentIndex - 1;

            // Legacy owners suppress their SelectionChanged while navigating
            if (_legacyControl != null)
            {
                _legacyControl.IsNavigatingComboBox = true;
            }
            _comboBox.SelectedIndex = newIndex;
        }
    }
}
