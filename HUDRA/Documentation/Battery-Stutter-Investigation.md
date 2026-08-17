# Battery Stutter / EPP Investigation — 2026-08-15/17

Device: ONEXPLAYER X2Mini PRO (Ryzen AI MAX+ 388 "Strix Halo", fly-family EC).
Game used for testing: Control (DX12), same scene throughout. TDP 35W via HUDRA unless noted.

## RESOLVED (2026-08-17): Windows Game Mode was the cause

Disabling **Windows Game Mode** (Settings → Gaming → Game Mode) fixed the battery
stutter/audio glitches. Game Mode applies its own invisible power-profile overrides
on top of the active scheme while a game is foreground — which explains why the
powercfg-level experiments below (EPP, DC knob mirroring) only partially helped:
they were being layered under Game Mode's own overrides the whole time.

Consequence: the **EPP feature was reverted from HUDRA** (2026-08-17) — slider,
service methods, web-remote endpoint, per-profile capture/revert all removed.
Retained from that branch: intelligent power switching now subscribes at startup
(was dead until first Settings visit) and the `[PWR]` field-log diagnostics.
The findings below are kept for reference — the powercfg/EPP mechanics
(/qh, PERFEPP2, EC TDP reset) remain true and may matter again.

## Summary

The home-page EPP feature was tested on-device, three real bugs were found and fixed
(all on branch `claude/apu-epp-control-167vks`), and a deeper battery-only
stutter/audio-glitch problem was investigated to root cause: **multi-millisecond DPCs
in the AMD GPU driver (`amdkmdag.sys`), triggered by GPU power-state churn on DC**.

## Shipped fixes (committed + pushed)

| Commit | Fix |
|---|---|
| `dc849e8` | `GetEppAsync` now uses `powercfg /qh` — plain `/query` silently omits ATTRIB_HIDE-hidden settings (PERFEPP is hidden by default on Win11), so the EPP control disabled itself on first run. Writes accept explicit GUIDs regardless of hide state. |
| `197c64c` | EPP slider moved from home page into Settings → Power Profile expander (composite element 4, under CPU Boost; gamepad: A activates, d-pad adjusts by 5). Also: intelligent power switching is now subscribed at startup — previously only wired on first Settings-page visit, so the enabled setting was dead each session until then. |
| `c3e5edf` | `SetEppAsync` also writes **PERFEPP2** (`36687f9e-…-15eb381c6865`, efficiency-class-2 cores). Strix Halo exposes it; left unset it stays at Windows defaults **33 AC / 50 DC**, so those cores ignored the slider. On-device result at 35W battery: CPU effective −300 MHz, GPU +150 MHz, +10 FPS. |

Also added edge-triggered `[PWR]` field-log diagnostics (game detect/stop, profile
switch results, EPP re-apply) to `HUDRA_Debug.log`.

## Finding 1 — EC resets TDP on every power-source change

HWiNFO logging showed package power jumping to **~55W at unplug and ~80W at replug**,
and holding there until manually reset in HUDRA (both directions, every cable event).
Sticky TDP can't catch it fast: it re-applies on a 60s timer and the Strix Halo SMU
returns 0 for live TDP reads (drift detection is blind).

**Built (this branch): TDP is re-applied immediately on every cable event.**
`PowerEventService` registers `GUID_ACDC_POWER_SOURCE` on the MainWindow HWND and
handles `PBT_POWERSETTINGCHANGE` in its existing WndProc subclass (not
`PBT_APMPOWERSTATUSCHANGE`, which also fires on battery-percentage ticks).
`Services/Power/PowerSourceTransitionTracker.cs` is the pure, unit-tested dedupe:
the initial callback Windows sends at registration only sets the baseline, repeats of
the current state are dropped, and short-term-UPS (payload 2) counts as DC.
`App.OnPowerSourceChanged` then re-asserts via `TdpMonitorService.TryReapplyNow()`
(falling back to the persisted last-used TDP), and repeats once after 2s because the
EC's reset can land after the Windows broadcast. **TDP only — nothing EPP-related.**

## Finding 2 — Windows DC power-plan defaults cause CPU perf-state oscillation

`powercfg /qh` diff of AC vs DC columns (Balanced scheme) found 13 processor knobs that
differ. Key offenders on DC: perf increase threshold **90%** (vs 30% AC), check
interval **30ms** (vs 15ms), increase policy Ideal (vs Rocket), boost policy 40 (vs 60).
Effect at identical 35W: DC 1% lows **8–15 FPS** vs AC 40–48; CPU effective clock spiky
(700–1,360 MHz vs 600–850 steady).

**Experiment applied (still active on this machine):** DC values mirrored to AC for
those four knobs on the current scheme. Result: ~half of samples recovered to healthy
47–50 FPS 1% lows; hitching reduced but not eliminated. Revert script:
`<scratchpad>/revert_dc_knobs.ps1` (originals: threshold 90, interval 30, inc policy 0,
boost policy 40).

## Finding 3 — remaining audio glitches are AMD GPU driver DPCs

