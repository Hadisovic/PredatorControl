<div align="center">

![Predator Control banner](https://raw.githubusercontent.com/Hadisovic/PredatorControl/4f127a67d805aa97db9d14f82124461d0c0b1580/assets/banner.jpg)

# 🪼 Predator Control

[![Version](https://img.shields.io/badge/version-1.4.3-00E5FF?style=for-the-badge&logo=github&logoColor=white)](https://github.com/Hadisovic/PredatorControl/releases/tag/v1.4.3)
[![Jelli UI](https://img.shields.io/badge/Jelli%20UI-Desktop%20Companion-FF69B4?style=for-the-badge)](#dual-interface)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-00B4CC?style=for-the-badge&logo=windows&logoColor=white)](#quick-setup)
[![Framework](https://img.shields.io/badge/.NET-10%20Windows-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](#building-from-source)

**A focused control panel and desktop companion for compatible Acer Predator and Nitro laptops.**

[Download 1.4.3](https://github.com/Hadisovic/PredatorControl/releases/tag/v1.4.3) · [Current source](https://github.com/Hadisovic/PredatorControl/tree/revisions) · [Report an issue](https://github.com/Hadisovic/PredatorControl/issues)

</div>

## Overview

Predator Control brings laptop performance, cooling, lighting, display and battery controls into one Windows application. Choose the Classic control panel for a conventional dashboard, or Jelli for a floating companion with an expandable interface. Available controls follow the laptop's detected capabilities and installed Acer interfaces.

The current **1.4.3 source is on `revisions`** and the release is tagged `v1.4.3`. The `master` branch still contains earlier application source; its README describes the latest published release. Use `revisions` or the release tag to build 1.4.3.

## What's new in 1.4.3

- **Tabbed Classic UI:** Overview, Hardware, Keyboard, Games and Settings organize the controls.
- **Jelli spacing:** consistent card padding, clearer groups and aligned labels and switches.
- **RGB layout:** All zones, Zone 1, Zone 2, Zone 3 and Zone 4 share a dedicated selector row on supported four-zone keyboards.
- **Color choices:** presets appear beneath the zone selectors in three columns, with the small zone-color indicators restored.
- **Simpler startup:** enabling Start with Windows installs a persistent copy. A missing optional Start Menu shortcut no longer interrupts successful setup.

## Why Predator Control?

- A focused alternative interface for supported Acer laptop controls.
- Classic and companion interfaces share the same available hardware features.
- Single-file downloads, with a self-contained option.
- Profiles, tray controls and an optional gaming overlay for everyday use.

Memory usage and responsiveness vary with the machine, runtime, active interface and enabled features.

## Dual interface

| Interface | Experience |
|---|---|
| **Classic control panel** | Native Windows dashboard with five tabs, dark themes and direct access to available controls. |
| **Jelli desktop companion** | Floating jellyfish companion, live metrics, expandable dashboard and attached-flyout presentation. |

Both dashboards use **Overview · Hardware · Keyboard · Games · Settings**. Switch between Jelli and Classic in Settings. Theme choices include Teal, Crimson and OLED Black.

## Core capabilities

Features below are available only where the laptop and its interfaces support them.

| Area | Controls |
|---|---|
| **Performance** | Available Quiet, Balanced/Default, Performance, Turbo or Eco profiles; AC/battery preferences; supported hardware-key actions. |
| **Cooling** | Firmware Auto, Max and supported custom fan controls, fan curves and CoolBoost. |
| **Keyboard** | Supported single-color backlight controls, idle timeout, RGB effects, zone colors and saved lighting profiles. |
| **Display and GPU** | Available GPU/MUX modes, internal/external refresh rates and LCD overdrive. Firmware changes may require a restart. |
| **Battery** | Supported charge limiting, health/capacity/cycle information and Windows battery reports. |
| **System** | Supported startup sound/animation, power-off USB charging and key-lock options. |
| **Games** | Game Sync profiles and a configurable gaming HUD. |
| **OEM services** | Service optimization and repair options where the required Acer components are present. |

A fixed-color keyboard does not become an RGB keyboard. Unavailable readings and unsupported controls are shown accordingly; generic Predator/Nitro branding does not guarantee a particular feature.

## Gaming overlay

Choose **Light**, **Default**, **Full** or **Complete** layouts. Available readings can include FPS, temperatures, fan speeds, power, utilization and memory usage. Metric availability varies by system and game.

| Shortcut / gesture | Action |
|---|---|
| **Ctrl + Shift + O** | Toggle the gaming overlay. |
| **Ctrl + Shift + Click** | Cycle the overlay layout. |
| **Ctrl + Shift + Drag** | Move the overlay. |
| **Ctrl + Shift + Scroll** | Adjust overlay scale. |
| **Ctrl + Shift + Middle Click** | Reset scale to 100%. |

The overlay uses Windows tracing and available GPU telemetry rather than injecting a DLL into the game. Compatibility with individual games and anti-cheat configurations is not guaranteed.

## Interface previews

<details>
<summary>View the earlier Jelli, Classic and overlay previews</summary>

These screenshots are retained from the old README and show **version 1.4.2**. The 1.4.3 Classic tabs, spacing and RGB arrangement are described above. Images are linked to the archived source so they remain available on both branches without restoring generated or historical files.

<div align="center">
  <h4>Jelli — earlier expanded overview</h4>
  <a href="https://raw.githubusercontent.com/Hadisovic/PredatorControl/4f127a67d805aa97db9d14f82124461d0c0b1580/assets/preview_jelli.png"><img src="https://raw.githubusercontent.com/Hadisovic/PredatorControl/4f127a67d805aa97db9d14f82124461d0c0b1580/assets/preview_jelli.png" width="430" alt="Version 1.4.2 Jelli overview"/></a>
  <h4>Classic — earlier interface</h4>
  <a href="https://raw.githubusercontent.com/Hadisovic/PredatorControl/4f127a67d805aa97db9d14f82124461d0c0b1580/assets/preview_dashboard.png"><img src="https://raw.githubusercontent.com/Hadisovic/PredatorControl/4f127a67d805aa97db9d14f82124461d0c0b1580/assets/preview_dashboard.png" width="340" alt="Version 1.4.2 Classic dashboard before the tabbed redesign"/></a>
  <a href="https://raw.githubusercontent.com/Hadisovic/PredatorControl/4f127a67d805aa97db9d14f82124461d0c0b1580/assets/preview_controls.png"><img src="https://raw.githubusercontent.com/Hadisovic/PredatorControl/4f127a67d805aa97db9d14f82124461d0c0b1580/assets/preview_controls.png" width="340" alt="Version 1.4.2 Classic lighting controls"/></a>
  <h4>Gaming overlay layouts</h4>
  <table>
    <tr><th>Light</th><th>Default</th></tr>
    <tr>
      <td><img src="https://raw.githubusercontent.com/Hadisovic/PredatorControl/4f127a67d805aa97db9d14f82124461d0c0b1580/assets/preview_overlay_light.png" width="179" alt="Light overlay preview from version 1.4.2"/></td>
      <td><img src="https://raw.githubusercontent.com/Hadisovic/PredatorControl/4f127a67d805aa97db9d14f82124461d0c0b1580/assets/preview_overlay.png" width="328" alt="Default overlay preview from version 1.4.2"/></td>
    </tr>
    <tr><th>Full</th><th>Complete</th></tr>
    <tr>
      <td><img src="https://raw.githubusercontent.com/Hadisovic/PredatorControl/4f127a67d805aa97db9d14f82124461d0c0b1580/assets/preview_overlay_full.png" width="340" alt="Full overlay preview from version 1.4.2"/></td>
      <td><img src="https://raw.githubusercontent.com/Hadisovic/PredatorControl/4f127a67d805aa97db9d14f82124461d0c0b1580/assets/preview_overlay_complete.png" width="340" alt="Complete overlay preview from version 1.4.2"/></td>
    </tr>
  </table>
</div>

</details>

## Quick setup

Requires **Windows 10/11 x64**. Approve the Windows administrator prompt when launching the application.

| Download | Runtime requirement |
|---|---|
| [**PredatorControl-standalone.exe**](https://github.com/Hadisovic/PredatorControl/releases/download/v1.4.3/PredatorControl-standalone.exe) | Bundles .NET 10.0.14; no separate .NET installation is needed. |
| [**PredatorControl-win-x64.exe**](https://github.com/Hadisovic/PredatorControl/releases/download/v1.4.3/PredatorControl-win-x64.exe) | Requires [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0), version 10.0.14 or a compatible newer 10.0 patch. |
| [**checksums.txt**](https://github.com/Hadisovic/PredatorControl/releases/download/v1.4.3/checksums.txt) | SHA-256 checksums for both downloads. |

**Jelli requires an installed [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/).** The .NET self-contained download does not bundle the WebView2 browser runtime.

1. Download the preferred EXE from [Releases](https://github.com/Hadisovic/PredatorControl/releases/tag/v1.4.3).
2. Run it and approve administrator access.
3. Choose Classic or Jelli and configure the available controls.
4. Optionally enable **Start with Windows** in Settings.

### Start with Windows

Manual launches remain portable. Enabling Start with Windows installs a persistent copy beneath `C:\Program Files\PredatorControl\1.4.3\<build-hash>\PredatorControl.exe` and configures startup. The current downloaded session continues running.

After closing that session, you can delete the downloaded EXE. Future sign-ins use the installed copy. A Start Menu shortcut is attempted when possible; shortcut creation is optional and does not prevent startup. You can also open the installed EXE directly.

Turning startup off removes its tasks and retains the installed files. This toggle is not a full uninstall.

### Updating

Close the running application before opening a newer download. If you use Start with Windows, turn it **off and back on in the new build** to refresh the installed startup copy. Downloads and version information are available on the [release page](https://github.com/Hadisovic/PredatorControl/releases/tag/v1.4.3).

## Building from source

Use Windows x64, the **.NET 10 SDK** and **Node.js 24**. The frontend build uses npm and the committed lockfile.

```powershell
git clone --branch revisions https://github.com/Hadisovic/PredatorControl.git
cd PredatorControl

# Build and run the hardware-free checks
.\scripts\Test-Compatibility.ps1

# Self-contained single-file executable
dotnet publish src/PredatorControlApp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/standalone

# Framework-dependent single-file executable
dotnet publish src/PredatorControlApp.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish/win-x64
```

The current source has **281 hardware-free checks** covering compatibility, keyboard behavior, frontend logic and other regression cases. Passing these checks does not replace physical-laptop validation.

### Project structure

```text
src/          Windows application and hardware integration
src/Jelli/    Embedded Jelli host and UI bridge
jelli-ui/     React, TypeScript and Vite frontend
tests/        Hardware-free regression and renderer tests
scripts/      Local validation runner
tools/        Source-checking and checksum utilities
```

## Compatibility and feedback

Support depends on the exact model, firmware and installed OEM services. Preserve the Acer components required by your laptop's controls. When reporting a problem, include the app version, laptop model, Windows version and reproduction steps. A compatibility report can be exported from Settings; review its contents before sharing it.

[Open an issue](https://github.com/Hadisovic/PredatorControl/issues) · [Browse current source](https://github.com/Hadisovic/PredatorControl/tree/revisions)

## Credits and attribution

Special thanks to **[@supesonly](https://github.com/supesonly)** for the original inspiration and foundational concept ([supesonly/Acer-P-Helper](https://github.com/supesonly/Acer-P-Helper)).

<div align="center">

**Made with 🪼 and neon teal**

*Acer, Predator, Nitro and PredatorSense are trademarks of Acer Inc. Predator Control is an independent project.*

</div>
