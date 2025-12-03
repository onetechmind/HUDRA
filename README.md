# HUDRA - Heads-Up Display Runtime Assistant

<div align="center">

<img src="/HUDRA/Assets/HUDRA-logo-violet.png" width="200">

**A powerful performance control app for AMD Ryzen handheld gaming devices — built for controllers and touch, designed for couch gaming.**

[![Windows](https://img.shields.io/badge/Windows-10/11-blue?style=flat&logo=windows)](https://www.microsoft.com/windows)
[![AMD Ryzen](https://img.shields.io/badge/AMD-Ryzen-red?style=flat&logo=amd)](https://www.amd.com)
[![WinUI 3](https://img.shields.io/badge/WinUI-3-purple?style=flat&logo=microsoft)](https://docs.microsoft.com/en-us/windows/apps/winui/)
[![.NET 8](https://img.shields.io/badge/.NET-8-purple?style=flat&logo=dotnet)](https://dotnet.microsoft.com/)
[![License: CC BY-NC-SA 4.0](https://img.shields.io/badge/License-CC%20BY--NC--SA%204.0-lightgrey.svg)](https://creativecommons.org/licenses/by-nc-sa/4.0/)

</div>

---

## Why HUDRA?

HUDRA gives you complete control over your AMD handheld's performance without the bloat. It's fast, transparent, focused, and built specifically for how we use handheld gaming PCs — whether you're using touch, mouse, or a controller.

- **Controller-first design** — Full gamepad navigation so you never need to reach for a mouse
- **Dynamic interface** — Context-aware buttons appear only when games are actively running. Allows for quick actions, such as: Back To Game, Scale via Lossless Scaling (requires app), and Force Quit.
- **No fluff** — Every feature exists because it makes gaming better

---

## ✨ Features

### 🎮 Game Library

IKR, another launcher? Nah, this one is simple. Browse, launch, and manage your games from a unified library with beautiful cover art.

- **Multi-launcher support** — Steam, Epic, GOG, Xbox/Game Pass, Ubisoft, EA, and more
- **SteamGridDB integration** — Automatically fetch cover art for your games. Easily change it to a local image or something else on SGDB via the Library page.
- **One-click launch** — Start games directly from HUDRA

### 🎛️ Performance Controls

Fine-tune your device's performance on the fly.

- **TDP Management** — Smooth scroll-based control from 5W to 30W with instant hardware response. Seriously, try scrolling it with your finger. Apple, eat your heart out.
- **Sticky TDP** — More aggressively maintains your TDP so it doesn't drift based on OEM firmware
- **Power Profiles** — Automatic switching between Windows power plans
- **CPU Boost Control** — Toggle processor boost states for thermal/battery management

### 🌀 Custom Fan Curves

Take control of your thermals with full manual fan control.

- **Interactive curve editor** — Drag temperature/speed points to create your perfect custom curve
- **Built-in presets** — Stealth (silent), Cruise (balanced), Warp (performance)
- **Real-time monitoring** — See current temps while you tune

### ⚡ AMD Graphics Features

Toggle AMD driver features without digging through Radeon Software.

- **RSR (Radeon Super Resolution)** — Driver-level upscaling for any game
- **AFMF (AMD Fluid Motion Frames)** — Driver-level frame generation
- **Anti-Lag** — Reduce input latency in supported games

### 🔳 Lossless Scaling Integration

Control Lossless Scaling directly from HUDRA when you need frame generation or upscaling in games that don't natively support it. Once profile changes are applied, LS will restart in the background with your updates (short delay).

- **LSFG (Lossless Scaling Frame Generation)** — 2x, 3x, or 4x frame multiplication
- **Upscaling toggle** — Enable/disable Lossless Scaling's LS1 upscaler
- **Flow Scale adjustment** — Fine-tune frame generation quality
- **One-button trigger** — Launch Lossless Scaling and return to your game instantly

### 📊 FPS Limiting (via RTSS)

Optional integration with RivaTuner Statistics Server for precise frame rate control.

### 🧭 Dynamic Navbar

HUDRA's navigation bar adapts to what's happening on your device.

- **Game detected?** — A controller button appears to instantly return to your game
- **Lossless Scaling** — Quick-access button to activate current Lossless Scaling settings in the detected game
- **Force quit** — Kills the current game (make sure you save!)

### 🎮 Full Controller Support

HUDRA is designed to be used entirely with a gamepad — no mouse or touch required.

- **D-Padnavigation** — Move between controls naturally (also works with Left Analog stick)
- **Face button actions** — A to select, B to go back
- **L1/R1 bumper shortcuts** — Cycle through app pages quickly.
- **L2/R2 trigger shortcuts** — Cycle through dynamic navbar buttons when a game is running. 
- **Works with XInput controllers** — Xbox, PlayStation (with DS4Windows), and built-in handheld controls

### 🖥️ System Controls

Everything else you need at your fingertips.

- **Volume & mute** — Audio control with visual feedback
- **Brightness slider** — System brightness adjustment
- **Resolution switching** — Change resolution, refresh rate, and HDR state (if supported)
- **Battery monitor** — Real-time status with time remaining estimate


## 📱 Supported Devices

### Full Support (TDP + Fan Control)

| Device | TDP Control | Fan Curves | Notes |
|--------|-------------|------------|-------|
| OneXPlayer X1 | ✅ | ✅ | X1, X1 Mini, X1 Pro |
| OneXFly F1 | ✅ | ✅ | F1, F1 Pro |
| Legion Go 2 | ✅ | ✅  |  |
| GPD Win 4 | ✅ | ✅ | |

### TDP Only
- Any AMD Ryzen-based handheld should work for all features aside from Fan Control. Community testing welcome!

---

## 🚀 Installation

### Requirements

- Windows 10 (1903+) or Windows 11
- AMD Ryzen processor
- Administrator privileges (required for hardware control)
- .NET 8 Desktop Runtime (included in installer)

### Quick Start

1. Download the latest `.exe` installer from [Releases](../../releases)
2. Run the installer — it will prompt for administrator privileges
3. Launch HUDRA from the Start menu or desktop shortcut
4. Configure your defaults in Settings

### First Launch

1. HUDRA appears in the corner of your screen
2. Set your preferred startup TDP in Settings
3. Browse your Library and start gaming!

---

## 🎮 Usage

### Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| Win + Alt + Ctrl | Toggle HUDRA visibility (global hotkey) |

### System Tray

- **Double-click** the tray icon to show/hide HUDRA
- **Right-click** for quick actions and exit
- Closing HUDRA hides it to tray — it doesn't exit

---

## ⚙️ Configuration

### Settings Location

```
%LOCALAPPDATA%\HUDRA\settings.json
```

## 🛠️ Building from Source

### Requirements

- Visual Studio 2022 with Windows App SDK workload
- .NET 8 SDK
- Windows 11 SDK (22000 or later)

### Build Steps

```bash
git clone https://github.com/onetechmind/HUDRA.git
cd HUDRA
dotnet restore
dotnet build --configuration Release
```

## 🤝 Contributing

We welcome contributions! Whether it's device support, bug fixes, or new features.

### Ways to Help

- **Device Testing** — Test on your hardware and report issues
- **EC Documentation** — Help reverse-engineer fan control for new devices
- **Bug Reports** — File detailed issues with device model and steps to reproduce
- **Feature Requests** — Start a discussion for new ideas

### Development Setup

1. Fork the repository
2. Create a feature branch
3. Follow existing code patterns (service-based architecture, WinUI 3 conventions)
4. Test on actual hardware if possible
5. Submit a pull request

---

## 📄 License

This project is licensed under **Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International (CC BY-NC-SA 4.0)**.

You are free to:
- **Share** — Copy and redistribute the material
- **Adapt** — Remix, transform, and build upon the material

Under the following terms:
- **Attribution** — Give appropriate credit and link to the license
- **NonCommercial** — No commercial use without permission
- **ShareAlike** — Derivatives must use the same license

For commercial licensing inquiries, please open an issue or contact the maintainers.

See [LICENSE](LICENSE) for full details.

### Third-Party Licenses

- **RyzenAdj** — LGPL-3.0 License
- **WinRing0** — Custom License (see Tools/ryzenadj/)
- **LibreHardwareMonitor** — MPL-2.0 License

---

## 🙏 Acknowledgments

- [RyzenAdj](https://github.com/FlyGoat/RyzenAdj) — AMD TDP control foundation
- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) — Hardware monitoring
- [SteamGridDB](https://www.steamgriddb.com/) — Game artwork
- [Lossless Scaling](https://store.steampowered.com/app/993090/Lossless_Scaling/) — Frame generation
- [RTSS](https://www.guru3d.com/files-details/rtss-rivatuner-statistics-server-download.html) — FPS limiting
- [GameLib.NET](https://github.com/tekgator/GameLib.NET)

---

## 📞 Support

- **Issues**: [GitHub Issues](../../issues)
- **Discussions**: [GitHub Discussions](../../discussions)
- **Emaill:** lance@onetechmind.com

---

<div align="center">

**Made with ❤️ for the handheld gaming community**

[⭐ Star this repo](../../stargazers) • [🐛 Report Bug](../../issues/new?template=bug_report.md) • [💡 Request Feature](../../issues/new?template=feature_request.md)

</div>
