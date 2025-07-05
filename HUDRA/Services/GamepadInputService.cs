// HUDRA/Services/GamepadInputService.cs
using Microsoft.UI.Xaml;
using System;
using Windows.Gaming.Input;
using HUDRA.Configuration;

namespace HUDRA.Services
{
    public class GamepadInputService : IDisposable
    {
        public event EventHandler<GamepadNavigationEventArgs>? NavigationChanged;
        public event EventHandler<GamepadActionEventArgs>? ActionPressed;

        private readonly DispatcherTimer _gamepadTimer;
        private bool _gamepadLeftPressed = false;
        private bool _gamepadRightPressed = false;
        private bool _gamepadUpPressed = false;
        private bool _gamepadDownPressed = false;
        private bool _gamepadAPressed = false;
        private bool _gamepadBPressed = false;

        private int _selectedControlIndex = 0;
        private bool _isComboBoxPopupOpen = false;

        public int SelectedControlIndex
        {
            get => _selectedControlIndex;
            set => _selectedControlIndex = Math.Max(0, Math.Min(HudraSettings.TOTAL_CONTROLS - 1, value));
        }

        public bool IsComboBoxPopupOpen
        {
            get => _isComboBoxPopupOpen;
            set => _isComboBoxPopupOpen = value;
        }

        public GamepadInputService()
        {
            _gamepadTimer = new DispatcherTimer { Interval = HudraSettings.GAMEPAD_POLL_INTERVAL };
            _gamepadTimer.Tick += GamepadTimer_Tick;
            _gamepadTimer.Start();
        }

        private void GamepadTimer_Tick(object sender, object e)
        {
            var gamepads = Gamepad.Gamepads;
            if (gamepads.Count == 0) return;

            var gamepad = gamepads[0];
            var reading = gamepad.GetCurrentReading();

            bool upPressed = (reading.Buttons & GamepadButtons.DPadUp) != 0;
            bool downPressed = (reading.Buttons & GamepadButtons.DPadDown) != 0;
            bool leftPressed = (reading.Buttons & GamepadButtons.DPadLeft) != 0;
            bool rightPressed = (reading.Buttons & GamepadButtons.DPadRight) != 0;
            bool aPressed = (reading.Buttons & GamepadButtons.A) != 0;
            bool bPressed = (reading.Buttons & GamepadButtons.B) != 0;

            // Handle popup navigation
            if (_isComboBoxPopupOpen)
            {
                HandlePopupNavigation(upPressed, downPressed, aPressed, bPressed);
                UpdateButtonStates(upPressed, downPressed, leftPressed, rightPressed, aPressed, bPressed);
                return;
            }

            // Handle main UI navigation
            HandleMainNavigation(upPressed, downPressed, leftPressed, rightPressed, aPressed, bPressed);
            UpdateButtonStates(upPressed, downPressed, leftPressed, rightPressed, aPressed, bPressed);
        }

        private void HandlePopupNavigation(bool upPressed, bool downPressed, bool aPressed, bool bPressed)
        {
            if (upPressed && !_gamepadUpPressed)
            {
                ActionPressed?.Invoke(this, new GamepadActionEventArgs(GamepadAction.PopupUp));
            }
            else if (downPressed && !_gamepadDownPressed)
            {
                ActionPressed?.Invoke(this, new GamepadActionEventArgs(GamepadAction.PopupDown));
            }
            else if (aPressed && !_gamepadAPressed)
            {
                ActionPressed?.Invoke(this, new GamepadActionEventArgs(GamepadAction.PopupSelect));
            }
            else if (bPressed && !_gamepadBPressed)
            {
                ActionPressed?.Invoke(this, new GamepadActionEventArgs(GamepadAction.PopupCancel));
            }
        }

        private void HandleMainNavigation(bool upPressed, bool downPressed, bool leftPressed, bool rightPressed, bool aPressed, bool bPressed)
        {
            // Vertical navigation (control switching)
            if (upPressed && !_gamepadUpPressed)
            {
                _selectedControlIndex = (_selectedControlIndex - 1 + HudraSettings.TOTAL_CONTROLS) % HudraSettings.TOTAL_CONTROLS;
                NavigationChanged?.Invoke(this, new GamepadNavigationEventArgs(_selectedControlIndex));
            }
            else if (downPressed && !_gamepadDownPressed)
            {
                _selectedControlIndex = (_selectedControlIndex + 1) % HudraSettings.TOTAL_CONTROLS;
                NavigationChanged?.Invoke(this, new GamepadNavigationEventArgs(_selectedControlIndex));
            }

            // Horizontal navigation (value changes)
            if (leftPressed && !_gamepadLeftPressed)
            {
                ActionPressed?.Invoke(this, new GamepadActionEventArgs(GamepadAction.DecrementValue, _selectedControlIndex));
            }
            else if (rightPressed && !_gamepadRightPressed)
            {
                ActionPressed?.Invoke(this, new GamepadActionEventArgs(GamepadAction.IncrementValue, _selectedControlIndex));
            }

            // A button activation
            if (aPressed && !_gamepadAPressed)
            {
                ActionPressed?.Invoke(this, new GamepadActionEventArgs(GamepadAction.Activate, _selectedControlIndex));
            }
        }

        private void UpdateButtonStates(bool up, bool down, bool left, bool right, bool a, bool b)
        {
            _gamepadUpPressed = up;
            _gamepadDownPressed = down;
            _gamepadLeftPressed = left;
            _gamepadRightPressed = right;
            _gamepadAPressed = a;
            _gamepadBPressed = b;
        }

        public void Dispose()
        {
            _gamepadTimer?.Stop();
        }
    }

    public enum GamepadAction
    {
        IncrementValue,
        DecrementValue,
        Activate,
        PopupUp,
        PopupDown,
        PopupSelect,
        PopupCancel
    }

    public class GamepadNavigationEventArgs : EventArgs
    {
        public int SelectedControlIndex { get; }

        public GamepadNavigationEventArgs(int selectedControlIndex)
        {
            SelectedControlIndex = selectedControlIndex;
        }
    }
     
    public class GamepadActionEventArgs : EventArgs
    {
        public GamepadAction Action { get; }
        public int ControlIndex { get; }

        public GamepadActionEventArgs(GamepadAction action, int controlIndex = -1)
        {
            Action = action;
            ControlIndex = controlIndex;
        }
    }
}