namespace HUDRA.Services.PawnIO
{
    /// <summary>
    /// AMD APU/CPU codenames HUDRA can identify from CPUID. Order matches
    /// HandheldCompanion's RyzenSmuService enum.
    ///
    /// PURE: no P/Invoke, no WinUI, no I/O. Must compile in a plain net8.0 lib.
    /// </summary>
    public enum AmdCodename
    {
        RavenRidge,
        Picasso,
        Dali,
        Renoir,
        Lucienne,
        Cezanne,
        VanGogh,
        Rembrandt,
        Mendocino,
        Phoenix,
        Phoenix2,
        HawkPoint,
        StrixPoint,
        StrixHalo,
        KrackanPoint,
        DragonRange,
        Raphael,
        GraniteRidge,
        Unknown
    }

    /// <summary>
    /// Maps a CPUID (effective family, effective model) pair to an <see cref="AmdCodename"/>.
    ///
    /// PURE: no P/Invoke, no WinUI, no I/O. Must compile in a plain net8.0 lib.
    /// The CPUID extraction (extended-family add, extended-model shift) lives in
    /// CpuidReader; this map assumes the already-normalized family/model.
    /// </summary>
    public static class AmdCodenameMap
    {
        public static AmdCodename FromCpuid(uint family, uint model)
        {
            switch (family)
            {
                // Zen / Zen2
                case 0x17:
                    return model switch
                    {
                        0x11 => AmdCodename.RavenRidge,
                        0x18 => AmdCodename.Picasso,
                        0x20 => AmdCodename.Dali,
                        0x60 => AmdCodename.Renoir,
                        0x68 => AmdCodename.Lucienne,
                        0x90 => AmdCodename.VanGogh,
                        0xA0 => AmdCodename.Mendocino,
                        _ => AmdCodename.Unknown
                    };

                // Zen3 / Zen4
                case 0x19:
                    return model switch
                    {
                        0x50 => AmdCodename.Cezanne,
                        0x44 => AmdCodename.Rembrandt,
                        0x74 => AmdCodename.Phoenix,
                        0x75 => AmdCodename.HawkPoint,
                        0x78 => AmdCodename.Phoenix2,
                        0x61 => AmdCodename.Raphael,
                        0x40 => AmdCodename.DragonRange,
                        _ => AmdCodename.Unknown
                    };

                // Zen5
                case 0x1A:
                    return model switch
                    {
                        0x24 => AmdCodename.StrixPoint,
                        0x70 => AmdCodename.StrixHalo,
                        0x60 => AmdCodename.KrackanPoint,
                        0x44 => AmdCodename.GraniteRidge,
                        _ => AmdCodename.Unknown
                    };

                default:
                    return AmdCodename.Unknown;
            }
        }
    }
}
