# HUDRA - Handheld Ultimate Dynamic Resource Adjuster

<div align="center">

![HUDRA Logo](Assets/HUDRA_Logo_64x64.ico)

**A sleek, modern performance control overlay for AMD Ryzen handheld gaming devices**

[![Windows](https://img.shields.io/badge/Windows-10/11-blue?style=flat&logo=windows)](https://www.microsoft.com/windows)
[![AMD Ryzen](https://img.shields.io/badge/AMD-Ryzen-red?style=flat&logo=amd)](https://www.amd.com)
[![WinUI 3](https://img.shields.io/badge/WinUI-3-purple?style=flat&logo=microsoft)](https://docs.microsoft.com/en-us/windows/apps/winui/)
[![.NET 8](https://img.shields.io/badge/.NET-8-purple?style=flat&logo=dotnet)](https://dotnet.microsoft.com/)

</div>

## ✨ Features

### 🔧 **Performance Controls**
- **TDP Adjustment**: Smooth scroll-based TDP control (5W-30W) with visual feedback
- **Resolution Management**: Quick resolution and refresh rate switching
- **Sticky TDP**: Automatic TDP correction to maintain your settings
- **Audio Feedback**: Satisfying tick sounds when adjusting settings

### 🎮 **Gaming Integration**
- **Smart Game Detection**: Automatically detects running games
- **Quick Game Switching**: One-click return to your game with visual cues
- **Machine Learning**: Learns which processes are games vs applications
- **Fullscreen Detection**: Recognizes fullscreen gaming applications

### 🎨 **Modern UI**
- **Mica Backdrop**: Beautiful translucent Windows 11-style background
- **Always on Top**: Stays accessible while gaming
- **Compact Design**: Minimal footprint, maximum functionality
- **Dark Theme**: Easy on the eyes during extended gaming sessions
- **Touch & Mouse**: Optimized for both handheld touch and desktop use

### ⚡ **System Integration**
- **Audio Controls**: Volume slider and mute toggle
- **Brightness Control**: System brightness adjustment
- **Battery Monitor**: Real-time battery status and time remaining
- **System Tray**: Runs quietly in the background
- **Turbo Button**: Global hotkey support (Win+Alt+Ctrl)

## 🚀 Quick Start

### Prerequisites
- Windows 10 version 1903 (build 18362) or later
- AMD Ryzen processor (for TDP control)
- Administrative privileges (required for hardware control)

### Installation
1. Download the latest release from [Releases](../../releases)
2. Extract the archive to your preferred location
3. **Run as Administrator** - Required for TDP control functionality
4. Configure your preferred settings in the Settings page

### First Launch
1. HUDRA will appear in the bottom-right corner of your screen
2. Set your preferred default TDP in Settings
3. Enable "Sticky TDP" for automatic TDP correction
4. Start gaming and enjoy smooth performance control!

## 🎯 Usage

### TDP Control
- **Scroll**: Use mouse wheel or touch gestures on the TDP picker
- **Click**: Tap any number to jump directly to that TDP value
- **Range**: 5W (battery saving) to 30W (maximum performance)
- **Feedback**: Visual highlighting and audio confirmation

### Navigation
- **Performance Tab**: Main controls (TDP, Resolution, Audio, Brightness)
- **Settings Tab**: Configure startup behavior and TDP correction
- **Battery Icon**: Shows current charge level and status
- **Game Button**: Appears when a game is detected (purple glow effect)

### Hotkeys
- **Win+Alt+Ctrl**: Show/hide HUDRA (global turbo button)
- **System Tray**: Double-click icon to toggle visibility
- **Close Button**: Hides to system tray (doesn't exit)

## ⚙️ Technical Details

### Architecture
- **Framework**: WinUI 3 (.NET 8)
- **TDP Control**: RyzenAdj integration with DLL optimization
- **UI Pattern**: MVVM with custom controls
- **Performance**: Direct DLL calls for sub-100ms TDP changes

### Advanced Features
- **DPI Scaling**: Automatic scaling for high-DPI displays
- **Auto-correction**: Background TDP monitoring and correction
- **Game Learning**: AI-powered game detection with persistent learning
- **Resource Efficient**: Minimal CPU/memory footprint

### Hardware Requirements
- **CPU**: AMD Ryzen 4000 series or newer
- **RAM**: 100MB+ available memory
- **Storage**: 50MB disk space
- **Graphics**: Integrated or discrete (for game detection)

## 🛠️ Building from Source

### Requirements
- Visual Studio 2022 with Windows App SDK
- .NET 8 SDK
- Windows 11 SDK (22000 or later)

### Build Steps
```bash
git clone https://github.com/yourusername/HUDRA.git
cd HUDRA
dotnet restore
dotnet build --configuration Release
```

### Dependencies
- **Microsoft.WindowsAppSDK**: WinUI 3 framework
- **System.Management**: WMI access for hardware control
- **MouseKeyHook**: Global hotkey support
- **RyzenAdj**: AMD TDP control library (included)

## 📁 Project Structure

```
HUDRA/
├── Controls/           # Custom WinUI 3 controls
│   ├── TdpPickerControl      # Smooth TDP scroll picker
│   ├── ResolutionPickerControl # Resolution/refresh rate
│   └── AudioControlsControl   # Volume and mute
├── Services/           # Core functionality
│   ├── TDPService            # RyzenAdj integration
│   ├── GameDetectionService  # Game detection & switching
│   └── WindowManagementService # Window positioning
├── Pages/              # Navigation pages
│   ├── MainPage             # Performance controls
│   └── SettingsPage         # Configuration
├── Tools/ryzenadj/     # AMD TDP control binaries
└── Assets/             # Icons and audio files
```

## 🔧 Configuration

### Settings File Location
```
%LOCALAPPDATA%\HUDRA\settings.json
```

### Key Settings
```json
{
  "TdpCorrectionEnabled": true,
  "UseStartupTdp": true,
  "StartupTdp": 15,
  "LastUsedTdp": 20
}
```

## 🐛 Troubleshooting

### Common Issues

**TDP Control Not Working**
- Ensure HUDRA is running as Administrator
- Check that RyzenAdj files are present in Tools/ryzenadj/
- Verify you have an AMD Ryzen processor

**Game Detection Issues**
- Games in non-standard directories may not be detected initially
- The system learns over time - manually switch to games a few times
- Check that the game is running in fullscreen or has significant GPU usage

**Performance Issues**
- Disable "Sticky TDP" if experiencing conflicts with other software
- Ensure no other TDP control software is running simultaneously
- Check Windows power plan settings

### Logs and Debugging
- Enable debug mode in Visual Studio for detailed logging
- Check Windows Event Viewer for application errors
- Monitor TDP values using RyzenAdj command line tools

## 🤝 Contributing

We welcome contributions! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

### Development Setup
1. Fork the repository
2. Create a feature branch
3. Follow the existing code style (C# conventions)
4. Test on actual Ryzen hardware if possible
5. Submit a pull request

### Areas for Contribution
- **Game Detection**: Improve automatic game recognition
- **UI/UX**: Enhanced visual design and animations
- **Hardware Support**: Extend to other AMD platforms
- **Localization**: Multi-language support

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

### Third-Party Licenses
- **RyzenAdj**: LGPL-3.0 License
- **WinRing0**: Custom License (see Tools/ryzenadj/)
- **MouseKeyHook**: MIT License

## 🙏 Acknowledgments

- [RyzenAdj](https://github.com/FlyGoat/RyzenAdj) - AMD TDP control foundation
- [WinRing0](http://openlibsys.org/) - Low-level hardware access
- Microsoft WinUI 3 team - Modern Windows app framework
- AMD - Ryzen platform and documentation

## 📞 Support

- **Issues**: [GitHub Issues](../../issues)
- **Discussions**: [GitHub Discussions](../../discussions)
- **Documentation**: [Wiki](../../wiki)

---

<div align="center">

**Made with ❤️ for the handheld gaming community**

[⭐ Star this repo](../../stargazers) • [🐛 Report Bug](../../issues) • [💡 Request Feature](../../issues)

</div>
