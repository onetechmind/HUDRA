using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HUDRA.Services
{
    /// <summary>
    /// Service for querying and controlling HDR (High Dynamic Range) display state.
    /// Uses Windows CCD (Connecting and Configuring Displays) API.
    /// </summary>
    public class HdrService
    {
        #region Native API Definitions

        private const int ERROR_SUCCESS = 0;

        private enum DISPLAYCONFIG_DEVICE_INFO_TYPE : uint
        {
            DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1,
            DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2,
            DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_PREFERRED_MODE = 3,
            DISPLAYCONFIG_DEVICE_INFO_GET_ADAPTER_NAME = 4,
            DISPLAYCONFIG_DEVICE_INFO_SET_TARGET_PERSISTENCE = 5,
            DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_BASE_TYPE = 6,
            DISPLAYCONFIG_DEVICE_INFO_GET_SUPPORT_VIRTUAL_RESOLUTION = 7,
            DISPLAYCONFIG_DEVICE_INFO_SET_SUPPORT_VIRTUAL_RESOLUTION = 8,
            DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9,
            DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE = 10,
            DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL = 11,
        }

        private enum DISPLAYCONFIG_COLOR_ENCODING : uint
        {
            DISPLAYCONFIG_COLOR_ENCODING_RGB = 0,
            DISPLAYCONFIG_COLOR_ENCODING_YCBCR444 = 1,
            DISPLAYCONFIG_COLOR_ENCODING_YCBCR422 = 2,
            DISPLAYCONFIG_COLOR_ENCODING_YCBCR420 = 3,
            DISPLAYCONFIG_COLOR_ENCODING_INTENSITY = 4,
        }

        [Flags]
        private enum QUERY_DISPLAY_CONFIG_FLAGS : uint
        {
            QDC_ALL_PATHS = 0x00000001,
            QDC_ONLY_ACTIVE_PATHS = 0x00000002,
            QDC_DATABASE_CURRENT = 0x00000004,
            QDC_VIRTUAL_MODE_AWARE = 0x00000010,
            QDC_INCLUDE_HMD = 0x00000020,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_SOURCE_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_RATIONAL
        {
            public uint Numerator;
            public uint Denominator;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_TARGET_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint outputTechnology;
            public uint rotation;
            public uint scaling;
            public DISPLAYCONFIG_RATIONAL refreshRate;
            public uint scanLineOrdering;
            public bool targetAvailable;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_INFO
        {
            public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
            public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
            public uint flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_2DREGION
        {
            public uint cx;
            public uint cy;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
        {
            public ulong pixelRate;
            public DISPLAYCONFIG_RATIONAL hSyncFreq;
            public DISPLAYCONFIG_RATIONAL vSyncFreq;
            public DISPLAYCONFIG_2DREGION activeSize;
            public DISPLAYCONFIG_2DREGION totalSize;
            public uint videoStandard;
            public uint scanLineOrdering;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_TARGET_MODE
        {
            public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetVideoSignalInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINTL
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_SOURCE_MODE
        {
            public uint width;
            public uint height;
            public uint pixelFormat;
            public POINTL position;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct DISPLAYCONFIG_MODE_INFO_UNION
        {
            [FieldOffset(0)]
            public DISPLAYCONFIG_TARGET_MODE targetMode;
            [FieldOffset(0)]
            public DISPLAYCONFIG_SOURCE_MODE sourceMode;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_MODE_INFO
        {
            public uint infoType;
            public uint id;
            public LUID adapterId;
            public DISPLAYCONFIG_MODE_INFO_UNION modeInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
        {
            public DISPLAYCONFIG_DEVICE_INFO_TYPE type;
            public uint size;
            public LUID adapterId;
            public uint id;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            public uint value;
            public DISPLAYCONFIG_COLOR_ENCODING colorEncoding;
            public uint bitsPerColorChannel;

            // Bit field accessors
            public bool AdvancedColorSupported => (value & 0x1) == 0x1;
            public bool AdvancedColorEnabled => (value & 0x2) == 0x2;
            public bool WideColorEnforced => (value & 0x4) == 0x4;
            public bool AdvancedColorForceDisabled => (value & 0x8) == 0x8;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            public uint enableAdvancedColor;
        }

        [DllImport("user32.dll")]
        private static extern int GetDisplayConfigBufferSizes(
            QUERY_DISPLAY_CONFIG_FLAGS flags,
            out uint numPathArrayElements,
            out uint numModeInfoArrayElements);

        [DllImport("user32.dll")]
        private static extern int QueryDisplayConfig(
            QUERY_DISPLAY_CONFIG_FLAGS flags,
            ref uint numPathArrayElements,
            [Out] DISPLAYCONFIG_PATH_INFO[] pathInfoArray,
            ref uint numModeInfoArrayElements,
            [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
            IntPtr currentTopologyId);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(
            ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO requestPacket);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigSetDeviceInfo(
            ref DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE setPacket);

        #endregion

        #region Public Methods

        /// <summary>
        /// Checks if HDR is currently enabled on the primary display.
        /// </summary>
        /// <returns>True if HDR is enabled, false otherwise.</returns>
        public bool IsHdrEnabled()
        {
            try
            {
                var (success, _, enabled, _) = GetPrimaryDisplayHdrInfo();
                return success && enabled;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HdrService: Failed to check HDR state: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks if HDR is supported on the primary display.
        /// </summary>
        /// <returns>True if HDR is supported, false otherwise.</returns>
        public bool IsHdrSupported()
        {
            try
            {
                var (success, supported, _, _) = GetPrimaryDisplayHdrInfo();
                return success && supported;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HdrService: Failed to check HDR support: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets detailed HDR information for the primary display.
        /// </summary>
        /// <returns>Tuple containing (success, supported, enabled, forceDisabled).</returns>
        public (bool Success, bool Supported, bool Enabled, bool ForceDisabled) GetHdrInfo()
        {
            return GetPrimaryDisplayHdrInfo();
        }

        /// <summary>
        /// Enables or disables HDR on the primary display.
        /// </summary>
        /// <param name="enable">True to enable HDR, false to disable.</param>
        /// <returns>True if the operation succeeded, false otherwise.</returns>
        public bool SetHdrEnabled(bool enable)
        {
            try
            {
                // First check if HDR is supported
                var (success, supported, currentlyEnabled, forceDisabled) = GetPrimaryDisplayHdrInfo();

                if (!success)
                {
                    Debug.WriteLine("HdrService: Failed to get HDR info before setting state");
                    return false;
                }

                if (!supported)
                {
                    Debug.WriteLine("HdrService: HDR is not supported on this display");
                    return false;
                }

                if (forceDisabled)
                {
                    Debug.WriteLine("HdrService: HDR is force-disabled on this display");
                    return false;
                }

                // If already in desired state, return success
                if (currentlyEnabled == enable)
                {
                    Debug.WriteLine($"HdrService: HDR already {(enable ? "enabled" : "disabled")}");
                    return true;
                }

                return SetPrimaryDisplayHdrState(enable);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HdrService: Failed to set HDR state: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Private Methods

        private (bool Success, bool Supported, bool Enabled, bool ForceDisabled) GetPrimaryDisplayHdrInfo()
        {
            // Get display paths
            int result = GetDisplayConfigBufferSizes(
                QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS,
                out uint pathCount,
                out uint modeCount);

            if (result != ERROR_SUCCESS)
            {
                Debug.WriteLine($"HdrService: GetDisplayConfigBufferSizes failed with error {result}");
                return (false, false, false, false);
            }

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

            result = QueryDisplayConfig(
                QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS,
                ref pathCount,
                paths,
                ref modeCount,
                modes,
                IntPtr.Zero);

            if (result != ERROR_SUCCESS)
            {
                Debug.WriteLine($"HdrService: QueryDisplayConfig failed with error {result}");
                return (false, false, false, false);
            }

            // Get HDR info for primary display (first active path)
            if (pathCount > 0)
            {
                var colorInfo = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO,
                        size = (uint)Marshal.SizeOf<DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>(),
                        adapterId = paths[0].targetInfo.adapterId,
                        id = paths[0].targetInfo.id
                    }
                };

                result = DisplayConfigGetDeviceInfo(ref colorInfo);

                if (result == ERROR_SUCCESS)
                {
                    return (true, colorInfo.AdvancedColorSupported, colorInfo.AdvancedColorEnabled, colorInfo.AdvancedColorForceDisabled);
                }
                else
                {
                    Debug.WriteLine($"HdrService: DisplayConfigGetDeviceInfo failed with error {result}");
                }
            }

            return (false, false, false, false);
        }

        private bool SetPrimaryDisplayHdrState(bool enable)
        {
            // Get display paths
            int result = GetDisplayConfigBufferSizes(
                QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS,
                out uint pathCount,
                out uint modeCount);

            if (result != ERROR_SUCCESS)
            {
                Debug.WriteLine($"HdrService: GetDisplayConfigBufferSizes failed with error {result}");
                return false;
            }

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

            result = QueryDisplayConfig(
                QUERY_DISPLAY_CONFIG_FLAGS.QDC_ONLY_ACTIVE_PATHS,
                ref pathCount,
                paths,
                ref modeCount,
                modes,
                IntPtr.Zero);

            if (result != ERROR_SUCCESS)
            {
                Debug.WriteLine($"HdrService: QueryDisplayConfig failed with error {result}");
                return false;
            }

            // Set HDR state for primary display
            if (pathCount > 0)
            {
                var setColorState = new DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE,
                        size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE>(),
                        adapterId = paths[0].targetInfo.adapterId,
                        id = paths[0].targetInfo.id
                    },
                    enableAdvancedColor = enable ? 1u : 0u
                };

                result = DisplayConfigSetDeviceInfo(ref setColorState);

                if (result == ERROR_SUCCESS)
                {
                    Debug.WriteLine($"HdrService: HDR {(enable ? "enabled" : "disabled")} successfully");
                    return true;
                }
                else
                {
                    Debug.WriteLine($"HdrService: DisplayConfigSetDeviceInfo failed with error {result}");
                }
            }

            return false;
        }

        #endregion
    }
}
