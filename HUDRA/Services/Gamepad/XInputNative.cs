using System.Runtime.InteropServices;

namespace HUDRA.Services.GamepadInput
{
    /// <summary>
    /// Raw XInput interop. HUDRA reads controllers through XInput rather than
    /// the WinRT gaming-input API (WGI) because WGI routes readings to the FOREGROUND
    /// process, which is fatal for an always-on-top overlay: field logs show the
    /// pad being withdrawn from this process's view for 13 seconds after a
    /// clicked link brought the browser to the foreground, and a separate
    /// incident where reads kept succeeding for 32 seconds while returning
    /// nothing but neutral values - input that is silently dead is worse than
    /// input that errors. XInput has no such gating: a background process keeps
    /// getting real button and axis data, so the overlay stays controllable
    /// while a game or browser owns the foreground.
    /// </summary>
    internal static class XInputNative
    {
        /// <summary>XInput supports exactly four user indexes (0-3).</summary>
        public const uint MaxUserCount = 4;

        public const uint ERROR_SUCCESS = 0;
        public const uint ERROR_DEVICE_NOT_CONNECTED = 0x048F;

        // wButtons bit masks
        public const ushort XINPUT_GAMEPAD_DPAD_UP = 0x0001;
        public const ushort XINPUT_GAMEPAD_DPAD_DOWN = 0x0002;
        public const ushort XINPUT_GAMEPAD_DPAD_LEFT = 0x0004;
        public const ushort XINPUT_GAMEPAD_DPAD_RIGHT = 0x0008;
        public const ushort XINPUT_GAMEPAD_START = 0x0010;
        public const ushort XINPUT_GAMEPAD_BACK = 0x0020;
        public const ushort XINPUT_GAMEPAD_LEFT_THUMB = 0x0040;
        public const ushort XINPUT_GAMEPAD_RIGHT_THUMB = 0x0080;
        public const ushort XINPUT_GAMEPAD_LEFT_SHOULDER = 0x0100;
        public const ushort XINPUT_GAMEPAD_RIGHT_SHOULDER = 0x0200;
        public const ushort XINPUT_GAMEPAD_A = 0x1000;
        public const ushort XINPUT_GAMEPAD_B = 0x2000;
        public const ushort XINPUT_GAMEPAD_X = 0x4000;
        public const ushort XINPUT_GAMEPAD_Y = 0x8000;

        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_STATE
        {
            /// <summary>Bumps only when the state actually changed; useful in diagnostics.</summary>
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_VIBRATION
        {
            public ushort wLeftMotorSpeed;
            public ushort wRightMotorSpeed;
        }

        /// <summary>
        /// Returns ERROR_SUCCESS, ERROR_DEVICE_NOT_CONNECTED for an empty slot,
        /// or another Win32 error. Never throws, so callers branch on the code
        /// rather than wrapping every read in a try/catch.
        /// </summary>
        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        public static extern uint XInputGetState(uint dwUserIndex, out XINPUT_STATE pState);

        [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
        public static extern uint XInputSetState(uint dwUserIndex, ref XINPUT_VIBRATION pVibration);
    }
}
