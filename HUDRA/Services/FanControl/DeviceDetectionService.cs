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
            typeof(OneXPlayerX1Device),
            typeof(GPDDevice)
        };

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