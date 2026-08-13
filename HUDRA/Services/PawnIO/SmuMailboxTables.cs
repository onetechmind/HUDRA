namespace HUDRA.Services.PawnIO
{
    /// <summary>
    /// A single SMU mailbox: the CMD (message id), RSP (response/status) and ARG
    /// (base of 6 consecutive 32-bit arg slots) register addresses.
    ///
    /// PURE: no P/Invoke, no WinUI, no I/O.
    /// </summary>
    public readonly record struct SmuMailbox(uint Cmd, uint Rsp, uint Arg);

    /// <summary>
    /// Per-codename SMU mailbox addresses and TDP command ids.
    ///
    /// Transcribed from HandheldCompanion's RyzenSmuService.cs. PURE: no P/Invoke,
    /// no WinUI, no I/O. Must compile in a plain net8.0 lib.
    /// </summary>
    public static class SmuMailboxTables
    {
        /// <summary>MP1 mailbox (CMD / RSP / ARG) for the given codename.</summary>
        public static SmuMailbox Mp1(AmdCodename c)
        {
            switch (c)
            {
                case AmdCodename.Rembrandt:
                case AmdCodename.VanGogh:
                case AmdCodename.Mendocino:
                case AmdCodename.Phoenix:
                case AmdCodename.Phoenix2:
                case AmdCodename.HawkPoint:
                    return new SmuMailbox(0x03B10528, 0x03B10578, 0x03B10998);

                case AmdCodename.KrackanPoint:
                case AmdCodename.StrixPoint:
                case AmdCodename.StrixHalo:
                    return new SmuMailbox(0x03B10928, 0x03B10978, 0x03B10998);

                default:
                    return new SmuMailbox(0x03B10528, 0x03B10564, 0x03B10998);
            }
        }

        /// <summary>PSMU mailbox (CMD / RSP / ARG). Single default for all codenames.</summary>
        public static SmuMailbox Psmu(AmdCodename c)
        {
            return new SmuMailbox(0x03B10A20, 0x03B10A80, 0x03B10A88);
        }

        /// <summary>
        /// TDP command ids (STAPM / fast / slow). arg0 = value in milliwatts.
        /// </summary>
        public static (uint Stapm, uint Fast, uint Slow) TdpCommands(AmdCodename c)
        {
            switch (c)
            {
                case AmdCodename.RavenRidge:
                case AmdCodename.Picasso:
                case AmdCodename.Dali:
                    return (0x1A, 0x1B, 0x1C);

                case AmdCodename.Renoir:
                case AmdCodename.Lucienne:
                case AmdCodename.Cezanne:
                case AmdCodename.VanGogh:
                case AmdCodename.Rembrandt:
                case AmdCodename.Mendocino:
                case AmdCodename.Phoenix:
                case AmdCodename.Phoenix2:
                case AmdCodename.HawkPoint:
                case AmdCodename.StrixPoint:
                case AmdCodename.StrixHalo:
                case AmdCodename.KrackanPoint:
                    return (0x14, 0x15, 0x16);

                default:
                    // Unlisted (incl. Unknown, desktop parts) fall back to the Zen2+
                    // APU command set so callers still get a plausible id; IsSupported
                    // gates whether we ever act on it.
                    return (0x14, 0x15, 0x16);
            }
        }

        /// <summary>False only for <see cref="AmdCodename.Unknown"/>.</summary>
        public static bool IsSupported(AmdCodename c) => c != AmdCodename.Unknown;
    }
}
