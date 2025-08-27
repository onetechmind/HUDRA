using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace HUDRA.Services.Input
{
    /// <summary>
    /// Provides helper methods for controller-based focus navigation.
    /// </summary>
    public class FocusNavigationService
    {
        public bool NavigateToNextElement()
        {
            var next = FocusManager.TryFindNextFocusableElement(FocusNavigationDirection.Next) as Control;
            if (next != null)
            {
                next.Focus(FocusState.Programmatic);
                UpdateFocusVisualState(next);
                return true;
            }
            return false;
        }

        public bool NavigateToPreviousElement()
        {
            var prev = FocusManager.TryFindNextFocusableElement(FocusNavigationDirection.Previous) as Control;
            if (prev != null)
            {
                prev.Focus(FocusState.Programmatic);
                UpdateFocusVisualState(prev);
                return true;
            }
            return false;
        }

        public Task NavigateToPage(int pageIndex)
        {
            // Actual page navigation is handled by MainWindow via NavigationService.
            return Task.CompletedTask;
        }

        public void UpdateFocusVisualState(FrameworkElement element)
        {
            VisualStateManager.GoToState(element, "ControllerFocused", true);
        }

        public void HandleDirectionalNavigation(HudraAction action)
        {
            var direction = action switch
            {
                HudraAction.NavigateUp => FocusNavigationDirection.Up,
                HudraAction.NavigateDown => FocusNavigationDirection.Down,
                HudraAction.NavigateLeft => FocusNavigationDirection.Left,
                HudraAction.NavigateRight => FocusNavigationDirection.Right,
                _ => FocusNavigationDirection.None
            };

            if (direction != FocusNavigationDirection.None)
            {
                var element = FocusManager.TryMoveFocus(direction) as Control;
                if (element != null)
                {
                    UpdateFocusVisualState(element);
                }
            }
        }
    }
}
