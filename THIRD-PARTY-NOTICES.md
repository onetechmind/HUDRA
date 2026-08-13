# Third-Party Notices

HUDRA incorporates or interoperates with the following third-party software. Each component remains licensed under its own terms; inclusion here does not change HUDRA's own license (see [LICENSE.md](LICENSE.md)).

## PawnIO (driver & installer)

- **Author:** namazso (admin@namazso.eu)
- **Website:** https://pawnio.eu
- **Source:** https://github.com/namazso/PawnIO

HUDRA does not bundle the PawnIO driver. On first launch (and on demand from **Settings → Install PawnIO Driver**), HUDRA downloads the official `PawnIO_setup.exe` installer from the official PawnIO.Setup releases (https://github.com/namazso/PawnIO.Setup/releases) and runs it unmodified and silently. The installer's license permits unmodified redistribution.

## PawnIO Modules

- **Source:** https://github.com/namazso/PawnIO.Modules
- **License:** GNU Lesser General Public License v2.1 or later (LGPL-2.1-or-later)
- **Version:** PawnIO.Modules 0.2.10 (compiled release artifacts, unmodified)

HUDRA bundles two compiled PawnIO modules under `Tools\PawnIO\`:

- `RyzenSMU.bin` — used to drive the AMD SMU mailbox for TDP control
- `LpcIO.bin` — used for embedded controller (EC) fan control and the turbo button

These are unmodified compiled release artifacts and may be replaced by the user with their own build of the same modules. The full LGPL-2.1-or-later license text ships alongside them at `Tools\PawnIO\LICENSE-modules.txt`.

## LibreHardwareMonitorLib

- **Source:** https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
- **License:** Mozilla Public License 2.0 (MPL-2.0)

Used for temperature monitoring. As of the version HUDRA uses (0.9.6+), LibreHardwareMonitorLib itself uses PawnIO rather than WinRing0.

## Acknowledgement — SMU mailbox approach

HUDRA's AMD SMU mailbox implementation for TDP control was informed by the approaches documented in [RyzenAdj](https://github.com/FlyGoat/RyzenAdj) (FlyGoat, LGPL) and [HandheldCompanion](https://github.com/Valkirie/HandheldCompanion). No code from either project is included in HUDRA.
