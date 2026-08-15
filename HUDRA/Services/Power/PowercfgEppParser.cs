using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace HUDRA.Services.Power
{
    /// <summary>
    /// Parses `powercfg /query` output for a single power setting. Kept free of
    /// WinUI and Windows-only dependencies so it can be source-linked into
    /// HUDRA.Tests and run cross-platform.
    /// </summary>
    public static class PowercfgEppParser
    {
        private static readonly Regex HexValueRegex = new(@"0x([0-9a-fA-F]+)", RegexOptions.Compiled);

        /// <summary>
        /// Extracts the AC and DC setting indexes from `powercfg /query` output
        /// ("Current AC Power Setting Index: 0x00000032" style lines).
        /// Returns -1 for a value that is missing or unparsable.
        /// </summary>
        public static (int Ac, int Dc) ParseSettingIndexes(string powercfgQueryOutput)
        {
            int ac = -1, dc = -1;

            if (string.IsNullOrEmpty(powercfgQueryOutput))
                return (ac, dc);

            foreach (var line in powercfgQueryOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Contains("Current AC Power Setting Index:", StringComparison.OrdinalIgnoreCase))
                {
                    ac = ParseHexValue(line, ac);
                }
                else if (line.Contains("Current DC Power Setting Index:", StringComparison.OrdinalIgnoreCase))
                {
                    dc = ParseHexValue(line, dc);
                }
            }

            return (ac, dc);
        }

        public static int ClampEpp(int value) => Math.Clamp(value, 0, 100);

        private static int ParseHexValue(string line, int fallback)
        {
            var match = HexValueRegex.Match(line);
            if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.HexNumber, null, out var value))
            {
                return value;
            }
            return fallback;
        }
    }
}
