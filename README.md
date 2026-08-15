# HUDRA — Heads-Up Display Runtime Assistant

<div align="center">

<img src="/HUDRA/Assets/HUDRA-logo-violet.png" width="200">

**Performance control for AMD Ryzen handhelds — built for controllers and touch.**

[![Windows](https://img.shields.io/badge/Windows-10/11-blue?style=flat&logo=windows)](https://www.microsoft.com/windows)
[![AMD Ryzen](https://img.shields.io/badge/AMD-Ryzen-red?style=flat&logo=amd)](https://www.amd.com)
[![WinUI 3](https://img.shields.io/badge/WinUI-3-purple?style=flat&logo=microsoft)](https://docs.microsoft.com/en-us/windows/apps/winui/)
[![.NET 8](https://img.shields.io/badge/.NET-8-purple?style=flat&logo=dotnet)](https://dotnet.microsoft.com/)
[![License: CC BY-NC-SA 4.0](https://img.shields.io/badge/License-CC%20BY--NC--SA%204.0-lightgrey.svg)](https://creativecommons.org/licenses/by-nc-sa/4.0/)

</div>

---

## Overview

HUDRA is built for how people actually use handheld gaming PCs — frequent controls at your fingertips, dynamic buttons that appear when you need them, and a dead-simple game library. No bloat, no learning curve.

It's unapologetically **not** a full-screen launcher or "big picture" experience. HUDRA appears when you need it and gets out of your way when you don't. Double-click the tray icon or use `Win + Alt + Ctrl` to toggle visibility.

Modern design, optimized for touch and full gamepad navigation.

---

## App Pages

### 🏠 Home

Your performance/quick settings command center. Everything you need to tweak on the fly lives here.

- **TDP Control** — Scroll-based slider from 5W to 30W with instant hardware response
- **Sticky TDP** — prevents your TDP from drifting due to OEM firmware behavior
- **EPP Control** — Energy Performance Preference slider (0 = max CPU boost, 100 = max efficiency). Higher values make the CPU boost less aggressively, which on a shared-TDP APU leaves more power headroom for the GPU in GPU-bound games. Note: Windows' "Best performance" power slider can override EPP on AC power
- **System Controls** — Volume, brightness, resolution/refresh rate, HDR, and battery status
- **FPS Limiter** — Set a framerate cap via RTSS integration (optional)

<p align="center"><img src="/HUDRA/Assets/Screenshots/1205-Home.png" width="40%"></p>

#### Dynamic Navbar

HUDRA's navigation bar adapts to context.

- **Back to Game** — Instantly return to your running game
- **Force Quit** — Kill the active game process
- **Scale** — Trigger Lossless Scaling with current settings
- **Hide HUDRA** — 'nuff said.

These buttons only appear when a game is detected.

<p align="center">
<img src="/HUDRA/Assets/Screenshots/1205-Navbar.png" width="40%"> 
</p>

---

### 📚 Library

A simple game launcher. Nothing more, nothing less.

- **Multi-launcher support** — Steam, Epic, GOG, Xbox/Game Pass, Ubisoft, EA, and more
- **Cover art via SteamGridDB** — Automatically fetches artwork [(requires free API key)](https://www.steamgriddb.com/profile/preferences/api)
- **One-click launch** — Start games directly, HUDRA hides automatically
- **Custom artwork** — Swap covers with local images or alternate SGDB options

<p align="center"><img src="/HUDRA/Assets/Screenshots/1205-Library1.png" width="40%"> &nbsp; <img src="/HUDRA/Assets/Screenshots/1205-Library2.png" width="40%"> </p>

### Per-Game Profiles
- Set TDP, Resolution, Refresh Rate, FPS Limit, HDR, Fan Curve, and AMD driver settings to be applied automatically on a per-game basis! Access these settings from the Library page by:
  - Highlighting a game > pressing X to enter Game Settings (or by clicking/tapping on the gear icon on the game tile).
  - When a game profile is applied, a status bar appears in the bottom of the main HUDRA window until the game closes. Click the "Revert" button to immediately revert back to your default profile/last settings.
  - On a per-game basis, you can enable "Auto-Revert" to automatically revert to your default profile/last settings when the game closes.
- Set a default profile HUDRA can use on startup (and when reverting from per-game profiles) by making all your preferred tweaks and clicking the "Save Defaults" button on the Settings page. Replaces the simplified "Startup TDP" feature.

<p align="center">
<img width="40%" alt="Screenshot 2026-01-12 150159" src="https://github.com/user-attachments/assets/f355f894-a721-4814-9a9c-53f164a0f9b9" />
&nbsp;
<img width="40%" alt="Screenshot 2026-01-12 150621" src="https://github.com/user-attachments/assets/f6c2006c-c6f2-47d8-811b-2fc2595e80b6" />
</p>

<p align="center">
<img width="567" alt="Screenshot 2026-01-12 150516" src="https://github.com/user-attachments/assets/599bf4cf-4490-4e4c-99c4-69ce9eccf5a3" />
</p>

### 🌀 Fan Control

Take manual control of your thermals with custom fan curves.

- **Interactive curve editor** — Drag 5 temperature/speed points to shape your curve
- **Built-in presets** — Stealth (silent), Cruise (balanced), Warp (performance)
- **Real-time temp display** — See current CPU temperature while tuning

<p align="center"><img src="/HUDRA/Assets/Screenshots/1205-FanControl.png" width="40%"></p>

**Devices With Fan Control Support:**

| Device | Fan Curves | Fully Tested? | 
|--------|------------| ------------- |
| OneXPlayer X1 / X1 Mini / X1 Pro | ✅ | ✅ |
| OneXPlayer X2 Mini / X2 Mini Pro | ✅ | ✅ |
| OneXFly F1 / F1 Pro| ✅ | ✅ |
| Legion Go 1/S| ✅ | ❌ |
| Legion Go 2| ✅ | ✅ |
| GPD Win 4| ✅ | ✅ |
| GPD Win Mini (all) | ✅ | ✅ |
| Other AMD Ryzen handhelds | ❌ | ❌ |

**If you are interested in testing fan control support for a device not on this list, please reach out!**

### ⚡ Scaling

Toggle graphics features without digging through other apps.

**AMD Features:**
- **RSR** — Radeon Super Resolution (driver-level upscaling)
- **AFMF** — AMD Fluid Motion Frames (driver-level frame gen)
- **Anti-Lag** — Reduce input latency in supported titles

**Lossless Scaling Integration:**

Set preferred, common settings (or load HUDRA's default), then click Apply to restart Lossless Scaling automatically.
- **LSFG** — Frame generation at 2x, 3x, or 4x
- **Upscaler toggle** — Enable/disable LS1 upscaling
- **Flow Scale adjustment** — Fine-tune frame gen quality
- **One-button scaling** — When a game is running, activate the Lossless Scaling button in the navbar to trigger scaling and return to your game instantly!


<p align="center">
<img src="/HUDRA/Assets/Screenshots/1205-Scaling1.png" width="40%">&nbsp;
<img src="/HUDRA/Assets/Screenshots/1205-Scaling2.png" width="40%"> 
</p>

### ⚙️ Settings

Configure HUDRA to your liking.

- **Power Profile Switcher** — Select your Normal and Gaming plans for automatic switching when gaming starts and ends
- **CPU Boost** — Enable/disable processor boost for thermal or battery management (not needed for most games)
- **Game Detection** — Enable/disable Library Scanning to take advantage of dynamic navbar actions and the Library page
- **SteamGridDB API key** — Paste your key for automatic cover art downloads in the Library (key encrypted locally on your device)
- **Startup Options** — Launch HUDRA with Windows and/or RTSS and Lossless Scaling with HUDRA. Start HUDRA minimzed
- **Debug Button/Version Info** -- Useful for reporting bugs!

<p align="center">
<img src="/HUDRA/Assets/Screenshots/1205-Settings1.png" width="40%">&nbsp;
<img src="/HUDRA/Assets/Screenshots/1205-Settings2.png" width="40%"> 
</p>

---

## Controller Support

HUDRA is fully navigable with a gamepad.

| Input | Action |
|-------|--------|
| D-Pad / Left Stick | Navigate controls |
| Right Stick | Scroll Library page | 
| A | Select |
| B | Back/Cancel |
| L1 / R1 | Cycle pages |
| L2 / R2 | Cycle navbar buttons (when game running) |


Works with Xbox controllers, PlayStation (via DS4Windows), and built-in handheld controls.

### Show/Hide HUDRA

HUDRA uses `Win + Alt + Control` by default as a hotkey to show/hide the app. This can be changed on the Settings page. I suggest mapping the keybind to one of your device's function buttons using the OEM software.

---

## Installation

**Requirements:** Windows 10 (1903+) or 11, AMD Ryzen processor, admin privileges, [PawnIO](https://pawnio.eu) driver.

HUDRA controls TDP and fan curves through [PawnIO](https://pawnio.eu), a signed, sandboxed kernel driver. On first launch, HUDRA offers to download and silently install the official PawnIO driver for you (no reboot required); you can also install or reinstall it anytime from **Settings → Install PawnIO Driver**.

Because PawnIO is signed and sandboxed (unlike the WinRing0 driver older versions relied on, which is on Microsoft's vulnerable-driver blocklist), HUDRA is compatible with **Windows Memory Integrity (HVCI / Core Isolation)** — no need to disable it to use HUDRA.

1. Download the installer from [Releases](../../releases)
2. Run installer
3. (Optional) HUDRA will offer to install RTSS if you do not already have it. Recommended for frame limiting
4. Launch from Start menu — HUDRA will prompt to install the PawnIO driver if it isn't already present
5. Enjoy!

---

## Building from Source

```bash
git clone https://github.com/onetechmind/HUDRA.git
cd HUDRA
dotnet restore
dotnet build --configuration Release
```

Requires Visual Studio 2022 with Windows App SDK workload, .NET 8 SDK, and Windows 11 SDK (22000+).

---

## License

See [LICENSE.md](https://github.com/onetechmind/HUDRA/blob/0.9.9470-beta/LICENSE.md)

---

## Acknowledgments

- [PawnIO](https://pawnio.eu) — Signed, sandboxed kernel driver used for TDP and fan control
- [PawnIO Modules](https://github.com/namazso/PawnIO.Modules) — SMU/EC access modules bundled with HUDRA
- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) — Hardware monitoring
- [SteamGridDB](https://www.steamgriddb.com/) — Game artwork
- [Lossless Scaling](https://store.steampowered.com/app/993090/Lossless_Scaling/) — Scaling and frame generation
- [RTSS](https://www.guru3d.com/files-details/rtss-rivatuner-statistics-server-download.html) — FPS limiting
- [GameLib.NET](https://github.com/tekgator/GameLib.NET) — Launcher detection
- [ADLX-SDK-Wrapper](https://github.com/JamesCJ60/ADLX-SDK-Wrapper) - AMD features integration
- [Handheld Companion](https://github.com/Valkirie/HandheldCompanion)
- [RyzenAdj](https://github.com/FlyGoat/RyzenAdj) — SMU mailbox approach referenced for AMD TDP control
- [Claude Code](https://www.claude.com/product/claude-code)

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for full third-party license details.

---

## Support

- [GitHub Issues](../../issues) — Bug reports
- [/r/HUDRA on Reddit](https://reddit.com/r/HUDRA) - Support & Feedback
- Email (for anything else): lance@onetechmind.com

---

<div align="center">

**Made for the handheld gaming community**

</div>
