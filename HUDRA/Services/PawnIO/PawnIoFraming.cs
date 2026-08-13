using System;
using System.Buffers.Binary;
using System.Text;

namespace HUDRA.Services.PawnIO
{
    /// <summary>
    /// Pure wire-format helpers for the PawnIO driver interface.
    ///
    /// Deliberately dependency-free (no P/Invoke, no WinUI, no I/O) so it can be
    /// compiled into the test project and verified on any platform.
    /// </summary>
    public static class PawnIoFraming
    {
        // IOCTL_ codes: CTL_CODE(41394, function, METHOD_BUFFERED, FILE_ANY_ACCESS)
        public const uint DeviceType = 41394u << 16;
        public const uint IoctlLoadBinary = DeviceType | (0x821u << 2);
        public const uint IoctlExecute = DeviceType | (0x841u << 2);

        /// <summary>
        /// Size of the fixed-width ASCII function name header at the start of an execute buffer.
        /// The name must fit in 31 bytes so a NUL terminator always remains.
        /// </summary>
        public const int FunctionNameBytes = 32;

        /// <summary>
        /// Builds the input buffer for IoctlExecute: a 32-byte zero-padded ASCII function
        /// name followed by the arguments as little-endian UInt64s.
        /// </summary>
        /// <remarks>
        /// Throws on a malformed function name because that can only be a programming error
        /// at a call site inside this app - it is never driven by runtime state or user input.
        /// </remarks>
        public static byte[] BuildExecuteInput(string functionName, ulong[] args)
        {
            if (functionName == null)
                throw new ArgumentException("Function name must not be null.", nameof(functionName));

            int nameLength = Encoding.ASCII.GetByteCount(functionName);
            if (nameLength > FunctionNameBytes - 1)
                throw new ArgumentException(
                    $"Function name '{functionName}' is {nameLength} bytes; the limit is {FunctionNameBytes - 1} (a NUL terminator is required).",
                    nameof(functionName));

            var effectiveArgs = args ?? Array.Empty<ulong>();
            var buffer = new byte[FunctionNameBytes + (effectiveArgs.Length * 8)];

            Encoding.ASCII.GetBytes(functionName, 0, functionName.Length, buffer, 0);

            for (int i = 0; i < effectiveArgs.Length; i++)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(
                    buffer.AsSpan(FunctionNameBytes + (i * 8), 8),
                    effectiveArgs[i]);
            }

            return buffer;
        }

        /// <summary>
        /// Parses the output buffer returned by IoctlExecute into little-endian UInt64s.
        /// Returns an empty array rather than throwing when the driver returns nothing usable.
        /// </summary>
        public static ulong[] ParseExecuteOutput(byte[] buffer, int bytesReturned)
        {
            if (buffer == null || bytesReturned < 8)
                return Array.Empty<ulong>();

            int count = bytesReturned / 8;
            int maxCount = buffer.Length / 8;
            if (count > maxCount)
                count = maxCount;

            if (count <= 0)
                return Array.Empty<ulong>();

            var values = new ulong[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(i * 8, 8));
            }

            return values;
        }
    }
}
