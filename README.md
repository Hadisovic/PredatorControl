<div align="center">

![Predator Control Banner](assets/banner.jpg)

# 🪼 Predator Control

[![Version](https://img.shields.io/badge/version-1.4.2-00E5FF?style=for-the-badge&logo=github&logoColor=white)](https://github.com/Hadisovic/PredatorControl/releases)
[![Jelli UI](https://img.shields.io/badge/Jelli%20UI-Live%20%F0%9F%AA%BC-FF69B4?style=for-the-badge&logo=sparkles&logoColor=white)](#-dual-interface-classic-dashboard--jelli-companion)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-00B4CC?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/Hadisovic/PredatorControl)
[![Framework](https://img.shields.io/badge/.NET-10.0%20Windows-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-2ED573?style=for-the-badge)](LICENSE)
[![RAM](https://img.shields.io/badge/RAM%20Usage-%7E18--35%20MB-FF6B35?style=for-the-badge&logo=memory&logoColor=white)](https://github.com/Hadisovic/PredatorControl)

<br/>

**Predator Control** is a fast, lightweight, bloat-free alternative to Acer PredatorSense and NitroSense for **Acer Predator & Nitro** gaming laptops.  
Direct hardware control, **<1ms zero-lag UI responsiveness**, **~18–35 MB RAM** (vs. 1.5 GB+), and zero background telemetry!

</div>

---

### 🖼️ UI & Gaming Overlay Previews

<div align="center">
  <h4>🎮 In-Game Gaming Overlay HUD (Zero-Injection ETW FPS Counter)</h4>
  <p><img src="assets/preview_overlay.png" width="620" alt="In-Game Gaming Overlay HUD"/></p>
  <br/>
  <table>
    <tr>
      <td align="center"><b>Dashboard, Thermals & GPU MUX Switch</b></td>
      <td align="center"><b>RGB Lighting & System Controls</b></td>
    </tr>
    <tr>
      <td><img src="assets/preview_dashboard.png" width="340" alt="Dashboard Preview"/></td>
      <td><img src="assets/preview_controls.png" width="340" alt="Controls Preview"/></td>
    </tr>
  </table>
</div>

---

## ✨ Why Predator Control?

Acer PredatorSense and Care Center bundle **10+ background services and telemetry agents**, an Electron frontend, and consume **500 MB – 1.5 GB of RAM**.  
Predator Control replaces all of that with direct ACPI WMI and official OEM bridge calls—using **~18–25 MB RAM** in Classic UI mode (~35–55 MB with Jelli UI active) while safely disabling OEM bloatware.

| Metric | 🐌 Acer PredatorSense + Care Center | 🪼 Predator Control | Advantage |
| :--- | :--- | :--- | :--- |
| **Memory Footprint** | 500 MB – 1,500+ MB | **~18–25 MB** (Classic) / **~35–55 MB** (Jelli) | **~97% Less RAM** |
| **UI Responsiveness** | 1–3s lag on mode switches | **<1ms Instant Zero-Lag Response** | **Instantaneous** |
| **Background Bloatware** | 6–10 persistent telemetry services | **0 Telemetry Services** (Safely disabled) | **Zero Bloat** |
| **Installer Size** | ~650 MB+ installer package | **~3.9 MB** (or ~50 MB standalone) | **Portable Single-File** |

---

## 🪼 Dual Interface: Classic Dashboard & Jelli Companion

Choose the interface that fits your workflow at any time:
- **Classic Control Panel**: Native WinForms dark dashboard with ~18–25 MB RAM footprint, DPI-aware multi-monitor scaling, and instant access to every hardware control on a single scrollable view.
- **Jelli Desktop Companion**: Bioluminescent jellyfish mascot and glassmorphism control surface (Overview, Cooling, Keyboard, Games, Settings tabs) that reacts to live thermals and power modes.
- **True Zero Footprint When Toggled Off**: Switching to Classic UI completely terminates all WebView2 background processes (`0 MB RAM` / `0% CPU` overhead).

---

## 🚀 Core Capabilities

### ⚡ Performance Profiles & Hotkeys
- **Predator & Nitro Profiles**: Native support for **Quiet**, **Balanced / Default**, **Performance**, **Turbo** (AC-only), and **Eco** modes across Predator Helios, Helios Neo, and Nitro series.
- **Hardware Key Integration**: Intercepts physical **Mode** and **Predator / NitroSense** keys (`WH_KEYBOARD_LL` & WMI events) for instant focus-safe mode cycling and window toggling.
- **AC / Battery Auto-Switch**: Automatically transitions power profiles when plugging in or unplugging AC power.
- **Game Sync**: Automatically binds performance modes, fan presets, and RGB lighting to specific game `.exe` launches.

### 🌡️ Thermal Management & Fans
- **Fan Modes**: **Auto** (firmware curve), **Max** (100% emergency cooling), and **Custom** independent CPU/GPU sliders.
- **Interactive Fan Curve Editor**: Multi-point temperature-to-RPM curve designer with automatic **Hardware Thermal Floor Protection** (`>=80°C -> 55%` up to `>=95°C -> 100%`) and crash-resilient startup recovery.
- **CoolBoost™**: One-click toggle raising the firmware thermal fan ceiling on supported models.

### 🎮 Physical GPU MUX Switch & Display
- **Hardware MUX Switching**: Switch between **Hybrid (Optimus)**, **Discrete GPU Only (NVIDIA)**, and **Automatic (Advanced Optimus)** with live hardware readback.
- **Display & Refresh Rate Control**: Toggle **LCD Overdrive (3ms)** and switch internal/external monitor refresh rates (`60Hz ↔ 165Hz/240Hz`) via native Win32 CCD APIs.

### 🌈 Keyboard Backlighting & 4-Zone RGB
- **4-Zone RGB (Pulsar)**: 9 hardware effects (Static, Breathing, Neon, Wave, Shifting, Zoom, Meteor, Twinkling, Off), per-zone custom color pickers, and saved profile management.
- **Monochrome / Red Backlight Support**: Automatically detects single-color Nitro keyboards, providing brightness and 30-second idle sleep timeout controls while blocking incompatible RGB packets.

### 🖥️ System, Battery & OEM Controls
- **80% Battery Charge Limiter**: Preserves battery lifespan with verified hardware readback.
- **Live Battery Health Diagnostics**: Displays real-time battery health %, wear level, cycle count, capacity (`mWh`), and generates 1-click Windows Battery Reports.
- **Startup Animation & Sound**: Toggles the BIOS POST boot chime and logo directly from Windows.
- **Power-Off USB Charging**: Turns your designated USB-A port into a power bank while the laptop is shut down or asleep (with an automatic 30% battery cutoff reserve).
- **Windows & Menu Key Lock**: Hardware matrix and low-level lock preventing accidental key presses in games.

> [!TIP]
> **POST Boot Chime:** If boot sound was previously muted in BIOS, enable **POST Animation & Sound** in BIOS Setup (`F2` on boot) once. Predator Control can then toggle it on or off directly from Windows.

---

## 🎮 Zero-Injection Gaming Overlay HUD

Monitor real-time performance in any game without triggering anti-cheat systems. Powered by standard Windows ETW kernel tracing (`DxgKrnl` / `DXGI`) and direct `nvml.dll` GPU telemetry—requiring **zero DLL injection** and **zero API hooks**.

- **4 Display Layouts**: Cycle between **Light**, **Default**, **Full**, and **Complete** HUD modes (FPS, 60s sparkline graph, CPU/GPU temps & wattage, fan RPMs, load bars, and VRAM/RAM usage).

| Shortcut / Gesture | Action |
| :--- | :--- |
| **`Ctrl + Shift + O`** | Toggle Gaming Overlay HUD on/off |
| **`Ctrl + Shift + Click`** | Cycle HUD layout (`Light` ➔ `Default` ➔ `Full` ➔ `Complete`) |
| **`Ctrl + Shift + Drag`** | Reposition HUD anywhere on screen (saved automatically) |
| **`Ctrl + Shift + Scroll`** | Scale HUD size (`50%` – `300%`) |
| **`Ctrl + Shift + Middle Click`** | Reset HUD scale to `100%` |

---

## 🛡️ Safe Bloatware Optimizer & Auto-Repair

Under **SYSTEM OPTIMIZATION**, toggle **"Optimize Acer Services (Disable OEM Bloatware)"**:
- Interacts with the Windows Service Control Manager (SCM) to stop and disable background telemetry daemons (`AcerCCAgentSvis`, `AcerDIAgentSvis`, `AcerQAAgentSvis`) while preserving essential hardware bridges (`AASSvc`, `AcerLightingService`, `ASMSvc`).
- Includes a 1-click **Auto-Repair OEM Services** utility that scans `DriverStore\FileRepository`, verifies cryptographic **Acer Incorporated** Authenticode signatures, and restores missing hardware services with explicit user consent.

---

## 🚀 Quick Setup & Usage

1. Download the latest version from [**Releases**](https://github.com/Hadisovic/PredatorControl/releases):
   - **`PredatorControl-standalone.exe`** *(Recommended)*: Self-contained single-file executable (~50.5 MB). No .NET install required.
   - **`PredatorControl-win-x64.exe`**: Lightweight single-file executable (~3.9 MB). Requires [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).
2. **Run as Administrator** (required for ACPI WMI and Embedded Controller hardware access).
3. *(Optional)* Enable **"Start with Windows"** in Settings to launch silently into the system tray on boot.

---

## 🏗️ Repository Architecture

```text
PredatorControl/
├── src/
│   ├── Form1.cs                    # Core window lifecycle, Win32 WndProc & SCM manager
│   ├── Form1.UI.cs                 # Classic UI construction & DPI-aware layout engine
│   ├── Form1.Events.cs             # Zero-lag hardware handlers, RGB, battery & updater
│   ├── Form1.Telemetry.cs          # 1Hz zero-allocation telemetry loop & thermal curves
│   ├── Form1.Preferences.cs        # Registry state persistence, themes & Task Scheduler
│   ├── Form1.Tray.cs               # System tray icon & quick-switch context menu
│   ├── WmiController.cs            # Direct ACPI WMI & EC hardware driver bridge
│   ├── AcerAgentClient.cs          # Verified OEM loopback & Named Pipe client
│   ├── HardwareCompatibility.cs    # Evidence-based model probing & thermal safety floor
│   ├── SecureLaunch.cs             # ACL verification & rooted System32 process launcher
│   ├── GameOverlayForm.cs          # Zero-injection in-game HUD overlay
│   ├── EtwFpsMonitor.cs            # Kernel ETW Present frame-time & FPS monitor
│   ├── NvmlGpuMonitor.cs           # Native NVIDIA NVML telemetry bridge
│   ├── DisplayCcdController.cs     # Win32 CCD display refresh rate switcher
│   ├── OemServiceManager.cs        # Authenticode-verified OEM service manager
│   ├── Updater.cs                  # HTTPS + SHA-256 verified GitHub release updater
│   └── Jelli/                      # WebView2 host & Jelli bridge coordinator
├── jelli-ui/                       # React + TypeScript + Vite Jelli companion frontend
├── tests/                          # 230+ synthetic hardware compatibility & regression tests
└── assets/                         # UI previews and branding assets
```

---

## ⚙️ Building from Source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (and Node.js 20+ if rebuilding `jelli-ui`) on Windows 10/11 x64:

```powershell
git clone https://github.com/Hadisovic/PredatorControl.git
cd PredatorControl

# Run hardware compatibility & keyboard regression test suites
dotnet test tests/Compatibility.Tests/Compatibility.Tests.csproj -c Release
dotnet test tests/Keyboard.Tests/Keyboard.Tests.csproj -c Release

# Publish Standalone Single-File Executable
dotnet publish src/PredatorControlApp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish/standalone
```

---

## 🔒 Safety & Security Architecture

- **Zero Third-Party Kernel Drivers**: Communicates exclusively through standard Windows ACPI WMI (`root\wmi`) and verified OEM services—never loading unsigned Ring-0 drivers such as `WinRing0`.
- **Hardware Guardrails**: Enforces model capability verification, automatic high-temperature fan floor escalation, and crash-safe manual fan recovery.
- **Hardened Execution**: Uses rooted `%SystemRoot%\System32` process execution, ACL-verified scheduled task staging in `%ProgramFiles%`, loopback socket PID ownership checks, and SHA-256 verified HTTPS updates.

---

## 💡 Credits & Attribution

Special thanks to **[@supesonly](https://github.com/supesonly)** for the original inspiration and foundational concept ([supesonly/Acer-P-Helper](https://github.com/supesonly/Acer-P-Helper)).

<div align="center">

**Made with 🪼 and neon teal**  
*Acer, Predator, Nitro, and PredatorSense are registered trademarks of Acer Inc. Predator Control is an independent open-source project.*

</div>
