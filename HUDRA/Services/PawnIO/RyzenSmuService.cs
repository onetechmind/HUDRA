using System;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using HUDRA.Services;

namespace HUDRA.Services.PawnIO
{
    /// <summary>
    /// Shared singleton backend that drives the AMD SMU mailbox over PawnIO's
    /// RyzenSMU.bin module. A faithful C# port of HandheldCompanion's
    /// RyzenSmuService.cs (itself a port of RyzenAdj).
    ///
    /// Replaces ryzenadj entirely. NEVER throws from init or public calls: every
    /// failure surfaces as IsAvailable=false plus a human-readable Status string,
    /// or a (false, message) tuple.
    /// </summary>
    public sealed class RyzenSmuService : IDisposable
    {
        // --- Handshake / probe constants (match HC / the .p module) ---
        private const int SMU_RETRIES_MAX = 8096;
        private const uint SMU_TEST_MSG = 0x1;
        private const uint SMU_TEST_ARG = 0x47;
        private const int PciMutexWaitMs = 5000;

        private const string ModuleName = "RyzenSMU.bin";
        private const string PciMutexName = @"Global\Access_PCI";

        /// <summary>SMU response/status codes (RSP register values).</summary>
        private enum SmuStatus : uint
        {
            OK = 0x01,
            Failed = 0xFF,
            UnknownCmd = 0xFE,
            RejectedPrereq = 0xFD,
            RejectedBusy = 0xFC,
            Timeout = 0x100
        }

        /// <summary>Which transport path we send commands through.</summary>
        private enum MailboxKind
        {
            None,
            Mp1,
            Psmu,
            Ioctl
        }

