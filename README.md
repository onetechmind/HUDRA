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

### 🏠 Performance Control

Your performance command center. Everything you need to tweak on the fly lives here.

<p align="center"><img src="/HUDRA/Assets/Screenshots/1205-Home.png" width="25%"></p>

- **TDP Control** — Scroll-based slider from 5W to 30W with instant hardware response
- **System Controls** — Volume, brightness, resolution/refresh rate, HDR, and battery status
- **FPS Limiter** — Set a framerate cap via RTSS integration (optional)

### 📚 Library

A simple game launcher. Nothing more, nothing less.

<p align="center"><img src="/HUDRA/Assets/Screenshots/1205-Library.png" width="25%"></p>

- **Multi-launcher support** — Steam, Epic, GOG, Xbox/Game Pass, Ubisoft, EA, and more
- **Cover art via SteamGridDB** — Automatically fetches artwork (requires free API key)
- **One-click launch** — Start games directly, HUDRA hides automatically
- **Custom artwork** — Swap covers with local images or alternate SGDB options

### 🌀 Fan Control

Take manual control of your thermals with custom fan curves.

<p align="center"><img src="/HUDRA/Assets/Screenshots/1205-FanControl.png" width="25%"></p>

- **Interactive curve editor** — Drag 5 temperature/speed points to shape your curve
- **Built-in presets** — Stealth (silent), Cruise (balanced), Warp (performance)
- **Real-time temp display** — See current CPU temperature while tuning

**Devices With Fan Control Support:**

| Device | Fan Curves | Fully Tested? | 
|--------|------------| ------------- |
| OneXPlayer X1 / X1 Mini / X1 Pro | ✅ | ✅ |
| OneXFly F1 / F1 Pro| ✅ | ✅ |
| Legion Go 1/S| ✅ | ❌ |
| Legion Go 2| ✅ | ✅ |
| GPD Win 4| ✅ | ✅ |
| Other AMD Ryzen handhelds | ❌ | ❌ |

**If you are interested in testing fan control support for a device not on this list, please reach out!**

### ⚡ Scaling

Toggle graphics features without digging through other apps.

<p align="center">
<img src="/HUDRA/Assets/Screenshots/1205-Scaling1.png" width="25%">&nbsp;
<img src="/HUDRA/Assets/Screenshots/1205-Scaling2.png" width="25%"> 
</p>

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

### ⚙️ Settings

Configure HUDRA to your liking.

<p align="center">
<img src="/HUDRA/Assets/Screenshots/1205-Settings1.png" width="25%">&nbsp;
<img src="/HUDRA/Assets/Screenshots/1205-Settings2.png" width="25%"> 
</p>

- **Sticky TDP** option prevents your TDP from drifting due to OEM firmware behavior
- **CPU Boost** — Enable/disable processor boost for thermal or battery management
- **Power Profile Switcher** — Select your Normal and Gaming plans for automatic switching when gaming starts and ends
- **Startup TDP** — Set your default power target on launch
- **Run at startup** — Launch HUDRA with Windows and/or RTSS and Lossless Scaling with HUDRA
- **SteamGridDB API key** — Paste your key for automatic cover art downloads in the Library
- **About & version info** -- Useful for reporting bugs!

---

## Dynamic Navbar

HUDRA's navigation bar adapts to context.

<p align="center">
<img src="/HUDRA/Assets/Screenshots/1205-Navbar.png" width="25%"> 
</p>

- **Back to Game** — Instantly return to your running game
- **Scale** — Trigger Lossless Scaling with current settings
- **Force Quit** — Kill the active game process
0 **Hide HUDRA** — 'nuff said.
These buttons only appear when a game is detected.

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

---

## Installation

**Requirements:** Windows 10 (1903+) or 11, AMD Ryzen processor, admin privileges.

1. Download the installer from [Releases](../../releases)
2. Run it — .NET 8 runtime is bundled
3. Launch from Start menu or desktop
4. Configure your defaults in Settings

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

- [RyzenAdj](https://github.com/FlyGoat/RyzenAdj) — TDP control
- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) — Hardware monitoring
- [SteamGridDB](https://www.steamgriddb.com/) — Game artwork
- [Lossless Scaling](https://store.steampowered.com/app/993090/Lossless_Scaling/) — Scaling and frame generation
- [RTSS](https://www.guru3d.com/files-details/rtss-rivatuner-statistics-server-download.html) — FPS limiting
- [GameLib.NET](https://github.com/tekgator/GameLib.NET) — Launcher detection
- [ADLX-SDK-Wrapper](https://github.com/JamesCJ60/ADLX-SDK-Wrapper) - AMD features integration

---

## Support

- [GitHub Issues](../../issues) — Bug reports
- [GitHub Discussions](../../discussions) — Feature requests & questions
- Email: lance@onetechmind.com

---

<div align="center">

**Made for the handheld gaming community**

</div>
