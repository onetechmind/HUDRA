using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace HUDRA.Services.GamepadInput
{
    /// <summary>
    /// Bottom of the input stack - always present. Owns app chrome input:
    /// LB/RB page cycling and LT/RT navbar button selection (with A to invoke
    /// and B to clear). Because unconsumed events fall through to this scope,
    /// chrome input keeps working no matter what page/editing scope is active,
    /// without any special-casing above.
    /// </summary>
    public sealed class ShellScope : IInputScope
    {
        private readonly Services.GamepadNavigationService _service;
        private List<Button> _navbarButtons = new();
        private int? _selectedIndex = null; // null = no selection
        private Button? _selectedButton = null;

        public ShellScope(Services.GamepadNavigationService service)
        {
            _service = service;
        }

        public string Name => "Shell";

        public int Layer => ScopeLayer.Shell;

        public bool HasNavbarSelection => _selectedIndex.HasValue && _selectedButton != null;

        public void RegisterNavbarButtons(List<Button> buttons)
        {
            _navbarButtons = buttons ?? new List<Button>();
            System.Diagnostics.Debug.WriteLine($"🎮 Registered {_navbarButtons.Count} navbar buttons");
        }

        public bool HandleEvent(in GamepadEvent e)
        {
            if (e.IsRepeat) return false; // chrome actions never repeat

            switch (e.Action)
            {
                case GamepadAction.LB:
                    ClearNavbarSelection();
                    _service.RaisePageNavigationRequested(Services.GamepadPageDirection.Previous);
                    _service.InputReader.PulseHaptics();
                    return true;

                case GamepadAction.RB:
                    ClearNavbarSelection();
                    _service.RaisePageNavigationRequested(Services.GamepadPageDirection.Next);
                    _service.InputReader.PulseHaptics();
                    return true;

                case GamepadAction.LT:
                    CycleNavbarSelection(-1);
                    _service.InputReader.PulseHaptics();
                    return true;

                case GamepadAction.RT:
                    CycleNavbarSelection(1);
                    _service.InputReader.PulseHaptics();
                    return true;

                case GamepadAction.Accept when HasNavbarSelection:
                    _service.InputReader.PulseHaptics();
                    InvokeSelectedNavbarButton();
                    return true;

                case GamepadAction.Back when HasNavbarSelection:
                    ClearNavbarSelection();
                    return true;
            }

            return false;
        }

        // Cycle through visible navbar buttons with LT/RT
        private void CycleNavbarSelection(int direction)
        {
            if (_navbarButtons.Count == 0) return;

            var visibleButtons = _navbarButtons
                .Select((button, index) => new { button, index })
                .Where(x => x.button.Visibility == Visibility.Visible)
                .ToList();

            if (visibleButtons.Count == 0)
            {
                ClearNavbarSelection();
                return;
            }

            // If only one visible button, just select it and don't cycle
            if (visibleButtons.Count == 1)
            {
                int singleIndex = visibleButtons[0].index;
                if (_selectedIndex == singleIndex) return;
                _selectedIndex = singleIndex;
                SetNavbarButtonSelection(_navbarButtons[singleIndex]);
                return;
            }

            int newIndex;
            if (!_selectedIndex.HasValue)
            {
                // No selection yet: always start from the top-most visible button
                newIndex = visibleButtons[0].index;
            }
            else
            {
                int currentVisibleIndex = visibleButtons.FindIndex(x => x.index == _selectedIndex.Value);
                if (currentVisibleIndex == -1)
                {
                    newIndex = visibleButtons[0].index;
                }
                else
                {
                    int nextVisibleIndex = currentVisibleIndex + direction;
                    if (nextVisibleIndex < 0) nextVisibleIndex = visibleButtons.Count - 1;
                    else if (nextVisibleIndex >= visibleButtons.Count) nextVisibleIndex = 0;
                    newIndex = visibleButtons[nextVisibleIndex].index;
                }
            }

            _selectedIndex = newIndex;
            SetNavbarButtonSelection(_navbarButtons[newIndex]);
        }

        private void SetNavbarButtonSelection(Button button)
        {
            try
            {
                // Clear previous selection border
                if (_selectedButton != null && _selectedButton != button)
                {
                    _selectedButton.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    _selectedButton.BorderThickness = new Thickness(0);
                }

                // Clear main app focus so focus borders disappear from page controls
                _service.ClearFocus();

                _selectedButton = button;
                button.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.DarkViolet);
                button.BorderThickness = new Thickness(3);

                // DON'T call Focus() - it can cause WinUI to add its own focus visual
                // creating a "double border" effect. We only need our custom border.
                button.UpdateLayout();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"🎮 ERROR setting navbar button selection: {ex.Message}");
            }
        }

        public void ClearNavbarSelection()
        {
            try
            {
                if (_selectedButton != null)
                {
                    _selectedButton.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                    _selectedButton.BorderThickness = new Thickness(0);
                    _selectedButton = null;
                }
                _selectedIndex = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"🎮 ERROR clearing navbar button selection: {ex.Message}");
            }
        }

        private void InvokeSelectedNavbarButton()
        {
            if (_selectedButton == null) return;

            // Programmatically click the button using UI Automation
            var peer = new ButtonAutomationPeer(_selectedButton);
            var invokeProvider = peer.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
            invokeProvider?.Invoke();

            ClearNavbarSelection();
        }
    }
}
