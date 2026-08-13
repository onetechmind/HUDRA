using System.Runtime.Intrinsics.X86;

namespace HUDRA.Services.PawnIO
{
    /// <summary>
    /// Windows-side CPUID helper. Reads the effective family/model of the running
    /// CPU using the x86 CPUID instruction via <see cref="X86Base"/>.
    ///
    /// Kept OUT of the pure codename/mailbox files: uses hardware intrinsics.
    /// </summary>
    internal static class CpuidReader
    {
        /// <summary>
        /// Reads the effective (family, model) of the running CPU.
        ///
        /// From CPUID leaf 1, EAX:
        ///   base family    = bits 8..11
        ///   extended family= bits 20..27
        ///   base model     = bits 4..7
        ///   extended model = bits 16..19
        ///   effective family = baseFamily == 0xF ? baseFamily + extFamily : baseFamily
        ///   effective model  = (baseFamily == 0x6 || baseFamily == 0xF)
        ///                        ? (extModel &lt;&lt; 4) + baseModel : baseModel
        ///
        /// Worked example — AMD Phoenix reports EAX with baseFamily=0xF,
        /// extFamily=0xA, baseModel=0x4, extModel=0x7:
        ///   effective family = 0xF + 0xA = 0x19
        ///   effective model  = (0x7 &lt;&lt; 4) + 0x4 = 0x74   → family 0x19 model 0x74 = Phoenix.
        /// </summary>
        public static (uint Family, uint Model) Read()
        {
            if (!X86Base.IsSupported)
                return (0, 0);

            (int eax, _, _, _) = X86Base.CpuId(1, 0);
            uint raw = (uint)eax;

            uint baseFamily = (raw >> 8) & 0xF;
            uint extFamily = (raw >> 20) & 0xFF;
            uint baseModel = (raw >> 4) & 0xF;
            uint extModel = (raw >> 16) & 0xF;

            uint family = baseFamily == 0xF ? baseFamily + extFamily : baseFamily;
            uint model = (baseFamily == 0x6 || baseFamily == 0xF)
                ? (extModel << 4) + baseModel
                : baseModel;

            return (family, model);
        }

        /// <summary>
        /// True if the running CPU reports the vendor string "AuthenticAMD"
        /// (CPUID leaf 0: EBX, EDX, ECX).
        /// </summary>
        public static bool IsAmd()
        {
            if (!X86Base.IsSupported)
                return false;

            (_, int ebx, int ecx, int edx) = X86Base.CpuId(0, 0);

            // "AuthenticAMD" = EBX="Auth", EDX="enti", ECX="cAMD"
            return (uint)ebx == 0x68747541u   // "Auth"
                && (uint)edx == 0x69746E65u    // "enti"
                && (uint)ecx == 0x444D4163u;   // "cAMD"
        }
    }
}
