using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace HUDRA.Services.Input
{
    public class FocusNavigationService
    {
        public XamlRoot? XamlRoot { get; set; }

        public void HandleDirectionalNavigation(FocusNavigationDirection direction)
        {
            FocusManager.TryMoveFocus(direction);
            var focusedElement = FocusManager.GetFocusedElement(XamlRoot);
            if (focusedElement is FrameworkElement frameworkElement)
            {
                VisualStateManager.GoToState(frameworkElement, "Focused", true);
            }
        }

        public object? FindNextFocusableElement(FocusNavigationDirection direction)
        {
            return FocusManager.FindNextFocusableElement(direction);
        }
    }
}
