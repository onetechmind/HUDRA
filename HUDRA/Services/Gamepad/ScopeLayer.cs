namespace HUDRA.Services.GamepadInput
{
    /// <summary>
    /// Insertion bands for the input scope stack. <see cref="InputRouter.Push"/>
    /// inserts a scope at the top of its own band, so a scope that arrives late
    /// (e.g. a page scope pushed by an async init continuation) can never land
    /// above a modal that is already open. That ordering used to be "whatever
    /// pushed last wins", which allowed a page scope to sit above a dialog and
    /// then be removed with it.
    ///
    /// Higher value = closer to the top = sees input first.
    /// </summary>
    public static class ScopeLayer
    {
        /// <summary>App chrome: page cycling, navbar selection. Never removed.</summary>
        public const int Shell = 0;

        /// <summary>Default page navigation (spatial focus). Never removed.</summary>
        public const int Page = 10;

        /// <summary>A page that drives its own navigation (e.g. the game library grid).</summary>
        public const int PageCustom = 20;

        /// <summary>Transient value editing: slider adjust, open dropdown.</summary>
        public const int Edit = 30;

        /// <summary>A modal owned by a page, such as the library's roulette overlay.</summary>
        public const int PageModal = 40;

        /// <summary>A ContentDialog. Blocks everything, including chrome.</summary>
        public const int Modal = 50;
    }
}
