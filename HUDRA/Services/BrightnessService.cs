using System;
using System.Management;

namespace HUDRA.Services
{
    public class BrightnessService
    {
        public int GetBrightness()
        {
            try
            {
                using var mclass = new ManagementClass("WmiMonitorBrightness");
                mclass.Scope = new ManagementScope(@"\\.\root\wmi");
                foreach (ManagementObject instance in mclass.GetInstances())
                {
                    return Convert.ToInt32(instance["CurrentBrightness"]);
                }
            }
            catch
            {
            }
            return 0;
        }

        public void SetBrightness(int brightness)
        {
            try
            {
                brightness = Math.Clamp(brightness, 0, 100);
                using var mclass = new ManagementClass("WmiMonitorBrightnessMethods");
                mclass.Scope = new ManagementScope(@"\\.\root\wmi");
                foreach (ManagementObject instance in mclass.GetInstances())
                {
                    object[] args = { 1, brightness };
                    instance.InvokeMethod("WmiSetBrightness", args);
                    break;
                }
            }
            catch
            {
            }
        }
    }
}
