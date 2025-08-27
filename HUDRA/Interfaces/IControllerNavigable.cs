using HUDRA.Services.Input;

namespace HUDRA.Interfaces
{
    /// <summary>
    /// Interface implemented by controls that can receive controller focus
    /// and handle controller button input.
    /// </summary>
    public interface IControllerNavigable
    {
        bool CanReceiveControllerFocus { get; }

        bool HandleControllerInput(ControllerButton button, bool isPressed);

        void OnControllerFocusReceived();

        void OnControllerFocusLost();
    }
}
