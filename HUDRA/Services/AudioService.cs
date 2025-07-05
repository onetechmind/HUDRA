using System;
using System.Runtime.InteropServices;

namespace HUDRA.Services
{
    public class AudioService
    {
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const int VK_VOLUME_MUTE = 0xAD;
        private const int KEYEVENTF_EXTENDEDKEY = 0x1;
        private const int KEYEVENTF_KEYUP = 0x2;

        // Don't track internal state - just use the button as a toggle
        public void ToggleMute()
        {
            // Simulate pressing the Volume Mute key
            keybd_event(VK_VOLUME_MUTE, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
            keybd_event(VK_VOLUME_MUTE, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        // Explicitly set mute state using CoreAudio APIs
        public void SetMute(bool mute)
        {
            try
            {
                var type = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"));
                if (type == null)
                {
                    return;
                }

                var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(type)!;
                enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var device);

                var iid = typeof(IAudioEndpointVolume).GUID;
                device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var endpoint);

                endpoint.SetMute(mute, Guid.Empty);

                Marshal.ReleaseComObject(endpoint);
                Marshal.ReleaseComObject(device);
                Marshal.ReleaseComObject(enumerator);
            }
            catch
            {
            }
        }

        // Query current system mute state
        public bool GetMuteStatus()
        {
            try
            {
                var type = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"));
                if (type == null)
                {
                    return false;
                }

                var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(type)!;
                enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var device);

                var iid = typeof(IAudioEndpointVolume).GUID;
                device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var endpoint);

                endpoint.GetMute(out var isMuted);

                Marshal.ReleaseComObject(endpoint);
                Marshal.ReleaseComObject(device);
                Marshal.ReleaseComObject(enumerator);

                return isMuted;
            }
            catch
            {
                // In case of any failure just assume not muted
                return false;
            }
        }

        // Get the current master volume level (0.0 - 1.0)
        public float GetMasterVolumeScalar()
        {
            try
            {
                var type = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"));
                if (type == null)
                {
                    return 0f;
                }

                var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(type)!;
                enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var device);

                var iid = typeof(IAudioEndpointVolume).GUID;
                device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var endpoint);

                endpoint.GetMasterVolumeLevelScalar(out var level);

                Marshal.ReleaseComObject(endpoint);
                Marshal.ReleaseComObject(device);
                Marshal.ReleaseComObject(enumerator);

                return level;
            }
            catch
            {
                return 0f;
            }
        }

        // Set the master volume level (0.0 - 1.0)
        public void SetMasterVolumeScalar(float level)
        {
            try
            {
                level = Math.Clamp(level, 0f, 1f);
                var type = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"));
                if (type == null)
                {
                    return;
                }

                var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(type)!;
                enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var device);

                var iid = typeof(IAudioEndpointVolume).GUID;
                device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var endpoint);

                endpoint.SetMasterVolumeLevelScalar(level, Guid.Empty);

                Marshal.ReleaseComObject(endpoint);
                Marshal.ReleaseComObject(device);
                Marshal.ReleaseComObject(enumerator);
            }
            catch
            {
            }
        }

        private const uint CLSCTX_ALL = 23; // Inproc + LocalServer

        // Minimal COM interfaces for CoreAudio APIs
        private enum EDataFlow
        {
            eRender,
            eCapture,
            eAll,
            EDataFlow_enum_count
        }

        private enum ERole
        {
            eConsole,
            eMultimedia,
            eCommunications,
            ERole_enum_count
        }

        [ComImport]
        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int NotImpl1();
            [PreserveSig]
            int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppDevice);
        }

        [ComImport]
        [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig]
            int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, out IAudioEndpointVolume ppInterface);
        }

        [ComImport]
        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr pNotify);
            int UnregisterControlChangeNotify(IntPtr pNotify);
            int GetChannelCount(out uint pnChannelCount);
            int SetMasterVolumeLevel(float fLevelDB, Guid pguidEventContext);
            int SetMasterVolumeLevelScalar(float fLevel, Guid pguidEventContext);
            int GetMasterVolumeLevel(out float pfLevelDB);
            int GetMasterVolumeLevelScalar(out float pfLevel);
            int SetChannelVolumeLevel(uint nChannel, float fLevelDB, Guid pguidEventContext);
            int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, Guid pguidEventContext);
            int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
            int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, Guid pguidEventContext);
            int GetMute(out bool pbMute);
            int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
            int VolumeStepUp(Guid pguidEventContext);
            int VolumeStepDown(Guid pguidEventContext);
            int QueryHardwareSupport(out uint pdwHardwareSupportMask);
            int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
        }
    }
}