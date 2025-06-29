using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace HUDRA.Services
{
    public class ResolutionService
    {
        [DllImport("user32.dll")]
        private static extern int EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

        [DllImport("user32.dll")]
        private static extern int ChangeDisplaySettings(ref DEVMODE devMode, int flags);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        private const int ENUM_CURRENT_SETTINGS = -1;
        private const int ENUM_REGISTRY_SETTINGS = -2;
        private const int CDS_UPDATEREGISTRY = 0x01;
        private const int CDS_TEST = 0x02;
        private const int DISP_CHANGE_SUCCESSFUL = 0;
        private const int DISP_CHANGE_RESTART = 1;
        private const int DISP_CHANGE_FAILED = -1;

        [StructLayout(LayoutKind.Sequential)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public short dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct DISPLAY_DEVICE
        {
            [MarshalAs(UnmanagedType.U4)]
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            [MarshalAs(UnmanagedType.U4)]
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        public class Resolution
        {
            public int Width { get; set; }
            public int Height { get; set; }
            public int RefreshRate { get; set; }

            public string DisplayText => $"{Width}×{Height}";
            public string FullDisplayText => $"{Width}×{Height}@{RefreshRate}Hz";

            public override string ToString() => DisplayText;

            public override bool Equals(object obj)
            {
                if (obj is Resolution other)
                    return Width == other.Width && Height == other.Height;
                return false;
            }

            public override int GetHashCode() => HashCode.Combine(Width, Height);
        }

        public (bool Success, Resolution CurrentResolution, string Message) GetCurrentResolution()
        {
            try
            {
                var devMode = new DEVMODE();
                devMode.dmSize = (short)Marshal.SizeOf(devMode);

                if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref devMode) != 0)
                {
                    var resolution = new Resolution
                    {
                        Width = devMode.dmPelsWidth,
                        Height = devMode.dmPelsHeight,
                        RefreshRate = devMode.dmDisplayFrequency
                    };

                    return (true, resolution, "Success");
                }

                return (false, null, "Failed to get current resolution");
            }
            catch (Exception ex)
            {
                return (false, null, $"Exception: {ex.Message}");
            }
        }

        public List<Resolution> GetAvailableResolutions()
        {
            var resolutions = new HashSet<Resolution>();
            var devMode = new DEVMODE();
            devMode.dmSize = (short)Marshal.SizeOf(devMode);

            int modeIndex = 0;
            while (EnumDisplaySettings(null, modeIndex, ref devMode) != 0)
            {
                // Filter for common gaming resolutions and reasonable refresh rates
                if (devMode.dmPelsWidth >= 1024 && devMode.dmPelsHeight >= 768 &&
                    devMode.dmDisplayFrequency >= 60 && devMode.dmBitsPerPel >= 24)
                {
                    resolutions.Add(new Resolution
                    {
                        Width = devMode.dmPelsWidth,
                        Height = devMode.dmPelsHeight,
                        RefreshRate = devMode.dmDisplayFrequency
                    });
                }
                modeIndex++;
            }

            // Return sorted by resolution (width first, then height)
            return resolutions
                .OrderBy(r => r.Width)
                .ThenBy(r => r.Height)
                .ThenByDescending(r => r.RefreshRate)
                .ToList();
        }

        public (bool Success, string Message) SetResolution(Resolution resolution)
        {
            try
            {
                var devMode = new DEVMODE();
                devMode.dmSize = (short)Marshal.SizeOf(devMode);

                // Get current settings first
                if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref devMode) == 0)
                {
                    return (false, "Failed to get current display settings");
                }

                // Update only the resolution
                devMode.dmPelsWidth = resolution.Width;
                devMode.dmPelsHeight = resolution.Height;
                devMode.dmDisplayFrequency = resolution.RefreshRate;
                devMode.dmFields = 0x180000 | 0x400000; // DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY

                // Test the change first
                int result = ChangeDisplaySettings(ref devMode, CDS_TEST);
                if (result != DISP_CHANGE_SUCCESSFUL)
                {
                    return (false, "Resolution test failed - not supported");
                }

                // Apply the change
                result = ChangeDisplaySettings(ref devMode, CDS_UPDATEREGISTRY);

                switch (result)
                {
                    case DISP_CHANGE_SUCCESSFUL:
                        return (true, $"Resolution changed to {resolution.DisplayText}");
                    case DISP_CHANGE_RESTART:
                        return (false, "Resolution change requires restart");
                    case DISP_CHANGE_FAILED:
                        return (false, "Resolution change failed");
                    default:
                        return (false, $"Unknown error: {result}");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Exception: {ex.Message}");
            }
        }

        public List<int> GetAvailableRefreshRates(Resolution resolution)
        {
            var refreshRates = new HashSet<int>();
            var devMode = new DEVMODE();
            devMode.dmSize = (short)Marshal.SizeOf(devMode);

            int modeIndex = 0;
            while (EnumDisplaySettings(null, modeIndex, ref devMode) != 0)
            {
                // Find all refresh rates for the specified resolution
                if (devMode.dmPelsWidth == resolution.Width &&
                    devMode.dmPelsHeight == resolution.Height &&
                    devMode.dmDisplayFrequency >= 60 && // Only include 60Hz and above
                    devMode.dmBitsPerPel >= 24) // Only include true color modes
                {
                    refreshRates.Add(devMode.dmDisplayFrequency);
                }
                modeIndex++;
            }

            // Return sorted refresh rates (ascending order)
            return refreshRates.OrderBy(rate => rate).ToList();
        }

        public (bool Success, int RefreshRate, string Message) GetCurrentRefreshRate()
        {
            try
            {
                var devMode = new DEVMODE();
                devMode.dmSize = (short)Marshal.SizeOf(devMode);

                if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref devMode) != 0)
                {
                    return (true, devMode.dmDisplayFrequency, "Success");
                }

                return (false, 0, "Failed to get current refresh rate");
            }
            catch (Exception ex)
            {
                return (false, 0, $"Exception: {ex.Message}");
            }
        }

        public (bool Success, string Message) SetRefreshRate(Resolution resolution, int refreshRate)
        {
            try
            {
                var devMode = new DEVMODE();
                devMode.dmSize = (short)Marshal.SizeOf(devMode);

                // Get current settings first
                if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref devMode) == 0)
                {
                    return (false, "Failed to get current display settings");
                }

                // Update resolution and refresh rate
                devMode.dmPelsWidth = resolution.Width;
                devMode.dmPelsHeight = resolution.Height;
                devMode.dmDisplayFrequency = refreshRate;
                devMode.dmFields = 0x180000 | 0x400000; // DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY

                // Test the change first
                int result = ChangeDisplaySettings(ref devMode, CDS_TEST);
                if (result != DISP_CHANGE_SUCCESSFUL)
                {
                    return (false, $"Refresh rate {refreshRate}Hz test failed for {resolution.DisplayText}");
                }

                // Apply the change
                result = ChangeDisplaySettings(ref devMode, CDS_UPDATEREGISTRY);

                switch (result)
                {
                    case DISP_CHANGE_SUCCESSFUL:
                        return (true, $"Refresh rate changed to {refreshRate}Hz");
                    case DISP_CHANGE_RESTART:
                        return (false, "Refresh rate change requires restart");
                    case DISP_CHANGE_FAILED:
                        return (false, "Refresh rate change failed");
                    default:
                        return (false, $"Unknown error: {result}");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Exception: {ex.Message}");
            }
        }
    }
}