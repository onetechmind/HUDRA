using System;

namespace HUDRA.Services.PawnIO
{
    /// <summary>
    /// PawnIO/LpcIO-backed replacement for the old WinRing0 <c>Ols</c> port I/O.
    ///
    /// Method names deliberately mirror the members the EC consumers used on
    /// <c>Ols</c> (<see cref="WriteIoPortByte"/>, <see cref="ReadIoPortByte"/>,
    /// <see cref="IsOpen"/>, <see cref="Dispose"/>) so the transport swap is a
    /// minimal diff at every call site.
    ///
    /// Backed by the LpcIO.bin PawnIO module. After <see cref="TryCreate"/> selects
    /// Super-I/O slot 1, ports 0x4E/0x4F are on the module allowlist - the exact
    /// pair every HUDRA EC device uses.
    ///
    /// Nothing here throws: writes/reads log once under "PAWNIO" and no-op on failure,
    /// matching how the old <c>Ols</c> never threw on a bad port.
    /// </summary>
    public sealed class LpcIoPort : IDisposable
    {
        private const string ModuleName = "LpcIO.bin";

        private readonly PawnIoTransport _transport;
        private bool _isOpen;

        private LpcIoPort(PawnIoTransport transport)
        {
            _transport = transport;
            _isOpen = true;
        }

        /// <summary>
        /// True while the module is loaded and the port pair is selected; false after Dispose.
        /// </summary>
        public bool IsOpen => _isOpen;

        /// <summary>
        /// Opens a PawnIO transport, loads LpcIO.bin, and selects the 0x4E/0x4F Super-I/O
        /// slot. Returns (null, message) on any failure, disposing the transport first.
        /// </summary>
        public static (LpcIoPort? Port, string Message) TryCreate()
        {
            var (transport, openMessage) = PawnIoTransport.TryOpen();
            if (transport == null)
            {
                DebugLogger.Log($"LpcIoPort: could not open PawnIO transport: {openMessage}", "PAWNIO");
                return (null, openMessage);
            }

            var loadResult = transport.LoadModuleFromFile(PawnIoInstallService.GetModulePath(ModuleName));
            if (!loadResult.Success)
            {
                DebugLogger.Log($"LpcIoPort: loading {ModuleName} failed: {loadResult.Message}", "PAWNIO");
                transport.Dispose();
                return (null, loadResult.Message);
            }

            // slot 1 == the 0x4E/0x4F index/data pair used by every HUDRA EC device.
            var selectResult = transport.Execute("ioctl_select_slot", new ulong[] { 1 }, 0);
            if (!selectResult.Success)
            {
                DebugLogger.Log($"LpcIoPort: selecting Super-I/O slot 1 failed: {selectResult.Message}", "PAWNIO");
                transport.Dispose();
                return (null, selectResult.Message);
            }

            DebugLogger.Log("LpcIoPort: ready (LpcIO.bin loaded, slot 1 selected)", "PAWNIO");
            return (new LpcIoPort(transport), "LpcIO ready");
        }

        /// <summary>
        /// Writes a byte to an allow-listed I/O port. No-ops (and logs once) on failure.
        /// </summary>
        public void WriteIoPortByte(ushort port, byte value)
        {
            if (!_isOpen)
                return;

            var result = _transport.Execute("ioctl_pio_outb", new ulong[] { port, value }, 0);
            if (!result.Success)
            {
                DebugLogger.Log($"LpcIoPort: outb port=0x{port:X} value=0x{value:X} failed: {result.Message}", "PAWNIO");
            }
        }

        /// <summary>
        /// Reads a byte from an allow-listed I/O port. Returns 0 (and logs once) on failure.
        /// </summary>
        public byte ReadIoPortByte(ushort port)
        {
            if (!_isOpen)
                return 0;

            var result = _transport.Execute("ioctl_pio_inb", new ulong[] { port }, 1);
            if (!result.Success)
            {
                DebugLogger.Log($"LpcIoPort: inb port=0x{port:X} failed: {result.Message}", "PAWNIO");
                return 0;
            }

            return (byte)(result.Output.Length > 0 ? result.Output[0] : 0);
        }

        public void Dispose()
        {
            if (!_isOpen)
                return;

            _isOpen = false;
            _transport.Dispose();
        }
    }
}
