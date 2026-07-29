using HUDRA.Interfaces;
using Microsoft.UI.Xaml.Controls;

namespace HUDRA.Services.GamepadInput
{
    /// <summary>
    /// Pushed while a ComboBox dropdown is open. Up/down move the selection,
    /// A commits it, B cancels and restores the original selection. Works for
    /// any standard ComboBox; an optional IDropdownOwner gets deferred-commit
    /// callbacks (suppress live apply while browsing, apply on A). Chrome
    /// actions fall through to the shell.
    /// </summary>
    public sealed class DropdownScope : IInputScope
    {
        private readonly ComboBox _comboBox;
        private readonly IDropdownOwner? _owner;
        private InputRouter? _router;
        private int _originalIndex;
        private bool _closedHooked;

        public DropdownScope(ComboBox comboBox, IDropdownOwner? owner = null)
        {
            _comboBox = comboBox;
            _owner = owner;
        }

        /// <summary>Legacy IGamepadNavigable controls get their flag-based callbacks adapted.</summary>
        public DropdownScope(ComboBox comboBox, IGamepadNavigable legacyControl)
            : this(comboBox, new LegacyDropdownOwner(legacyControl))
        {
        }

        public string Name => "Dropdown";

        public int Layer => ScopeLayer.Edit;

        /// <summary>
        /// Invalid once the dropdown is no longer open. Both push sites open the
        /// dropdown synchronously before pushing, so this arms immediately.
        /// </summary>
        public bool IsStillValid => _comboBox.IsDropDownOpen;

        public void OnPushed(InputRouter router)
        {
            _router = router;
            _originalIndex = _comboBox.SelectedIndex;

            // The dropdown can also be dismissed by mouse click, Escape or light
            // dismiss. Those paths bypass the gamepad entirely and used to leave
            // this scope stacked, after which d-pad presses silently moved the
            // selection (live-applying settings) with no dropdown on screen.
            _comboBox.DropDownClosed += OnDropDownClosed;
            _closedHooked = true;

            _owner?.OnDropdownOpened(_originalIndex);
            System.Diagnostics.Debug.WriteLine($"🎮 Dropdown opened, original index: {_originalIndex}");
        }

        public void OnPopped()
        {
            if (_closedHooked)
            {
                _comboBox.DropDownClosed -= OnDropDownClosed;
                _closedHooked = false;
            }

            // Popped without commit/cancel (page change): close, keep selection
            if (_comboBox.IsDropDownOpen)
            {
                _comboBox.IsDropDownOpen = false;
            }
            _owner?.OnDropdownClosed();
            System.Diagnostics.Debug.WriteLine("🎮 Dropdown closed");
        }

        private void OnDropDownClosed(object? sender, object e) => _router?.Remove(this);

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
                    _owner?.OnDropdownCommitted(_comboBox);
                    _comboBox.IsDropDownOpen = false;
                    System.Diagnostics.Debug.WriteLine($"🎮 Dropdown A - confirmed selection: {_comboBox.SelectedIndex}");
                    _router?.Remove(this);
                    return true;

                case GamepadAction.Back:
                    if (e.IsRepeat) return true;
                    // Cancel: restore the original selection (suppressed so the
                    // owner doesn't treat the restore as a new selection)
                    _owner?.OnDropdownNavigating();
                    _comboBox.SelectedIndex = _originalIndex;
                    _comboBox.IsDropDownOpen = false;
                    System.Diagnostics.Debug.WriteLine($"🎮 Dropdown B - cancelled, restored index: {_originalIndex}");
                    _router?.Remove(this);
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

            _owner?.OnDropdownNavigating();
            _comboBox.SelectedIndex = newIndex;
        }
    }

    /// <summary>Adapts the IGamepadNavigable ComboBox flags to IDropdownOwner.</summary>
    internal sealed class LegacyDropdownOwner : IDropdownOwner
    {
        private readonly IGamepadNavigable _control;

        public LegacyDropdownOwner(IGamepadNavigable control) => _control = control;

        public void OnDropdownOpened(int originalIndex)
        {
            _control.IsComboBoxOpen = true;
            _control.ComboBoxOriginalIndex = originalIndex;
            _control.IsNavigatingComboBox = false;
        }

        public void OnDropdownNavigating() => _control.IsNavigatingComboBox = true;

        public void OnDropdownCommitted(ComboBox comboBox)
        {
            _control.IsNavigatingComboBox = false;
            _control.ProcessCurrentSelection();
        }

        public void OnDropdownClosed()
        {
            _control.IsNavigatingComboBox = false;
            _control.IsComboBoxOpen = false;
        }
    }
}
