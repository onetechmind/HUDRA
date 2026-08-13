using HUDRA.Services.PawnIO;
using Xunit;

namespace HUDRA.Tests
{
    public class SmuMailboxTablesTests
    {
        [Theory]
        [InlineData(AmdCodename.Phoenix)]
        [InlineData(AmdCodename.HawkPoint)]
        [InlineData(AmdCodename.Rembrandt)]
        [InlineData(AmdCodename.VanGogh)]
        [InlineData(AmdCodename.Mendocino)]
        [InlineData(AmdCodename.Phoenix2)]
        public void Mp1_Zen2PlusApus_UseSharedMailbox(AmdCodename codename)
        {
            var mailbox = SmuMailboxTables.Mp1(codename);

            Assert.Equal(0x03B10528u, mailbox.Cmd);
            Assert.Equal(0x03B10578u, mailbox.Rsp);
            Assert.Equal(0x03B10998u, mailbox.Arg);
        }

        [Theory]
        [InlineData(AmdCodename.StrixPoint)]
        [InlineData(AmdCodename.StrixHalo)]
        [InlineData(AmdCodename.KrackanPoint)]
        public void Mp1_Zen5_UsesItsOwnMailbox(AmdCodename codename)
        {
            var mailbox = SmuMailboxTables.Mp1(codename);

            Assert.Equal(0x03B10928u, mailbox.Cmd);
            Assert.Equal(0x03B10978u, mailbox.Rsp);
            Assert.Equal(0x03B10998u, mailbox.Arg);
        }

        [Fact]
        public void Mp1_UnlistedCodename_UsesDefaultMailbox()
        {
            var mailbox = SmuMailboxTables.Mp1(AmdCodename.Renoir);

            Assert.Equal(0x03B10528u, mailbox.Cmd);
            Assert.Equal(0x03B10564u, mailbox.Rsp);
            Assert.Equal(0x03B10998u, mailbox.Arg);
        }

        [Fact]
        public void Psmu_IsSameForAllCodenames()
        {
            var mailbox = SmuMailboxTables.Psmu(AmdCodename.Phoenix);

            Assert.Equal(0x03B10A20u, mailbox.Cmd);
            Assert.Equal(0x03B10A80u, mailbox.Rsp);
            Assert.Equal(0x03B10A88u, mailbox.Arg);

            // Single default: any codename should give the same values.
            Assert.Equal(mailbox, SmuMailboxTables.Psmu(AmdCodename.RavenRidge));
        }

        [Theory]
        [InlineData(AmdCodename.RavenRidge)]
        [InlineData(AmdCodename.Picasso)]
        [InlineData(AmdCodename.Dali)]
        public void TdpCommands_Zen1Apus_UseLegacyIds(AmdCodename codename)
        {
            var (stapm, fast, slow) = SmuMailboxTables.TdpCommands(codename);

            Assert.Equal(0x1Au, stapm);
            Assert.Equal(0x1Bu, fast);
            Assert.Equal(0x1Cu, slow);
        }

        [Theory]
        [InlineData(AmdCodename.Phoenix)]
        [InlineData(AmdCodename.HawkPoint)]
        [InlineData(AmdCodename.StrixPoint)]
        [InlineData(AmdCodename.Renoir)]
        public void TdpCommands_Zen2PlusApus_UseCurrentIds(AmdCodename codename)
        {
            var (stapm, fast, slow) = SmuMailboxTables.TdpCommands(codename);

            Assert.Equal(0x14u, stapm);
            Assert.Equal(0x15u, fast);
            Assert.Equal(0x16u, slow);
        }

        [Theory]
        [InlineData(AmdCodename.Phoenix, true)]
        [InlineData(AmdCodename.StrixPoint, true)]
        [InlineData(AmdCodename.HawkPoint, true)]
        [InlineData(AmdCodename.Mendocino, true)]
        [InlineData(AmdCodename.Unknown, false)]
        public void IsSupported_TrueForAllExceptUnknown(AmdCodename codename, bool expected)
        {
            Assert.Equal(expected, SmuMailboxTables.IsSupported(codename));
        }
    }
}
