using System;
using System.Buffers.Binary;
using HUDRA.Services.PawnIO;
using Xunit;

namespace HUDRA.Tests
{
    public class PawnIoFramingTests
    {
        [Fact]
        public void Constants_MatchExpectedValues()
        {
            Assert.Equal(32, PawnIoFraming.FunctionNameBytes);
            Assert.Equal(41394u << 16, PawnIoFraming.DeviceType);
            Assert.Equal(PawnIoFraming.DeviceType | (0x821u << 2), PawnIoFraming.IoctlLoadBinary);
            Assert.Equal(PawnIoFraming.DeviceType | (0x841u << 2), PawnIoFraming.IoctlExecute);
        }

        [Fact]
        public void BuildExecuteInput_ProducesCorrectLength()
        {
            var buffer = PawnIoFraming.BuildExecuteInput(
                "ioctl_write_smu_register", new ulong[] { 0x03B10528u, 0x3A98u });

            Assert.Equal(32 + (2 * 8), buffer.Length);
        }

        [Fact]
        public void BuildExecuteInput_WritesNameAsNulTerminatedAscii()
        {
            const string name = "ioctl_write_smu_register";
            var buffer = PawnIoFraming.BuildExecuteInput(name, new ulong[] { 0x03B10528u, 0x3A98u });

            for (int i = 0; i < name.Length; i++)
            {
                Assert.Equal((byte)name[i], buffer[i]);
            }

            // The rest of the 32-byte header, including the terminator immediately
            // after the name, must be zero-filled.
            for (int i = name.Length; i < PawnIoFraming.FunctionNameBytes; i++)
            {
                Assert.Equal(0, buffer[i]);
            }
        }

        [Fact]
        public void BuildExecuteInput_WritesArgsLittleEndianAfterHeader()
        {
            var buffer = PawnIoFraming.BuildExecuteInput(
                "ioctl_write_smu_register", new ulong[] { 0x03B10528u, 0x3A98u });

            // First arg (0x03B10528) at offset 32, little-endian.
            Assert.Equal(0x28, buffer[32]);
            Assert.Equal(0x05, buffer[33]);
            Assert.Equal(0xB1, buffer[34]);
            Assert.Equal(0x03, buffer[35]);
            Assert.Equal(0, buffer[36]);
            Assert.Equal(0, buffer[37]);
            Assert.Equal(0, buffer[38]);
            Assert.Equal(0, buffer[39]);

            // Second arg (0x3A98) at offset 40, little-endian.
            Assert.Equal(0x98, buffer[40]);
            Assert.Equal(0x3A, buffer[41]);
            Assert.Equal(0, buffer[42]);
            Assert.Equal(0, buffer[43]);
            Assert.Equal(0, buffer[44]);
            Assert.Equal(0, buffer[45]);
            Assert.Equal(0, buffer[46]);
            Assert.Equal(0, buffer[47]);
        }

        [Fact]
        public void BuildExecuteInput_NullArgs_TreatedAsEmpty()
        {
            var buffer = PawnIoFraming.BuildExecuteInput("some_function", null);
            Assert.Equal(32, buffer.Length);
        }

        [Fact]
        public void BuildExecuteInput_31CharName_IsAccepted()
        {
            string name = new string('a', 31);
            var buffer = PawnIoFraming.BuildExecuteInput(name, null);

            Assert.Equal(32, buffer.Length);
            for (int i = 0; i < 31; i++)
            {
                Assert.Equal((byte)'a', buffer[i]);
            }
        }

        [Fact]
        public void BuildExecuteInput_32CharName_Throws()
        {
            string name = new string('a', 32);
            Assert.Throws<ArgumentException>(() => PawnIoFraming.BuildExecuteInput(name, null));
        }

        [Fact]
        public void BuildExecuteInput_NullName_Throws()
        {
            Assert.Throws<ArgumentException>(() => PawnIoFraming.BuildExecuteInput(null!, null));
        }

        [Fact]
        public void ParseExecuteOutput_RoundTripsLittleEndianValues()
        {
            ulong[] original = { 0x0102030405060708UL, 0xAABBCCDDEEFF0011UL };
            var buffer = new byte[16];
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(0, 8), original[0]);
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(8, 8), original[1]);

            var result = PawnIoFraming.ParseExecuteOutput(buffer, 16);

            Assert.Equal(original, result);
        }

        [Fact]
        public void ParseExecuteOutput_NullBuffer_ReturnsEmpty()
        {
            Assert.Empty(PawnIoFraming.ParseExecuteOutput(null!, 16));
        }

        [Fact]
        public void ParseExecuteOutput_BytesReturnedLessThan8_ReturnsEmpty()
        {
            var buffer = new byte[16];
            Assert.Empty(PawnIoFraming.ParseExecuteOutput(buffer, 7));
            Assert.Empty(PawnIoFraming.ParseExecuteOutput(buffer, 0));
        }

        [Fact]
        public void ParseExecuteOutput_ClampsCountToBufferLength()
        {
            var buffer = new byte[8];
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(0, 8), 0x1234UL);

            // Driver claims more bytes were returned than the buffer can hold.
            var result = PawnIoFraming.ParseExecuteOutput(buffer, 100);

            Assert.Single(result);
            Assert.Equal(0x1234UL, result[0]);
        }
    }
}
