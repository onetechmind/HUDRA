using System;
using System.Management;
using System.Diagnostics;
using HUDRA.Services.PawnIO;

namespace HUDRA.Services
{
    /// <summary>
    /// Minimal surface consumed by the app's TDP call sites. Lets those sites depend
    /// on an abstraction instead of the concrete TDPService.
    /// </summary>
    public interface ITdpService
    {
        (bool Success, string Message) SetTdp(int tdpInMilliwatts);
        (bool Success, int TdpWatts, string Message) GetCurrentTdp();
        string InitializationStatus { get; }
        (bool Success, string Message) ReinitializeAfterResume();
    }

    /// <summary>
    /// Facade over the PawnIO SMU backend (<see cref="RyzenSmuService"/>) for CPU TDP,
    /// plus the Lenovo WMI path for Lenovo handhelds. Replaces the old ryzenadj
    /// DLL/EXE integration entirely.
    /// </summary>
    public class TDPService : ITdpService, IDisposable
    {
        // Lenovo WMI Capability IDs for CPU power limits (from HandheldCompanion)
        private const int CAP_CPU_SHORT_TERM_POWER_LIMIT = 0x0101FF00;  // SPL / STAPM
        private const int CAP_CPU_LONG_TERM_POWER_LIMIT = 0x0102FF00;   // Slow limit
        private const int CAP_CPU_PEAK_POWER_LIMIT = 0x0103FF00;        // Fast limit
        private const int CAP_APU_SPPT_POWER_LIMIT = 0x0105FF00;        // APU sPPT

        public string InitializationStatus
        {
            get
            {
                var device = HardwareDetectionService.GetDetectedDevice();
                if (device.IsLenovo && device.SupportsLenovoWmi)
                    return "Lenovo WMI Mode";
                return RyzenSmuService.Instance.Status;
            }
        }

        /// <summary>
        /// True when the PawnIO SMU backend is available.
        /// </summary>
        public bool IsSmuMode => RyzenSmuService.Instance.IsAvailable;

        public TDPService()
        {
            // Cheap now: no LoadLibrary / dependency loading. The SMU backend
            // initializes lazily on first use via RyzenSmuService.Instance, and the
            // Lenovo WMI path is detected per-call in SetTdp. Constructing a
            // TDPService per operation (`using var tdpService = new TDPService()`) is
            // therefore safe and effectively free.
        }

        public (bool Success, int TdpWatts, string Message) GetCurrentTdp()
        {
            // Lenovo handhelds are Phoenix/HawkPoint, so the SMU read path works for
            // them too — a single SMU read serves every device.
            return RyzenSmuService.Instance.GetTdp();
        }

        public (bool Success, string Message) SetTdp(int tdpInMilliwatts)
        {
            int tdpWatts = tdpInMilliwatts / 1000;

            // Check if Lenovo device with WMI support - use WMI exclusively
            var device = HardwareDetectionService.GetDetectedDevice();
            if (device.IsLenovo && device.SupportsLenovoWmi)
            {
                Debug.WriteLine($"[TDP] Setting TDP to {tdpWatts}W via WMI (Lenovo device)");
                return SetTdpWmi(tdpInMilliwatts);
            }

            // Non-Lenovo: drive the AMD SMU over PawnIO.
            var r = RyzenSmuService.Instance.SetTdp(tdpInMilliwatts);
            if (r.Success)
                return r;

            // SMU failed — mirror the old DLL->WMI degrade chain (minus the EXE path):
            // attempt the generic WMI fallback, and only report success if WMI worked.
            DebugLogger.Log($"SMU SetTdp failed ({r.Message}); trying WMI fallback", "TDP");
            var w = SetTdpWmi(tdpInMilliwatts);
            if (w.Success)
                return (true, w.Message + " (WMI fallback)");

            return r;
        }

        private (bool Success, string Message) SetTdpWmi(int tdpInMilliwatts)
        {
            try
            {
                int tdpWatts = tdpInMilliwatts / 1000;
                Debug.WriteLine($"[TDP] Setting TDP via Lenovo WMI: {tdpWatts}W");

                // Use LENOVO_OTHER_METHOD.SetFeatureValue (same as HandheldCompanion)
                using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM LENOVO_OTHER_METHOD");

                foreach (ManagementObject instance in searcher.Get())
                {
                    try
                    {
                        // Set all CPU power limits to the same value
                        // Note: Return codes are unreliable - actual success is verified by caller
                        int[] capabilityIds = {
                            CAP_CPU_SHORT_TERM_POWER_LIMIT,  // SPL/STAPM
                            CAP_CPU_LONG_TERM_POWER_LIMIT,   // Slow
                            CAP_CPU_PEAK_POWER_LIMIT,        // Fast
                            CAP_APU_SPPT_POWER_LIMIT         // APU sPPT
                        };

                        foreach (int capId in capabilityIds)
                        {
                            try
                            {
                                var inParams = instance.GetMethodParameters("SetFeatureValue");
                                inParams["IDs"] = capId;
                                inParams["value"] = tdpWatts;
                                instance.InvokeMethod("SetFeatureValue", inParams, null);
                            }
                            catch { /* Ignore - return codes unreliable anyway */ }
                        }

                        // Return success - actual verification done by caller
                        return (true, $"WMI calls completed for {tdpWatts}W");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[TDP] WMI method call failed: {ex.Message}");
                    }
                }

                return (false, "LENOVO_OTHER_METHOD not available");
            }
            catch (ManagementException)
            {
                return (false, "WMI not available (non-Lenovo device)");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TDP] WMI exception: {ex.Message}");
                return (false, $"WMI Exception: {ex.Message}");
            }
        }

        public (bool Success, string Message) ReinitializeAfterResume()
        {
            return RyzenSmuService.Instance.ReinitializeAfterResume();
        }

        public void Dispose()
        {
            // NO-OP by design. The SMU backend (RyzenSmuService.Instance) is a shared
            // singleton and MUST NOT be disposed here: many call sites do
            // `using var tdpService = new TDPService()` per operation, so tearing down
            // the shared backend would kill it for every other caller. TDPService no
            // longer owns any per-instance unmanaged state (no LoadLibrary handles),
            // so there is nothing to release. The singleton's real teardown happens at
            // app shutdown (Phase 5).
        }
    }
}
