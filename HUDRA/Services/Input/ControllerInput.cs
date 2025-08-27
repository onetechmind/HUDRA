using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;

namespace HUDRA.Services.Input
{
    /// <summary>
    /// Represents a connected game controller and its state.
    /// </summary>
    public class ControllerInput
    {
        public string ControllerId { get; set; } = string.Empty;
        public ControllerType Type { get; set; } = ControllerType.Generic;
        public bool IsConnected { get; set; }
        public float? BatteryLevel { get; set; }
        public Dictionary<ControllerButton, bool> ButtonStates { get; set; } = new();
        public Dictionary<ControllerAxis, float> AxisValues { get; set; } = new();
    }

    public enum ControllerType
    {
        Xbox,
        PlayStation,
        Nintendo,
        Generic
    }

    /// <summary>
    /// Supported controller buttons.
    /// </summary>
    public enum ControllerButton
    {
        A, B, X, Y,
        LeftShoulder, RightShoulder,
        DPadUp, DPadDown, DPadLeft, DPadRight,
        LeftStick, RightStick,
        Start, Back, Guide
    }

    /// <summary>
    /// Analogue axes supported by the controller.
    /// </summary>
    public enum ControllerAxis
    {
        LeftStickX,
        LeftStickY,
        RightStickX,
        RightStickY,
        LeftTrigger,
        RightTrigger
    }

    /// <summary>
    /// High level actions recognised by HUDRA.
    /// </summary>
    public enum HudraAction
    {
        NavigateUp,
        NavigateDown,
        NavigateLeft,
        NavigateRight,
        NextPage,
        PreviousPage,
        Activate,
        Cancel,
        IncrementValue,
        DecrementValue
    }

    /// <summary>
    /// Event args for controller navigation requests.
    /// </summary>
    public class ControllerNavigationEventArgs : EventArgs
    {
        public ControllerNavigationEventArgs(HudraAction action)
        {
            Action = action;
        }

        public HudraAction Action { get; }
    }

    /// <summary>
    /// Event args for controller button events.
    /// </summary>
    public class ControllerButtonEventArgs : EventArgs
    {
        public ControllerButtonEventArgs(ControllerButton button, bool isPressed)
        {
            Button = button;
            IsPressed = isPressed;
        }

        public ControllerButton Button { get; }
        public bool IsPressed { get; }
    }
}
