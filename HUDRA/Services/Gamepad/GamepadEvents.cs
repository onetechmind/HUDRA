using System;

namespace HUDRA.Services.GamepadInput
{
    /// <summary>
    /// Semantic gamepad actions produced by <see cref="GamepadInputReader"/>.
    /// Directional actions (NavUp/NavDown/NavLeft/NavRight) auto-repeat while held;
    /// all other actions fire once per press.
    /// </summary>
    public enum GamepadAction
    {
        NavUp,
        NavDown,
        NavLeft,
        NavRight,
        Accept,   // A
        Back,     // B
        X,
        Y,
        LB,       // Left shoulder
        RB,       // Right shoulder
        LT,       // Left trigger (digital, with hysteresis)
        RT        // Right trigger (digital, with hysteresis)
    }

    /// <summary>
    /// Where a semantic action originated. Keyboard covers both physical keys
    /// (arrows/Enter/Escape) and the VirtualKey.Gamepad* keys WinUI synthesizes
    /// from gamepad hardware.
    /// </summary>
    public enum GamepadEventSource
    {
        Gamepad,
        Keyboard
    }

    /// <summary>
    /// A single semantic input event. Timestamp is monotonic time since the
    /// reader started (not wall-clock).
    /// </summary>
    public readonly record struct GamepadEvent(
        GamepadAction Action,
        bool IsRepeat,
        TimeSpan Timestamp,
        GamepadEventSource Source = GamepadEventSource.Gamepad);

    /// <summary>
    /// Per-tick analog state for scopes that consume raw stick motion (e.g. scrolling).
    /// Emitted while the right stick is deflected and once more when it returns to rest.
    /// </summary>
    public readonly record struct GamepadStickFrame(double RightStickY);
}
