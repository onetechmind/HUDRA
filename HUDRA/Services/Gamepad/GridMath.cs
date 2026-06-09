namespace HUDRA.Services.GamepadInput
{
    /// <summary>
    /// Index math for directional movement over a vertically-flowing grid
    /// (left-to-right, top-to-bottom, fixed column count, last row possibly
    /// partial). Shared by grid-style scopes (game library, image pickers).
    /// Pure and WinUI-free for unit testing.
    /// </summary>
    public static class GridMath
    {
        /// <summary>
        /// Returns the target index for a move, or -1 if the move exits the
        /// grid in that direction (caller decides what lies beyond the edge).
        /// Left/right flow across row boundaries; down from the last partial
        /// row's neighbors lands on the final item.
        /// </summary>
        public static int Move(int index, int count, int columns, GamepadAction direction)
        {
            if (count <= 0 || columns <= 0 || index < 0 || index >= count) return -1;

            switch (direction)
            {
                case GamepadAction.NavLeft:
                    return index > 0 ? index - 1 : -1;

                case GamepadAction.NavRight:
                    return index < count - 1 ? index + 1 : -1;

                case GamepadAction.NavUp:
                    return index - columns >= 0 ? index - columns : -1;

                case GamepadAction.NavDown:
                    if (index + columns < count) return index + columns;
                    // From the second-to-last row over a hole in a partial last
                    // row, land on the last item instead of exiting
                    int lastRowStart = ((count - 1) / columns) * columns;
                    return index < lastRowStart ? count - 1 : -1;

                default:
                    return -1;
            }
        }
    }
}
