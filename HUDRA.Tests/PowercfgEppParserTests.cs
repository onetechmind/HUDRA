using HUDRA.Services.Power;
using Xunit;

namespace HUDRA.Tests
{
    public class PowercfgEppParserTests
    {
        // Trimmed but structurally faithful `powercfg /query SCHEME_CURRENT <sub> <setting>` output
        private const string TypicalQueryOutput = @"Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)
  Subgroup GUID: 54533251-82be-4824-96c1-47b60b740d00  (Processor power management)
    Power Setting GUID: 36687f9e-e3a5-4dbf-b1dc-15eb381c6863  (Processor energy performance preference policy)
      Minimum Possible Setting: 0x00000000
      Maximum Possible Setting: 0x00000064
      Possible Settings increment: 0x00000001
      Possible Settings units: %
    Current AC Power Setting Index: 0x00000021
    Current DC Power Setting Index: 0x00000032";

        [Fact]
        public void ParseSettingIndexes_ExtractsAcAndDcValues()
        {
            var (ac, dc) = PowercfgEppParser.ParseSettingIndexes(TypicalQueryOutput);

            Assert.Equal(0x21, ac);
            Assert.Equal(0x32, dc);
        }

        [Fact]
        public void ParseSettingIndexes_IgnoresMinMaxSettingLines()
        {
            // "Maximum Possible Setting: 0x00000064" must not be mistaken for a
            // current value.
            var output = @"    Power Setting GUID: 36687f9e-e3a5-4dbf-b1dc-15eb381c6863
      Minimum Possible Setting: 0x00000000
      Maximum Possible Setting: 0x00000064";

            var (ac, dc) = PowercfgEppParser.ParseSettingIndexes(output);

            Assert.Equal(-1, ac);
            Assert.Equal(-1, dc);
        }

        [Fact]
        public void ParseSettingIndexes_AcOnly_ReportsMissingDc()
        {
            var output = "    Current AC Power Setting Index: 0x00000050";

            var (ac, dc) = PowercfgEppParser.ParseSettingIndexes(output);

            Assert.Equal(0x50, ac);
            Assert.Equal(-1, dc);
        }

        [Theory]
        [InlineData("")]
        [InlineData("The power scheme, subgroup or setting specified does not exist.")]
        [InlineData("garbage\nwith no relevant\nlines at all")]
        public void ParseSettingIndexes_NoMatch_ReturnsMinusOne(string output)
        {
            var (ac, dc) = PowercfgEppParser.ParseSettingIndexes(output);

            Assert.Equal(-1, ac);
            Assert.Equal(-1, dc);
        }

        [Fact]
        public void ParseSettingIndexes_HandlesWindowsLineEndings()
        {
            var output = "Current AC Power Setting Index: 0x00000000\r\nCurrent DC Power Setting Index: 0x00000064\r\n";

            var (ac, dc) = PowercfgEppParser.ParseSettingIndexes(output);

            Assert.Equal(0, ac);
            Assert.Equal(100, dc);
        }

        [Fact]
        public void ParseSettingIndexes_CaseInsensitiveLabels()
        {
            var output = "current ac power setting index: 0x0000000a";

            var (ac, _) = PowercfgEppParser.ParseSettingIndexes(output);

            Assert.Equal(10, ac);
        }

        [Fact]
        public void ParseSettingIndexes_UnparsableHex_ReturnsMinusOne()
        {
            var output = "Current AC Power Setting Index: 0xZZZZ";

            var (ac, _) = PowercfgEppParser.ParseSettingIndexes(output);

            Assert.Equal(-1, ac);
        }

        [Theory]
        [InlineData(-50, 0)]
        [InlineData(-1, 0)]
        [InlineData(0, 0)]
        [InlineData(50, 50)]
        [InlineData(100, 100)]
        [InlineData(101, 100)]
        [InlineData(int.MaxValue, 100)]
        public void ClampEpp_ClampsToValidRange(int input, int expected)
        {
            Assert.Equal(expected, PowercfgEppParser.ClampEpp(input));
        }
    }
}