        private static readonly Lazy<RyzenSmuService> _instance =
            new(() => new RyzenSmuService(), LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>Lazy, thread-safe shared instance.</summary>
        public static RyzenSmuService Instance => _instance.Value;

        // Serializes ALL public calls (SetTdp / GetTdp / Reinit). Several
        // unsynchronized call sites hit this later — this is the single
        // intra-process serialization point. The Access_PCI mutex is separate
        // (cross-process).
        private readonly object _lock = new();

        private readonly Mutex _pciMutex;

        private PawnIoTransport? _transport;
        private AmdCodename _codename = AmdCodename.Unknown;
        private MailboxKind _mailboxKind = MailboxKind.None;
        private SmuMailbox _mailbox;
        private (uint Stapm, uint Fast, uint Slow) _tdpCommands;
        private bool _initialized;
        private int? _lastAppliedTdpWatts;
        private bool _loggedImplausibleRead;

        public bool IsAvailable { get; private set; }
        public string Status { get; private set; } = "Not initialized";

        private RyzenSmuService()
        {
            _pciMutex = CreateWorldWritableMutex(PciMutexName);
        }

        // ----------------------------------------------------------------- Public API

        public (bool Success, string Message) SetTdp(int milliwatts)
        {
            lock (_lock)
            {
                EnsureInitialized();
                if (!IsAvailable)
                    return (false, Status);

                uint mW = milliwatts < 0 ? 0u : (uint)milliwatts;

                // STAPM is the gate; fast/slow are best-effort.
                var stapm = SendCommand(_tdpCommands.Stapm, new[] { mW });
                if (stapm.Status != SmuStatus.OK)
                {
                    var failMsg = $"TDP set {mW / 1000}W ({mW} mW) failed (STAPM {stapm.Status}).";
                    DebugLogger.Log(failMsg, "SMU");
                    return (false, failMsg);
                }

                // Echo verification: the SMU returns the applied limit in arg0. This is
                // the instant equivalent of HUDRA's old Thread.Sleep(2000)+readback and
                // catches a status-OK-but-silently-ignored write (cf. the 30W-after-nav
                // bug). Sending mW (>0) is the proven HC write path — distinct from the
                // risky arg-0 query GetTdp uses, so this echo is safe to trust. When the
                // module returns no echo we can't verify, so we trust the OK status.
                if (stapm.Response.Length > 0 && stapm.Response[0] != mW)
                {
                    var mismatch = $"TDP set unverified (STAPM echo {stapm.Response[0]} mW != {mW} mW).";
                    DebugLogger.Log(mismatch, "SMU");
                    return (false, mismatch);
                }

                var fast = SendCommand(_tdpCommands.Fast, new[] { mW });
                var slow = SendCommand(_tdpCommands.Slow, new[] { mW });

                _lastAppliedTdpWatts = (int)(mW / 1000);

                string msg = $"TDP set {mW / 1000}W ({mW} mW) (STAPM ok; " +
                             $"fast {(fast.Status == SmuStatus.OK ? "ok" : "failed")}; " +
                             $"slow {(slow.Status == SmuStatus.OK ? "ok" : "failed")})";
                DebugLogger.Log(msg, "SMU");
                return (true, msg);
            }
        }

        public (bool Success, int TdpWatts, string Message) GetTdp()
        {
            lock (_lock)
            {
                EnsureInitialized();
                if (!IsAvailable)
                    return (false, 0, Status);

                var result = SendCommand(_tdpCommands.Stapm, new[] { 0u });
                if (result.Status == SmuStatus.OK && result.Response.Length > 0)
                {
                    uint mW = result.Response[0];
                    int watts = (int)(mW / 1000);

                    // GUARD: the arg-0 query semantic is the riskiest assumption in
                    // the port. Only trust a sane reading. On some firmware (observed
                    // on StrixHalo, family 0x1A model 0x70) this query returns 0 rather
                    // than the current limit — we fall back to the last value we set.
                    if (watts >= 1 && watts <= 150)
                        return (true, watts, "ok");

                    // Log the unsupported-readback condition once per session, not on
                    // every poll (the sticky-TDP monitor calls this every 60s).
                    if (!_loggedImplausibleRead)
                    {
                        _loggedImplausibleRead = true;
                        DebugLogger.Log($"TDP live read returned {mW} mW (unsupported on this SMU); using last-set value", "SMU");
                    }
                }

                if (_lastAppliedTdpWatts.HasValue)
                    return (true, _lastAppliedTdpWatts.Value, "last-set");

                return (false, 0, "no TDP reading");
            }
        }

        public (bool Success, string Message) ReinitializeAfterResume()
        {
            lock (_lock)
            {
                _transport?.Dispose();
                _transport = null;
                _initialized = false;
                IsAvailable = false;
                _mailboxKind = MailboxKind.None;

                EnsureInitialized();
                return (IsAvailable, Status);
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                try { _transport?.Dispose(); }
                catch (Exception ex) { DebugLogger.Log($"Disposing transport threw: {ex.Message}", "SMU"); }
                finally { _transport = null; }

                try { _pciMutex.Dispose(); }
                catch (Exception ex) { DebugLogger.Log($"Disposing PCI mutex threw: {ex.Message}", "SMU"); }
            }
        }

        // ----------------------------------------------------------------- Init

        private void EnsureInitialized()
        {
            if (_initialized)
                return;

            _initialized = true;
            IsAvailable = false;

            try
            {
                var (transport, openMessage) = PawnIoTransport.TryOpen();
                if (transport == null)
                {
                    Status = "PawnIO not installed";
                    DebugLogger.Log($"SMU init: {openMessage}", "SMU");
                    return;
                }

                _transport = transport;

                var modulePath = PawnIoInstallService.GetModulePath(ModuleName);
                var (loaded, loadMessage) = transport.LoadModuleFromFile(modulePath);
                if (!loaded)
                {
                    Status = loadMessage;
                    DebugLogger.Log($"SMU init: {loadMessage}", "SMU");
                    return;
                }

                var (family, model) = CpuidReader.Read();
                _codename = AmdCodenameMap.FromCpuid(family, model);

                if (!SmuMailboxTables.IsSupported(_codename))
                {
                    Status = $"Unsupported CPU (family 0x{family:X} model 0x{model:X})";
                    DebugLogger.Log($"SMU init: {Status}", "SMU");
                    return;
                }

                _tdpCommands = SmuMailboxTables.TdpCommands(_codename);

                if (!ProbeMailbox())
                {
                    Status = $"SMU init failed: no working mailbox (codename {_codename})";
                    DebugLogger.Log($"SMU init: {Status}", "SMU");
                    return;
                }

                IsAvailable = true;
                Status = $"PawnIO SMU ({_codename}, {_mailboxKind} mailbox)";
                DebugLogger.Log($"SMU init: {Status} (family 0x{family:X} model 0x{model:X})", "SMU");
            }
            catch (Exception ex)
            {
                IsAvailable = false;
                Status = $"SMU init failed: {ex.Message}";
                DebugLogger.Log($"SMU init threw: {ex}", "SMU");
            }
        }

        /// <summary>
        /// Port of RyzenAdj smu_service_test. Try MP1, then PSMU, then the module's
        /// own ioctl_send_smu_command. Caches the chosen mailbox kind.
        /// </summary>
        private bool ProbeMailbox()
        {
            // MP1
            _mailbox = SmuMailboxTables.Mp1(_codename);
            var mp1 = SendMailbox(_mailbox, SMU_TEST_MSG, new[] { SMU_TEST_ARG });
            if (mp1.Status == SmuStatus.OK)
            {
                _mailboxKind = MailboxKind.Mp1;
                DebugLogger.Log("SMU probe: MP1 mailbox OK", "SMU");
                return true;
            }
            DebugLogger.Log($"SMU probe: MP1 failed ({mp1.Status})", "SMU");

            // PSMU
            _mailbox = SmuMailboxTables.Psmu(_codename);
            var psmu = SendMailbox(_mailbox, SMU_TEST_MSG, new[] { SMU_TEST_ARG });
            if (psmu.Status == SmuStatus.OK)
            {
                _mailboxKind = MailboxKind.Psmu;
                DebugLogger.Log("SMU probe: PSMU mailbox OK", "SMU");
                return true;
            }
            DebugLogger.Log($"SMU probe: PSMU failed ({psmu.Status})", "SMU");

            // ioctl_send_smu_command fallback
            var fallback = SendViaIoctl(SMU_TEST_MSG, new[] { SMU_TEST_ARG });
            if (fallback.Status == SmuStatus.OK)
            {
                _mailboxKind = MailboxKind.Ioctl;
                DebugLogger.Log("SMU probe: ioctl_send_smu_command OK", "SMU");
                return true;
            }
            DebugLogger.Log($"SMU probe: ioctl fallback failed ({fallback.Status})", "SMU");

            return false;
        }

        // ----------------------------------------------------------------- Dispatch

        private (SmuStatus Status, uint[] Response) SendCommand(uint msg, uint[] args)
        {
            switch (_mailboxKind)
            {
                case MailboxKind.Mp1:
                case MailboxKind.Psmu:
                    return SendMailbox(_mailbox, msg, args);
                case MailboxKind.Ioctl:
                    return SendViaIoctl(msg, args);
                default:
                    return (SmuStatus.Failed, Array.Empty<uint>());
            }
        }

        /// <summary>
        /// The 7-step mailbox handshake, run under the cross-process Access_PCI mutex.
        /// Port of HandheldCompanion SendMp1Command / RyzenAdj smu_service_req.
        /// </summary>
        private (SmuStatus Status, uint[] Response) SendMailbox(SmuMailbox mb, uint msg, uint[] args)
        {
            bool held = AcquirePciMutex();
            try
            {
                // 1. Poll RSP until non-zero (idle/ready).
                if (!WaitRspNonZero(mb.Rsp))
                    return (SmuStatus.Timeout, Array.Empty<uint>());

                // 2. Clear RSP.
                if (!WriteReg(mb.Rsp, 0))
                    return (SmuStatus.Failed, Array.Empty<uint>());

                // 3. Write the 6 arg slots (pad missing with 0).
                for (uint i = 0; i < 6; i++)
                {
                    uint val = i < args.Length ? args[i] : 0u;
                    if (!WriteReg(mb.Arg + i * 4, val))
                        return (SmuStatus.Failed, Array.Empty<uint>());
                }

                // 4. Write CMD = message id.
                if (!WriteReg(mb.Cmd, msg))
                    return (SmuStatus.Failed, Array.Empty<uint>());

                // 5. Poll RSP until non-zero (completion).
                if (!WaitRspNonZero(mb.Rsp))
                    return (SmuStatus.Timeout, Array.Empty<uint>());

                // 6. Read RSP status.
                var (readOk, rsp) = ReadReg(mb.Rsp);
                if (!readOk)
                    return (SmuStatus.Failed, Array.Empty<uint>());

                var status = (SmuStatus)rsp;
                if (status != SmuStatus.OK)
                    return (status, Array.Empty<uint>());

                // 7. Read back the 6 arg slots (query/echo values return here).
                var response = new uint[6];
                for (uint i = 0; i < 6; i++)
                {
                    var (argOk, argVal) = ReadReg(mb.Arg + i * 4);
                    response[i] = argOk ? argVal : 0u;
                }

                return (SmuStatus.OK, response);
            }
            finally
            {
                if (held)
                {
                    try { _pciMutex.ReleaseMutex(); }
                    catch (Exception ex) { DebugLogger.Log($"Releasing PCI mutex threw: {ex.Message}", "SMU"); }
                }
            }
        }

        /// <summary>
        /// Fallback dispatch through the module's own ioctl_send_smu_command
        /// (7 in: cmd + 6 args, 6 out).
        /// </summary>
        private (SmuStatus Status, uint[] Response) SendViaIoctl(uint msg, uint[] args)
        {
            if (_transport == null)
                return (SmuStatus.Failed, Array.Empty<uint>());

            var input = new ulong[7];
            input[0] = msg;
            for (int i = 0; i < 6; i++)
                input[i + 1] = i < args.Length ? args[i] : 0u;

            bool held = AcquirePciMutex();
            try
            {
                var (ok, output, _) = _transport.Execute("ioctl_send_smu_command", input, 6);
                if (!ok)
                    return (SmuStatus.Failed, Array.Empty<uint>());

                var response = new uint[6];
                for (int i = 0; i < output.Length && i < 6; i++)
                    response[i] = (uint)output[i];

                return (SmuStatus.OK, response);
            }
            finally
            {
                if (held)
                {
                    try { _pciMutex.ReleaseMutex(); }
                    catch (Exception ex) { DebugLogger.Log($"Releasing PCI mutex threw: {ex.Message}", "SMU"); }
                }
            }
        }

        // ----------------------------------------------------------------- Register I/O

        private bool WaitRspNonZero(uint rspAddr)
        {
            for (int retry = 0; retry < SMU_RETRIES_MAX; retry++)
            {
                var (ok, val) = ReadReg(rspAddr);
                if (ok && val != 0)
                    return true;
            }
            return false;
        }

        private (bool Success, uint Value) ReadReg(uint addr)
        {
            if (_transport == null)
                return (false, 0);

            var (ok, output, _) = _transport.Execute("ioctl_read_smu_register", new ulong[] { addr }, 1);
            if (!ok || output.Length < 1)
                return (false, 0);

            return (true, (uint)output[0]);
        }

        private bool WriteReg(uint addr, uint val)
        {
            if (_transport == null)
                return false;

            var (ok, _, _) = _transport.Execute("ioctl_write_smu_register", new ulong[] { addr, val }, 0);
            return ok;
        }

        // ----------------------------------------------------------------- Mutex

        /// <summary>
        /// Acquires the Access_PCI mutex with a bounded wait. On timeout, logs and
        /// proceeds anyway (never deadlock the app). An abandoned mutex means we now
        /// own it. Returns true if we hold it (and must release).
        /// </summary>
        private bool AcquirePciMutex()
        {
            try
            {
                return _pciMutex.WaitOne(PciMutexWaitMs);
            }
            catch (AbandonedMutexException)
            {
                // Previous owner crashed without releasing — we now own it.
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"Access_PCI wait threw, proceeding without lock: {ex.Message}", "SMU");
                return false;
            }
        }

        private static Mutex CreateWorldWritableMutex(string name)
        {
            try
            {
                var security = new MutexSecurity();
                var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
                security.AddAccessRule(new MutexAccessRule(
                    everyone, MutexRights.FullControl, AccessControlType.Allow));

                var mutex = new Mutex(false, name, out _);
                mutex.SetAccessControl(security);
                return mutex;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"Creating world-writable Access_PCI mutex failed, using plain mutex: {ex.Message}", "SMU");
                return new Mutex(false, name);
            }
        }
    }
}
