# PawnIO Migration (WinRing0 + RyzenAdj → PawnIO)

## Overview

HUDRA previously performed all ring-0 hardware access — TDP control, EC fan control, the turbo button, and temperature monitoring — through the **WinRing0** kernel driver (via `libryzenadj.dll` / `ryzenadj.exe` for TDP, and the `Ols` class for port I/O). WinRing0 1.3.x is on Microsoft's vulnerable-driver blocklist and fails to load when **Windows Memory Integrity (HVCI / Core Isolation)** is enabled, so users had to disable a security feature to use HUDRA.

This migration replaces that entire stack with **[PawnIO](https://pawnio.eu)** (by namazso), a signed, sandboxed kernel driver that loads validated bytecode modules and is fully compatible with HVCI / Secure Boot. The approach mirrors what [Universal x86 Tuning Utility](https://github.com/JamesCJ60/Universal-x86-Tuning-Utility) and [Handheld Companion](https://github.com/Valkirie/HandheldCompanion) did: **the AMD SMU mailbox protocol is reimplemented in C#** over PawnIO's `RyzenSMU` module, and EC port I/O runs over the `LpcIO` module. No RyzenAdj code executes anymore.

**Headline result:** HUDRA no longer ships or loads WinRing0, and works with Memory Integrity turned on.

---

## Architecture

All PawnIO code lives in `Services/PawnIO/`. The design keeps the existing public surfaces (`TDPService`, `ECCommunicationBase`, `TurboService`) intact so call sites were largely untouched — only the transport underneath changed.

### Components

1. **`PawnIoFraming`** (`Services/PawnIO/PawnIoFraming.cs`)
   - Pure wire-format helpers (no P/Invoke) — the ioctl control codes and the execute-buffer layout (32-byte ASCII function name + little-endian `UInt64` args).
   - Unit-tested; linked into `HUDRA.Tests` and runnable on any platform.

2. **`PawnIoTransport`** (`Services/PawnIO/PawnIoTransport.cs`)
   - Raw `DeviceIoControl` transport, adapted from the ZenStates-Core approach (does **not** depend on `PawnIOLib.dll`).
   - Opens `\\?\GLOBALROOT\Device\PawnIO` (PawnIO ≥ 2.1.0), falling back to `\\.\PawnIO`.
   - One transport instance = one open handle = one loaded module.

3. **`PawnIoInstallService`** (`Services/PawnIO/PawnIoInstallService.cs`)
   - `Detect()` — reads `HKLM\...\Uninstall\PawnIO` (both registry views) plus a device-path probe.
   - `InstallSilent()` — **downloads the official `PawnIO_setup.exe` at runtime** from namazso's GitHub releases to a temp folder, runs it with `-install -silent`, then deletes it. The installer is *not* bundled in the repo.
   - `GetModulePath(name)` — resolves the bundled `.bin` modules under `Tools\PawnIO\`.

4. **`RyzenSmuService`** (`Services/PawnIO/RyzenSmuService.cs`) ⭐ **core**
   - Singleton that reimplements the AMD SMU mailbox handshake in C# over `RyzenSMU.bin` — a port of Handheld Companion's `RyzenSmuService` (itself a port of RyzenAdj).
   - Detects the CPU codename via CPUID, selects the mailbox addresses + TDP command IDs for that codename, probes the working mailbox (MP1 → PSMU → the module's own `ioctl_send_smu_command`), and drives set/read transactions under the cross-process `Global\Access_PCI` mutex.

5. **`LpcIoPort`** (`Services/PawnIO/LpcIoPort.cs`)
   - Drop-in replacement for the old `Ols` port-I/O class, backed by `LpcIO.bin`. Method names (`WriteIoPortByte` / `ReadIoPortByte` / `IsOpen`) match `Ols` so `ECCommunicationBase` and `TurboService` changed by one token each.

6. **`AmdCodename` / `SmuMailboxTables` / `CpuidReader`** (`Services/PawnIO/`)
   - `CpuidReader` reads family/model from CPUID leaf 1. `AmdCodenameMap.FromCpuid()` maps that to a codename. `SmuMailboxTables` holds the per-codename MP1/PSMU addresses and STAPM/fast/slow command IDs. The first two are pure and unit-tested.

7. **`TDPService`** (`Services/TDPService.cs`)
   - Rewritten from ~634 lines to a thin facade (`ITdpService`) over `RyzenSmuService.Instance`. The Lenovo Legion Go WMI path is preserved verbatim. All 9 call sites compile unchanged; the constructor is now cheap and `Dispose()` is a no-op (the SMU backend is a shared singleton).

---

## How the SMU mailbox works

TDP on AMD is set by writing three "power limit" values through the SMU (System Management Unit) mailbox. `RyzenSmuService` implements the classic handshake for one command (a message ID plus up to six 32-bit args):

1. Poll the **RSP** (response) register until it reads non-zero (mailbox idle/ready).
2. Clear RSP to 0.
3. Write the six argument slots at `ARG + i*4`.
4. Write the message ID to the **CMD** register (rings the doorbell).
5. Poll RSP until non-zero (command completed).
6. Read RSP: `0x01` = OK; other values are failure codes.
7. On OK, read the six arg slots back — this is where a query/echo value returns.

The mailbox register addresses and command IDs differ per CPU codename (see `SmuMailboxTables.cs`). The whole transaction is wrapped in the `Global\Access_PCI` named mutex so HUDRA interoperates safely with HWiNFO, Ryzen Master, and ZenStates.

**Write verification is instant.** After setting STAPM, the SMU echoes the applied value back in arg0. `SetTdp` checks `response[0] == milliwatts` — this replaces the old code's `Thread.Sleep(2000)` + read-back-and-compare, making every TDP change ~2–4 seconds faster while being a stronger correctness check.

---

## Device support

Codename detection covers HUDRA's AMD handheld fleet (CPUID family/model → codename in `AmdCodenameMap`):

| Family | Notable models | Codename |
|--------|----------------|----------|
| 0x17   | 0x60 / 0x68 / 0x90 / 0xA0 | Renoir / Lucienne / VanGogh / Mendocino |
| 0x19   | 0x44 / 0x74 / 0x75 / 0x78 | Rembrandt / Phoenix / HawkPoint / Phoenix2 |
| 0x1A   | 0x24 / 0x60 / 0x70 | StrixPoint / KrackanPoint / StrixHalo |

An unrecognized CPU reports `Unsupported CPU (...)` in diagnostics and the SMU path stays disabled rather than guessing at a mailbox. Lenovo Legion Go continues to use its WMI TDP path.

---

## Driver installation flow

HUDRA runs elevated, so no extra UAC step is needed for the silent install.

1. On startup (`App.OnLaunched`, before any hardware service initializes), `EnsurePawnIoInstalled()` calls `PawnIoInstallService.Detect()`.
2. If PawnIO is missing and the user hasn't previously declined, a Yes/No prompt offers to install it.
3. On Yes → `InstallSilent()` downloads and runs the official installer, then re-detects. On No → the choice is remembered (`PawnIoInstallDeclined` setting).
4. A **Settings → "Install PawnIO Driver"** button lets a user who declined install it later.

If PawnIO is absent, HUDRA degrades gracefully: TDP/fan/turbo report unavailable via status strings, while the Lenovo WMI path and WMI-based temperature monitoring still work.

---

## What was removed

- `Tools/ryzenadj/` in full: `libryzenadj.dll`, `ryzenadj.exe`, `WinRing0x64.dll/.sys`, `inpoutx64.dll`, and the unused RyzenAdj service scaffolding.
- The root `WinRing0x64.dll` / `WinRing0x64.sys`.
- `Services/OpenLibSys.cs` (the `Ols` class).
- All ryzenadj DLL/EXE loading, the EXE fallback, and its `0xC0000005`-treated-as-success hack.
- `LibreHardwareMonitorLib` was bumped `0.9.4 → 0.9.6`; 0.9.5+ uses PawnIO internally, so temperature monitoring now shares the same driver (removing HUDRA's third ring-0 driver). This pulled `System.Management` up to `10.0.2`.

---

## Bundled modules & licensing

- `Tools/PawnIO/RyzenSMU.bin` and `Tools/PawnIO/LpcIO.bin` — unmodified compiled modules from [namazso/PawnIO.Modules](https://github.com/namazso/PawnIO.Modules) release 0.2.10, **LGPL-2.1-or-later**. The license text ships alongside them as `Tools/PawnIO/LICENSE-modules.txt`, and the files are user-replaceable.
- The PawnIO **driver/installer** is downloaded from namazso's official releases at runtime (its license permits redistributing the installer unmodified; HUDRA doesn't repackage it).
- See `THIRD-PARTY-NOTICES.md` at the repo root for full attributions.

---

## Understanding TDP limits & ramp behavior (STAPM / SPPT / SPL)

> A reference for the common "I set 35W but it doesn't instantly sit at 35W" observation. **This is expected AMD behavior, not a HUDRA bug** — every tuning app (HUDRA, OneXConsole, etc.) behaves identically because it's a property of the silicon, not the app.

### The three limits are ceilings, not targets

HUDRA sets three power limits through the SMU:

- **STAPM** (Skin-Temperature-Aware Power Management) — a *sliding-window average* limit with a time constant. The chip eases up to this over several seconds rather than snapping to it; it lets the APU boost above the sustained limit briefly, then settle as the average catches up.
- **SPPT** (fast limit) — short-term ceiling for brief bursts.
- **SPL** (slow limit) — the sustained, long-term ceiling.

All three **cap** how much power the APU is *allowed* to draw. Actual draw is **demand-driven** by the boost algorithm. So:

- At a menu or on the desktop, the chip pulls ~5–15W because nothing needs more — that's idle downclocking, **not** throttling.
- Under a real game load, it ramps up toward the ceiling (e.g. 35W). The chip *reaching* the ceiling in-game is the proof the limit is applied correctly.
- No control app can force a constant 35W with no workload — that would just be wasted heat and battery.

The "ramp up when I get into a game" is the limit doing exactly its job. There is nothing to fix.

### The lever that changes ramp aggressiveness: time constants

If you ever want the chip to converge to the ceiling faster/slower, the tunables are the **STAPM and slow time constants** (`stapm-time`, `slow-time`), not the limit values:

- **Shorter** `stapm-time` → STAPM average converges faster; the chip settles toward the sustained limit sooner (less bursty).
- **Longer** `stapm-time` → longer sustained boost above the slow limit before the average reins it in (more bursty).

HUDRA does **not** set these today — it only writes the three limits. The SMU command IDs are `0x18` (`stapm-time`) and `0x17` (`slow-time`) on the StrixHalo/StrixPoint MP1 mailbox, and they'd be a small, self-contained addition on top of the existing `RyzenSmuService` mailbox layer if aggressive-ramp tuning is ever wanted.

### When it *is* worth investigating

The ramp behavior above is normal. But if the chip **does not reach your set TDP under a sustained, full-load game with adequate cooling**, that's a different cause — typically a thermal limit (`tctl-temp` / APU skin-temp) or the device EC's own power profile capping it, not the power limits themselves.

---

## Live-read limitation on StrixHalo (and sticky-TDP)

Reading the *current* TDP back from the SMU uses Handheld Companion's pattern: re-send the STAPM command with arg0 = 0 and read the echoed value. **This works on Phoenix/StrixPoint but returns 0 on StrixHalo (family 0x1A model 0x70) firmware** — the query is simply unsupported there (it is non-destructive; it does not set 0W). `GetTdp()` guards against this: it falls back to the last value HUDRA applied and logs the condition once per session.

Because a live read is unavailable on such chips, **sticky-TDP** (`TdpMonitorService`) was changed from read-compare-correct to **unconditional re-assert**: it re-applies your target every 60 seconds regardless of the read. The SMU write is idempotent and echo-verified, so re-writing an unchanged limit is harmless, and if a game or the firmware lowered the limit this restores it. This makes sticky-TDP robust on all chips.

The 60s cadence has one event-driven supplement: the OEM EC resets power limits on
every AC/DC cable event (measured ~55W at unplug / ~80W at replug on the X2 Mini Pro),
so `PowerEventService` registers `GUID_ACDC_POWER_SOURCE` and, on each genuine
transition, `TdpMonitorService.TryReapplyNow()` re-asserts the target immediately and
once more 2 s later — the same serialized write path the timer uses. See
`Battery-Stutter-Investigation.md`, Finding 1.

**Future enhancement:** a true live read via the SMU power-metrics table (like RyzenAdj's `get_stapm_limit`) would restore precise drift detection and enable showing actual-vs-set wattage.

---

## Verification

- **Build/test:** full solution builds with 0 errors; `HUDRA.Tests` passes 136 tests (85 baseline + 51 new covering framing, codename mapping, and mailbox tables) and runs on Linux CI.
- **On-device (StrixHalo X2 Mini Pro):** verified end to end — first-launch consent → runtime installer download → silent install → PawnIO 2.2.0.0 → `LpcIO.bin` (EC probe live) → `RyzenSMU.bin` → MP1 mailbox probe OK → echo-verified TDP sets → sticky-TDP re-assert firing at the 60s cadence, and holding/changing correctly in-game.
- **Diagnostics:** Settings → debug info reports `PawnIO Installed`, `TDP Backend` (e.g. `PawnIO SMU (StrixHalo, Mp1 mailbox)`), and current TDP. SMU log lines (`HUDRA_Debug.log`, category `SMU`/`PAWNIO`) name the wattage, e.g. `TDP set 15W (15000 mW) (STAPM ok; fast ok; slow ok)`.
