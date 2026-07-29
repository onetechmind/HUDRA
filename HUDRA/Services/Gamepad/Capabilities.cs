using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using HUDRA.Interfaces;

namespace HUDRA.Services.GamepadInput
{
    /// <summary>
    /// Value-editing capability used by ValueEditScope: A enters edit mode,
    /// left/right adjust, A/B exit. Implemented by adapters over standard
    /// sliders and over legacy IGamepadNavigable controls.
    /// </summary>
    public interface IGamepadValueEditable
    {
        void OnEditingChanged(bool editing);
        void AdjustValue(int direction);

        /// <summary>
        /// False once the control being edited has left the visual tree, so the
        /// router can drop an edit scope stranded by a page change. Uses IsLoaded
        /// rather than visibility, so collapsing a containing expander mid-edit
        /// does not abort the edit.
        /// </summary>
        bool IsEditTargetAlive => true;
    }

    /// <summary>
    /// A composite control whose inner elements are individual focus
    /// candidates but need parent-defined activation semantics (e.g. a
    /// ComboBox with deferred commit). Gets first chance at activating an
    /// inner element before the generic adapters.
    /// </summary>
    public interface IGamepadElementHost
    {
        bool TryActivateElement(FrameworkElement element, InputRouter router);
    }

    /// <summary>
    /// Page-level B-button handling when nothing closer consumed it
    /// (e.g. GameSettingsPage navigating back to the Library).
    /// </summary>
    public interface IGamepadBackHandler
    {
        bool HandleBack();
    }

    /// <summary>
    /// Optional callbacks for a control that owns a ComboBox driven by
    /// DropdownScope, enabling deferred commit: the owner suppresses its
    /// SelectionChanged handling while the user browses items and only
    /// applies the selection on A.
    /// </summary>
    public interface IDropdownOwner
    {
        void OnDropdownOpened(int originalIndex);
        void OnDropdownNavigating();
        void OnDropdownCommitted(ComboBox comboBox);
        void OnDropdownClosed();
    }

    /// <summary>Adapts a standard WinUI Slider for gamepad value editing.</summary>
    public sealed class SliderEditable : IGamepadValueEditable
    {
        private readonly Slider _slider;

        public SliderEditable(Slider slider) => _slider = slider;

        public bool IsEditTargetAlive
        {
            get
            {
                try { return _slider.IsLoaded && _slider.XamlRoot != null; }
                catch { return false; }
            }
        }

        public void OnEditingChanged(bool editing) { /* edit visual comes from FocusIndicatorLayer */ }

        public void AdjustValue(int direction)
        {
            double step = _slider.SmallChange > 0 ? _slider.SmallChange : 1;
            double next = _slider.Value + direction * step;
            _slider.Value = System.Math.Clamp(next, _slider.Minimum, _slider.Maximum);
        }
    }

    /// <summary>Adapts a legacy IGamepadNavigable slider control for ValueEditScope.</summary>
    public sealed class NavigableSliderEditable : IGamepadValueEditable
    {
        private readonly IGamepadNavigable _control;

        public NavigableSliderEditable(IGamepadNavigable control) => _control = control;

        public bool IsEditTargetAlive
        {
            get
            {
                try { return _control is not FrameworkElement element || element.IsLoaded; }
                catch { return false; }
            }
        }

        public void OnEditingChanged(bool editing)
        {
            _control.IsSliderActivated = editing;
            System.Diagnostics.Debug.WriteLine($"🎮 Slider {(editing ? "activated" : "deactivated")} for {_control.GetType().Name}");
        }

        public void AdjustValue(int direction) => _control.AdjustSliderValue(direction);
    }
}
