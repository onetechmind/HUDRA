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

    /// <summary>Adapts a standard WinUI Slider for gamepad value editing.</summary>
    public sealed class SliderEditable : IGamepadValueEditable
    {
        private readonly Slider _slider;

        public SliderEditable(Slider slider) => _slider = slider;

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

        public void OnEditingChanged(bool editing)
        {
            _control.IsSliderActivated = editing;
            System.Diagnostics.Debug.WriteLine($"🎮 Slider {(editing ? "activated" : "deactivated")} for {_control.GetType().Name}");
        }

        public void AdjustValue(int direction) => _control.AdjustSliderValue(direction);
    }
}