Ruled out first: HUDRA fan-curve EC polling (disabled → same), HWiNFO sensor polling
(exited → same). LatencyMon unusable (driver signature rejected by this Windows build).

xperf capture (75s, in-game, battery; flags `PROC_THREAD+LOADER+DPC+INTERRUPT`):

- **`amdkmdag.sys`: 7.5 s of DPC time on CPU 0** (4.4% of the core), 155,662 DPCs,
  including 34 × 4–8ms, 17 × 8–16ms, ~9 more up to 33ms+. Audio engine period is
  ~10ms — these directly cause the dropouts.
- `Wdf01000.sys` 3.8s (secondary, KMDF on behalf of some driver), `dxgkrnl.sys` 1.25s.

Mechanism: on DC the tight budget makes the iGPU re-clock constantly; each DPM
transition runs long driver DPCs. On AC clocks are stable → few long DPCs → no glitches.

**Tested and ruled out (2026-08-16):**
1. Adrenalin driver update — no newer version available at time of testing.
2. **MPO (Multi-Plane Overlay) disable** — the top community fix for this exact
   signature (`OverlayTestMode=5` DWORD in `HKLM\SOFTWARE\Microsoft\Windows\Dwm`,
   takes effect at sign-in). Applied, rebooted, tested on battery: **no change**.
   Reverted (value deleted).

**Still untested:**
1. Adrenalin battery-side auto features: ensure global/per-game profile is not
   HYPR-RX **Eco** and **Radeon Chill** is off — both exist to re-clock the iGPU for
   power savings, i.e. exactly the DPM churn feeding the DPC storms.
2. A/B Hardware-accelerated GPU scheduling toggle (HAGS on RDNA3+ iGPUs is new-ish).
3. Retest when a new Adrenalin driver ships (highest-probability real fix; known AMD
   DPC issue class).
4. Mitigation: higher battery TDP (e.g. 40W) = steadier GPU clocks = fewer triggers.

## Online research notes (2026-08-16)

- No Strix Halo-specific Windows tunable found. Notably, **GPD Win 5 reviews (same
  silicon) report flat, stutter-free battery behavior** — suggests OEM/driver/config,
  not the APU itself. ([FinalBoss GPD Win 5 review](https://finalboss.io/gpd-win-5-2026-review-strix-halo-handheld))
- `amdkmdag.sys` DPC stutter + audio pops is a widely reported AMD issue class going
  back years; the usual suspects are MPO (ruled out here), driver regressions (fixed
  by driver updates), and GPU power-state churn.
- HYPR-RX Eco (Adrenalin 23.12.1+) and HAGS-on-APU are both recent additions aimed at
  RDNA3+ iGPUs — power-saving re-clocking features are prime suspects for DC-only
  DPC churn. ([AMD HYPR-RX profiles](https://www.amd.com/en/products/software/adrenalin/hypr-rx.html))
- Linux-side reports exist of Strix Halo (gfx1151) getting stuck in low-power idle
  clocks (ROCm #5750) — different OS, but confirms this platform's DPM is young.
- MPO references: [BrainVoyage MPO fix](https://brainvoyage.blog/multiplane-overlay-amd-fix),
  [guru3D MPO thread](https://forums.guru3d.com/threads/disabling-mpo-multiplane-overlay-in-2025.455222/).

## Machine state left after this session

- DC perf-state knobs: **reverted to Windows defaults** (2026-08-16). Note the
  experiment had landed on the Gaming scheme (`2d3d2e6b…`), not Balanced — the game
  was running when it was applied. Verified 90/30/Ideal/40 restored.
- MPO: tested with `OverlayTestMode=5`, no effect, **reverted** (value deleted — MPO
  is back at Windows default).
- EPP: restored to Windows defaults on both schemes (2026-08-17, after the feature
  was reverted) — PERFEPP/PERFEPP1 33 AC/33 DC, PERFEPP2 33 AC/50 DC.
- Windows Game Mode: **disabled by the user** (2026-08-17) — this is the fix.
- Installed this session: LatencyMon (unusable), Windows ADK (for xperf).
- HWiNFO configured: 4-sensor RTSS OSD (Average Effective Clock, CPU Package Power,
  GPU Clock Effective, GPU Utilization) + Framerate Presented via RTSS.
- Intelligent Power Switching enabled in HUDRA settings (was default-off).

## Reference: useful commands

```powershell
# Read EPP incl. hidden (AC/DC): 0x50 = 80
powercfg /qh SCHEME_CURRENT 54533251-82be-4824-96c1-47b60b740d00 36687f9e-e3a5-4dbf-b1dc-15eb381c6863

# Diff AC vs DC processor knobs: dump SUB_PROCESSOR with /qh, compare AC/DC index lines

# DPC capture + per-driver analysis (elevated)
xperf -on PROC_THREAD+LOADER+DPC+INTERRUPT -f kernel.etl
xperf -d merged.etl
xperf -i merged.etl -a dpcisr
```
