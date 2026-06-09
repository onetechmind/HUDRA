using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace HUDRA.Services.GamepadInput
{
    /// <summary>
    /// Gamepad activation for standard WinUI controls, so any Button, Slider,
    /// ComboBox, ToggleSwitch, CheckBox or RadioButton marked with
    /// GamepadNavigation.IsEnabled works with zero code-behind. An
    /// IGamepadElementHost ancestor gets first chance, for composite controls
    /// whose inner elements need custom semantics.
    /// </summary>
    public static class ControlAdapters
    {
        /// <summary>Activate the focused element. Returns true if handled.</summary>
        public static bool TryActivate(FrameworkElement element, InputRouter router)
        {
            var host = FindHost(element);
            if (host != null && host.TryActivateElement(element, router))
            {
                return true;
            }

            switch (element)
            {
                case Slider slider:
                    router.Push(new ValueEditScope(new SliderEditable(slider)));
                    return true;

                case ToggleSwitch toggleSwitch:
                    toggleSwitch.IsOn = !toggleSwitch.IsOn;
                    return true;

                case CheckBox checkBox:
                    checkBox.IsChecked = !(checkBox.IsChecked ?? false);
                    return true;

                case RadioButton radioButton:
                    radioButton.IsChecked = true;
                    return true;

                case ComboBox comboBox:
                    if (comboBox.Items.Count == 0) return true;
                    comboBox.IsDropDownOpen = true;
                    router.Push(new DropdownScope(comboBox));
                    return true;

                // ToggleButton before Button: it derives from ButtonBase too
                case ToggleButton toggleButton:
                    toggleButton.IsChecked = !(toggleButton.IsChecked ?? false);
                    return true;

                case ButtonBase button:
                    Invoke(button);
                    return true;
            }

            return false;
        }

        /// <summary>Programmatic click through the automation peer (fires Click/Command).</summary>
        public static void Invoke(ButtonBase button)
        {
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button);
            if (peer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke)
            {
                invoke.Invoke();
            }
            else if (peer?.GetPattern(PatternInterface.Toggle) is IToggleProvider toggle)
            {
                toggle.Toggle();
            }
        }

        private static IGamepadElementHost? FindHost(FrameworkElement element)
        {
            DependencyObject? parent = VisualTreeHelper.GetParent(element);
            while (parent != null)
            {
                if (parent is IGamepadElementHost host) return host;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }
    }
}
