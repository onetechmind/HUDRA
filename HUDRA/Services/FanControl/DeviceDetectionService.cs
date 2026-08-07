using System;
using System.Collections.Generic;
using System.Diagnostics;
using HUDRA.Services.FanControl.Devices;

namespace HUDRA.Services.FanControl
{
    public class DeviceDetectionService
    {
        private static readonly List<Type> SupportedDeviceTypes = new()
        {
            typeof(OneXPlayerX2Device), // Strict DMI match on X2 Mini - check first
            typeof(OneXFlyF1Device),    // Check F1 series next (more specific than X1)
            typeof(OneXPlayerX1Device), // Then check X1 series
            typeof(GPDWinMiniDevice),   // Check Win Mini before generic GPD
            typeof(GPDDevice),
            typeof(LenovoLegionGoDevice) // Lenovo Legion Go / Legion Go 2
        };

        private static IFanControlDevice? _lastDetectedDevice;

        public static IFanControlDevice? LastDetectedDevice => _lastDetectedDevice;

        public static IFanControlDevice? DetectDevice()
        {
            foreach (var deviceType in SupportedDeviceTypes)
            {
                try
                {
                    if (Activator.CreateInstance(deviceType) is IFanControlDevice device)
                    {
                        if (device.IsDeviceSupported() && device.Initialize())
                        {
                            Debug.WriteLine($"Successfully detected: {device.ManufacturerName} {device.DeviceName}");
                            _lastDetectedDevice = device;
                            return device;
                        }

                        device.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to initialize {deviceType.Name}: {ex.Message}");
                }
            }

            return null;
        }
    }
}