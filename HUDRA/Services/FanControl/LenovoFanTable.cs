using System;
using System.Linq;

namespace HUDRA.Services.FanControl
{
    /// <summary>
    /// Represents a Lenovo Legion Go fan speed table.
    /// This structure is serialized to a 64-byte array for WMI calls.
    /// </summary>
    public readonly struct LenovoFanTable
    {
        // Header fields
        private readonly byte _fstm;  // Fan Speed Table Mode (always 1)
        private readonly byte _fsid;  // Fan Speed ID (always 0)
        private readonly uint _fstl;  // Fan Speed Table Length (always 0)

        // 10 fan speed values (0-100%)
        private readonly ushort _fss0;
        private readonly ushort _fss1;
        private readonly ushort _fss2;
        private readonly ushort _fss3;
        private readonly ushort _fss4;
        private readonly ushort _fss5;
        private readonly ushort _fss6;
        private readonly ushort _fss7;
        private readonly ushort _fss8;
        private readonly ushort _fss9;

        /// <summary>
        /// Creates a new LenovoFanTable from an array of 10 fan speed values.
        /// </summary>
        /// <param name="fanSpeeds">Array of 10 fan speed values (0-100%)</param>
        public LenovoFanTable(ushort[] fanSpeeds)
        {
            if (fanSpeeds == null || fanSpeeds.Length != 10)
            {
                throw new ArgumentException("Fan speeds array must contain exactly 10 values", nameof(fanSpeeds));
            }

            // Validate and clamp values to 0-100
            for (int i = 0; i < fanSpeeds.Length; i++)
            {
                fanSpeeds[i] = Math.Clamp(fanSpeeds[i], (ushort)0, (ushort)100);
            }

            // Set header fields
            _fstm = 1;
            _fsid = 0;
            _fstl = 0;

            // Set fan speed values
            _fss0 = fanSpeeds[0];
            _fss1 = fanSpeeds[1];
            _fss2 = fanSpeeds[2];
            _fss3 = fanSpeeds[3];
            _fss4 = fanSpeeds[4];
            _fss5 = fanSpeeds[5];
            _fss6 = fanSpeeds[6];
            _fss7 = fanSpeeds[7];
            _fss8 = fanSpeeds[8];
            _fss9 = fanSpeeds[9];
        }

        /// <summary>
        /// Serializes the fan table to a 64-byte array for WMI transmission.
        ///
        /// Byte Layout:
        /// 0x00 (1 byte)  - FSTM (Fan Speed Table Mode)
        /// 0x01 (1 byte)  - FSID (Fan Speed ID)
        /// 0x02 (4 bytes) - FSTL (Fan Speed Table Length)
        /// 0x06 (2 bytes) - FSS0 (Fan Speed 0)
        /// 0x08 (2 bytes) - FSS1 (Fan Speed 1)
        /// ...
        /// 0x18 (2 bytes) - FSS9 (Fan Speed 9)
        /// 0x1A-0x3F (38 bytes) - Padding (zeros)
        /// </summary>
        public byte[] GetBytes()
        {
            var bytes = new byte[64];
            int offset = 0;

            // Header
            bytes[offset++] = _fstm;
            bytes[offset++] = _fsid;

            // FSTL (4 bytes, little-endian)
            bytes[offset++] = (byte)(_fstl & 0xFF);
            bytes[offset++] = (byte)((_fstl >> 8) & 0xFF);
            bytes[offset++] = (byte)((_fstl >> 16) & 0xFF);
            bytes[offset++] = (byte)((_fstl >> 24) & 0xFF);

            // Fan speed values (10 × 2 bytes each, little-endian)
            WriteUshort(bytes, ref offset, _fss0);
            WriteUshort(bytes, ref offset, _fss1);
            WriteUshort(bytes, ref offset, _fss2);
            WriteUshort(bytes, ref offset, _fss3);
            WriteUshort(bytes, ref offset, _fss4);
            WriteUshort(bytes, ref offset, _fss5);
            WriteUshort(bytes, ref offset, _fss6);
            WriteUshort(bytes, ref offset, _fss7);
            WriteUshort(bytes, ref offset, _fss8);
            WriteUshort(bytes, ref offset, _fss9);

            // Remaining bytes are already zero-initialized
            return bytes;
        }

        /// <summary>
        /// Helper method to write a ushort in little-endian format.
        /// </summary>
        private static void WriteUshort(byte[] buffer, ref int offset, ushort value)
        {
            buffer[offset++] = (byte)(value & 0xFF);
            buffer[offset++] = (byte)((value >> 8) & 0xFF);
        }

        /// <summary>
        /// Gets the fan speed values as an array.
        /// </summary>
        public ushort[] GetFanSpeeds()
        {
            return new ushort[] { _fss0, _fss1, _fss2, _fss3, _fss4, _fss5, _fss6, _fss7, _fss8, _fss9 };
        }

        /// <summary>
        /// Returns a string representation of the fan table for debugging.
        /// </summary>
        public override string ToString()
        {
            var speeds = GetFanSpeeds();
            return $"FanTable[{string.Join(", ", speeds.Select(s => $"{s}%"))}]";
        }
    }
}
