using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace HUDRA.Services.GamepadInput
{
    /// <summary>
    /// Exclusive input while a ContentDialog is open: A invokes the primary
    /// button, B closes/cancels, everything else is swallowed. Pushed/popped
    /// around ShowAsync by the dialog helper.
    /// </summary>
    public sealed class DialogScope : IInputScope
    {
        private readonly ContentDialog _dialog;
        private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;

        public DialogScope(ContentDialog dialog, Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue)
        {
            _dialog = dialog;
            _dispatcherQueue = dispatcherQueue;
        }

        public string Name => "Dialog";

        public ContentDialog Dialog => _dialog;

        public bool HandleEvent(in GamepadEvent e)
        {
            if (e.IsRepeat) return true;

            if (e.Action == GamepadAction.Accept)
            {
                System.Diagnostics.Debug.WriteLine("🎮 A button pressed - triggering dialog primary action");
                _dispatcherQueue.TryEnqueue(() => TriggerPrimaryButton(_dialog));
            }
            else if (e.Action == GamepadAction.Back)
            {
                System.Diagnostics.Debug.WriteLine("🎮 B button pressed - triggering dialog cancel");
                _dispatcherQueue.TryEnqueue(_dialog.Hide);
            }

            // A dialog owns ALL input while open
            return true;
        }

        private static void TriggerPrimaryButton(ContentDialog dialog)
        {
            try
            {
                var primaryButton = FindButtonByName(dialog, "PrimaryButton");
                if (primaryButton != null)
                {
                    var peer = new ButtonAutomationPeer(primaryButton);
                    var invokeProvider = peer.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
                    invokeProvider?.Invoke();
                    System.Diagnostics.Debug.WriteLine("🎮 Primary button invoked via automation");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("🎮 Warning: Could not find primary button in dialog");
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"🎮 Error triggering dialog primary button: {ex.Message}");
            }
        }

        private static Button? FindButtonByName(DependencyObject parent, string name)
        {
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                // ContentDialog names its template buttons "PrimaryButton",
                // "SecondaryButton" and "CloseButton"
                if (child is Button button && button.Name == name)
                {
                    return button;
                }

                var result = FindButtonByName(child, name);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }
    }
}
