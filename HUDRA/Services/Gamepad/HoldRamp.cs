namespace HUDRA.Services.GamepadInput
{
    /// <summary>
    /// Step multiplier for held directional adjustments: x2 after ~1.3 s of
    /// holding, x4 after ~2.2 s, x8 after ~3 s (streaks count 110 ms repeats).
    /// One place for the thresholds so every hold-to-adjust surface ramps
    /// identically: ValueEditScope counts explicit repeat events, the TDP
    /// picker infers holds from event timing (its left/right adjust directly,
    /// without an edit mode).
    /// </summary>
    public static class HoldRamp
    {
        public static int Multiplier(int streak) => streak switch
        {
            < 8 => 1,
            < 16 => 2,
            < 24 => 4,
            _ => 8
        };
    }
}
