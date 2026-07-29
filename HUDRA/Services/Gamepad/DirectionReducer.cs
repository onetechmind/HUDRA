using System.Collections.Generic;

namespace HUDRA.Services.GamepadInput
{
    /// <summary>
    /// Reduces a raw held-action set to at most one directional action, which is
    /// what navigation expects: each event moves focus one step, so emitting two
    /// directions from a diagonal would double-step.
    ///
    /// Kept separate (and WinUI-free) because the ORDER of reduction and stuck-input
    /// masking matters: masking has to happen on the raw set first, otherwise a
    /// stuck Up permanently occupies the single directional slot and masks every
    /// other direction — which is precisely how one drifting controller made all
    /// navigation appear dead.
    /// </summary>
    public static class DirectionReducer
    {
        /// <summary>Priority order when several directions are held at once.</summary>
        private static readonly GamepadAction[] DirectionPriority =
        {
            GamepadAction.NavUp,
            GamepadAction.NavDown,
            GamepadAction.NavLeft,
            GamepadAction.NavRight
        };

        public static bool IsDirectional(GamepadAction action) =>
            action is GamepadAction.NavUp or GamepadAction.NavDown
                   or GamepadAction.NavLeft or GamepadAction.NavRight;

        /// <summary>
        /// Copies <paramref name="raw"/> into <paramref name="result"/>, keeping all
        /// non-directional actions and at most the highest-priority direction.
        /// </summary>
        public static void Reduce(IReadOnlySet<GamepadAction> raw, ISet<GamepadAction> result)
        {
            result.Clear();

            foreach (var action in raw)
            {
                if (!IsDirectional(action)) result.Add(action);
            }

            foreach (var direction in DirectionPriority)
            {
                if (raw.Contains(direction))
                {
                    result.Add(direction);
                    return;
                }
            }
        }
    }
}
