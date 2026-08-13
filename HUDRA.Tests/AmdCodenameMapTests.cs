using HUDRA.Services.PawnIO;
using Xunit;

namespace HUDRA.Tests
{
    public class AmdCodenameMapTests
    {
        [Theory]
        [InlineData(0x19u, 0x74u, AmdCodename.Phoenix)]
        [InlineData(0x19u, 0x75u, AmdCodename.HawkPoint)]
        [InlineData(0x19u, 0x78u, AmdCodename.Phoenix2)]
        [InlineData(0x19u, 0x50u, AmdCodename.Cezanne)]
        [InlineData(0x19u, 0x44u, AmdCodename.Rembrandt)]
        [InlineData(0x1Au, 0x24u, AmdCodename.StrixPoint)]
        [InlineData(0x1Au, 0x70u, AmdCodename.StrixHalo)]
        [InlineData(0x1Au, 0x60u, AmdCodename.KrackanPoint)]
        [InlineData(0x17u, 0xA0u, AmdCodename.Mendocino)]
        [InlineData(0x17u, 0x60u, AmdCodename.Renoir)]
        [InlineData(0x17u, 0x68u, AmdCodename.Lucienne)]
        [InlineData(0x17u, 0x90u, AmdCodename.VanGogh)]
        [InlineData(0x17u, 0x11u, AmdCodename.RavenRidge)]
        [InlineData(0x19u, 0x99u, AmdCodename.Unknown)] // unrecognized model within a known family
        [InlineData(0x15u, 0x01u, AmdCodename.Unknown)] // non-Zen family
        [InlineData(0x1Au, 0xFFu, AmdCodename.Unknown)] // unrecognized model within a known family
        public void FromCpuid_MapsShippingTargets(uint family, uint model, AmdCodename expected)
        {
            Assert.Equal(expected, AmdCodenameMap.FromCpuid(family, model));
        }
    }
}
