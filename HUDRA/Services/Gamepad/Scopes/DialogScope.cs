using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace HUDRA.Services.GamepadInput
{
    /// <summary>
    /// Exclusive input while a ContentDialog is open. D-pad left/right moves
    /// real WinUI focus between the dialog's visible template buttons
    /// (Primary/Secondary/Close), A invokes the focused button, B cancels.
    /// Initial focus follows ContentDialog.DefaultButton, so A always does
    /// what the visible focus says - including "safe default" dialogs whose
    /// default is the close button.
    /// </summary>
    public sealed class DialogScope : IInputScope
    {
        private readonly ContentDialog _dialog;
        private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;
        private readonly List<Button> _buttons = new();
        private int _focusedIndex = -1;
        private bool _openedHooked;

        public DialogScope(ContentDialog dialog, Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue)
        {
            _dialog = dialog;
            _dispatcherQueue = dispatcherQueue;
        }

        public string Name => "Dialog";

        public ContentDialog Dialog => _dialog;

        public void OnPushed(InputRouter router)
        {
            // The template buttons only exist once the dialog has opened. If it
            // is already open (safety-net push), resolve them now; otherwise
            // wait for Opened.
            if (!TryInitializeButtons())
            {
                _dialog.Opened += OnDialogOpened;
                _openedHooked = true;
            }
        }

        public void OnPopped()
        {
            if (_openedHooked)
            {
                _dialog.Opened -= OnDialogOpened;
                _openedHooked = false;
            }
        }

        private void OnDialogOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
        {
            TryInitializeButtons();
        }

        public bool HandleEvent(in GamepadEvent e)
        {
            switch (e.Action)
            {
                case GamepadAction.NavLeft:
                    MoveFocus(-1);
                    return true;

                case GamepadAction.NavRight:
                    MoveFocus(1);
                    return true;

                case GamepadAction.Accept:
                    if (e.IsRepeat) return true;
                    System.Diagnostics.Debug.WriteLine("🎮 A button pressed - invoking focused dialog button");
                    _dispatcherQueue.TryEnqueue(InvokeFocusedButton);
                    return true;

                case GamepadAction.Back:
                    if (e.IsRepeat) return true;
                    System.Diagnostics.Debug.WriteLine("🎮 B button pressed - triggering dialog cancel");
                    // NOTE: must be a lambda. Passing the projected WinRT method
                    // group (_dialog.Hide) as the dispatcher delegate causes an
                    // InvalidCastException inside WinRT.Runtime on invoke.
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        try
                        {
                            _dialog.Hide();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"🎮 Dialog Hide failed: {ex.Message}");
                        }
                    });
                    return true;
            }

            // A dialog owns ALL input while open
            return true;
        }

        private bool TryInitializeButtons()
        {
            _buttons.Clear();

            // Template part order matches the visual left-to-right order
            AddButtonIfVisible("PrimaryButton");
            AddButtonIfVisible("SecondaryButton");
            AddButtonIfVisible("CloseButton");

            if (_buttons.Count == 0) return false;

            _focusedIndex = DefaultButtonIndex();
            FocusCurrentButton();
            return true;
        }

        private void AddButtonIfVisible(string templateName)
        {
            var button = FindButtonByName(_dialog, templateName);
            if (button != null && button.Visibility == Visibility.Visible)
            {
                _buttons.Add(button);
            }
        }

        private int DefaultButtonIndex()
        {
            string? defaultName = _dialog.DefaultButton switch
            {
                ContentDialogButton.Primary => "PrimaryButton",
                ContentDialogButton.Secondary => "SecondaryButton",
                ContentDialogButton.Close => "CloseButton",
                _ => null
            };

            if (defaultName != null)
            {
                int index = _buttons.FindIndex(b => b.Name == defaultName);
                if (index >= 0) return index;
            }
            return 0;
        }

        private void MoveFocus(int direction)
        {
            if (_buttons.Count == 0 && !TryInitializeButtons()) return;

            int next = Math.Clamp(_focusedIndex + direction, 0, _buttons.Count - 1);
            if (next == _focusedIndex) return;

            _focusedIndex = next;
            FocusCurrentButton();
        }

        private void FocusCurrentButton()
        {
            if (_focusedIndex < 0 || _focusedIndex >= _buttons.Count) return;

            // Real WinUI focus is safe inside a dialog (the dialog owns focus
            // anyway) and gives us the system focus visual for free
            _buttons[_focusedIndex].Focus(FocusState.Keyboard);
        }

        private void InvokeFocusedButton()
        {
            try
            {
                Button? target = _focusedIndex >= 0 && _focusedIndex < _buttons.Count
                    ? _buttons[_focusedIndex]
                    : null;

                // Buttons may not have resolved yet (A pressed before Opened
                // completed) - fall back to the primary button
                target ??= FindButtonByName(_dialog, "PrimaryButton");

                if (target != null)
                {
                    var peer = new ButtonAutomationPeer(target);
                    var invokeProvider = peer.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
                    invokeProvider?.Invoke();
                    System.Diagnostics.Debug.WriteLine($"🎮 Dialog button '{target.Name}' invoked via automation");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("🎮 Warning: no dialog button to invoke");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"🎮 Error invoking dialog button: {ex.Message}");
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
