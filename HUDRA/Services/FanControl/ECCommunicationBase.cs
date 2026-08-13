using HUDRA.Services.PawnIO;
using System;
using System.Diagnostics;

namespace HUDRA.Services.FanControl
{
    public abstract class ECCommunicationBase : IDisposable
    {
        protected LpcIoPort? _port;
        protected bool _disposed = false;
        private readonly object _lockObject = new object();

        public bool IsOpen => _port != null && _port.IsOpen;

        protected virtual bool InitializeEC()
        {
            try
            {
                var (port, msg) = LpcIoPort.TryCreate();
                _port = port;

                if (_port == null || !_port.IsOpen)
                {
                    Debug.WriteLine($"EC init failed: {msg}");
                    return false;
                }

                Debug.WriteLine("EC communication initialized successfully");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initialize EC communication: {ex.Message}");
                return false;
            }
        }

        protected virtual bool WriteECRegister(ushort address, ECRegisterMap registerMap, byte data)
        {
            if (!IsOpen) return false;

            lock (_lockObject)
            {
                try
                {
                    var protocol = registerMap.Protocol;
                    var addressUpper = (byte)((address >> 8) & 0xFF);
                    var addressLower = (byte)(address & 0xFF);

                    // Use device-specific protocol
                    WritePortPair(registerMap.StatusCommandPort, registerMap.DataPort,
                                 protocol.AddressSelectHigh, protocol.AddressSetHigh);
                    WritePortPair(registerMap.StatusCommandPort, registerMap.DataPort,
                                 protocol.AddressPort, addressUpper);

                    WritePortPair(registerMap.StatusCommandPort, registerMap.DataPort,
                                 protocol.AddressSelectLow, protocol.AddressSetLow);
                    WritePortPair(registerMap.StatusCommandPort, registerMap.DataPort,
                                 protocol.AddressPort, addressLower);

                    WritePortPair(registerMap.StatusCommandPort, registerMap.DataPort,
                                 protocol.DataSelect, protocol.DataCommand);
                    WritePortPair(registerMap.StatusCommandPort, registerMap.DataPort,
                                 protocol.AddressPort, data);

                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"EC register write failed: {ex.Message}");
                    return false;
                }
            }
        }
        protected virtual bool ReadECRegister(ushort address, ECRegisterMap registerMap, out byte data)
        {
            data = 0;

            if (!IsOpen)
                return false;

            lock (_lockObject)
            {
                try
                {
                    var protocol = registerMap.Protocol;
                    var addressUpper = (byte)((address >> 8) & 0xFF);
                    var addressLower = (byte)(address & 0xFF);

                    // Use device-specific protocol for address setup
                    WritePortPair(registerMap.StatusCommandPort, registerMap.DataPort,
                                 protocol.AddressSelectHigh, protocol.AddressSetHigh);
                    WritePortPair(registerMap.StatusCommandPort, registerMap.DataPort,
                                 protocol.AddressPort, addressUpper);

                    WritePortPair(registerMap.StatusCommandPort, registerMap.DataPort,
                                 protocol.AddressSelectLow, protocol.AddressSetLow);
                    WritePortPair(registerMap.StatusCommandPort, registerMap.DataPort,
                                 protocol.AddressPort, addressLower);

                    // Set up for read operation
                    WritePortPair(registerMap.StatusCommandPort, registerMap.DataPort,
                                 protocol.DataSelect, protocol.DataCommand);

                    // Use protocol-specific read sequence
                    _port!.WriteIoPortByte(registerMap.StatusCommandPort, protocol.ReadDataSelect);
                    data = _port.ReadIoPortByte(registerMap.DataPort);

                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"EC register read failed: {ex.Message}");
                    return false;
                }
            }
        }

        private void WritePortPair(ushort commandPort, ushort dataPort, byte command, byte data)
        {
            _port!.WriteIoPortByte(commandPort, command);
            _port.WriteIoPortByte(dataPort, data);
        }

        protected static byte PercentageToDuty(double percentage, byte minValue, byte maxValue)
        {
            percentage = Math.Clamp(percentage, 0.0, 100.0);
            var range = maxValue - minValue;
            var duty = (percentage / 100.0) * range + minValue;
            return (byte)Math.Round(duty);
        }

        protected static double DutyToPercentage(byte duty, byte minValue, byte maxValue)
        {
            if (maxValue <= minValue) return 0.0;

            var range = maxValue - minValue;
            var normalizedDuty = Math.Clamp(duty - minValue, 0, range);
            return (normalizedDuty / (double)range) * 100.0;
        }

        public virtual void Dispose()
        {
            if (!_disposed)
            {
                _port?.Dispose();
                _port = null;
                _disposed = true;
            }
        }
    }
}