<div align="center">

![Predator Control Banner](assets/banner.jpg)

# 🪼 Predator Control

[![Version](https://img.shields.io/badge/version-1.2.1-00E5FF?style=for-the-badge&logo=github&logoColor=white)](https://github.com/YS47/PredatorControl/releases)
[![Jelli UI](https://img.shields.io/badge/Jelli%20UI-Live%20%F0%9F%AA%BC-FF69B4?style=for-the-badge&logo=sparkles&logoColor=white)](#-jelli-ui)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-00B4CC?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/Hadisovic/PredatorControl)
[![Framework](https://img.shields.io/badge/.NET-10.0%20Windows-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-2ED573?style=for-the-badge)](LICENSE)
[![RAM](https://img.shields.io/badge/RAM%20Usage-%7E40%20MB-FF6B35?style=for-the-badge&logo=memory&logoColor=white)](https://github.com/Hadisovic/PredatorControl)
[![Services](https://img.shields.io/badge/Acer%20Services-4%20Essential%20Only-2ED573?style=for-the-badge&logo=windows&logoColor=white)](#-the-4-essential-acer-services)

<br/>

**Predator Control** is a lightweight, bloat-free alternative to Acer PredatorSense for **Acer Predator & Nitro** laptops.  
Direct ACPI WMI hardware control, **~40 MB RAM** (vs 1.5 GB+), and only **4 essential Acer services** running!

</div>

---

### 🖼️ UI & Gaming Overlay Previews

<div align="center">
  <h4>🎮 In-Game Gaming Overlay HUD (G-Helper Style & Zero-Injection ETW FPS Counter)</h4>
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

> [!NOTE]
> ### 🪼 Jelli UI is Live in v1.2.0+!
> The beautiful jellyfish-themed **Jelli UI** is now the default interface. You can switch back to the classic control panel at any time from the Jelli Settings panel.

---

## 🪼 Jelli UI

> ✅ **Status:** Live & Stable as of v1.2.0

Predator Control now ships with the full **Jelli UI** — a bioluminescent jellyfish-themed interface that replaces the classic dark dashboard:

- 🌸 **Interactive Jelli Mascot**: A cute jellyfish companion that reacts to your laptop's thermals, fan speeds, and active power profiles.
- 🌊 **Modern Glassmorphism Design**: Deep ocean dark backdrop with glowing `#00E5FF` neon teal and soft coral accents.
- 🪶 **Zero-Bloat Architecture**: WebView2-hosted React frontend with true zero-footprint when disabled — Chromium processes are fully terminated on toggle-off.
- 🎛️ **Jelli or Classic — Your Choice**: Toggle between the Jelli UI and the original classic control panel from Settings at any time. When Jelli is off, it consumes **zero CPU and zero RAM** (all WebView2 child processes are killed).
- 📊 **Floating Metrics HUD**: Optional on-screen performance overlay with Off / Light / Default / Full modes.
- 🔗 **Expand or Attach Flyout**: Choose between Jelli as a standalone expanded window or as a tray-attached flyout.

---

## ✨ Why Predator Control?

Acer PredatorSense and Acer Care Center bundle **10+ background services and telemetry agents**, an Electron frontend, and consume **500 MB – 1.5 GB of RAM**.  
Predator Control replaces all of that with direct ACPI WMI calls, using only **~40 MB RAM**, and keeps only the **4 essential hardware services** required for your laptop's fans, lighting, and GPU MUX switch!

### 🔬 Resource Comparison

| Metric | 🐌 Acer PredatorSense + Care Center | 🪼 Predator Control | Improvement |
| :--- | :---: | :---: | :--- |
| **Idle RAM** | 500 MB – 1.5 GB | **~40 MB** | 🟢 **~95% less RAM** |
| **Background Services** | 10+ bloatware & telemetry daemons | **4 Essential Only** | 🟢 **All telemetry silenced** |
| **Idle CPU** | 0.8 – 3.5% | **< 0.1% / 0.0%** | 🟢 Zero micro-stutter |
| **Window Toggle Latency** | 2.5 – 5.0 s | **< 50 ms** | 🟢 Instant launch |
| **Mode Switch Latency** | 500 – 1200 ms | **< 30 ms** | 🟢 Direct EC write |
| **Game Focus Stealing** | ❌ Focus steals during games | **✅ Never** (`WS_EX_NOACTIVATE`) | 🟢 100% game-safe |
| **In-Game FPS Counter** | ❌ None | **✅ Zero-Injection Kernel ETW** | 🟢 Anti-Cheat Safe |

---

## 🛡️ The 4 Essential Acer Services

Predator Control replaces the heavy PredatorSense Electron frontend while smartly coexisting with Acer's hardware layer. To maintain full hardware control with **only ~40 MB RAM**, it keeps **4 essential Acer services** running:

| Service | Display Name | Why It Is Essential |
| :--- | :--- | :--- |
| **`AASSvc`** | **Acer Agent Service** | Direct Named Pipe (`\\.\pipe\AcerAgentPipe`) for real-time fan RPM tachometers, GPU MUX switching, and fast RGB packet serialization. |
| **`AcerLightingService`** | **Acer Lighting Service** | Controls keyboard 4-zone lighting profiles and firmware color latching (prevents annoying Amber light reset on reboot). |
| **`AcerHardwareService`** / **`AcerServiceSvc`** | **Acer Service Component** | Handles low-level APGe hardware actions (30-second keyboard backlight idle sleep timeout, power profile TDP switches). |
| **`AcerDeviceEnablingServiceV2`** | **Acer Device Enabling Service V2** | ACPI device driver bridge communicating directly with the Embedded Controller (EC). |

### 🧹 What Gets Disabled? (Bloatware & Telemetry Silenced)
Predator Control automatically detects and disables **6 intrusive, duplicate background services**:
- ❌ **`AcerCCAgentSvis`** (Acer Care Center — Telemetry, update nagware, background RAM hog)
- ❌ **`AcerQAAgentSvis`** (Acer Quick Access — Legacy popups & slow OSD)
- ❌ **`AcerDIAgentSvis`** (Acer Device Info — Periodic cloud telemetry uploads)
- ❌ **`ASMSvc`** (Acer System Monitor Service — Duplicate sensor polling causing CPU wakeups)
- ❌ Intrusive Acer startup tasks and background analytics

You can run `disable_acer_bloatware.bat` anytime to apply this optimization with a single click, or `enable_acer_services.bat` to restore default settings.

---

## 🚀 Features Matrix

### ⚡ Performance Profiles
| Mode | ACPI Code | Description |
| :--- | :---: | :--- |
| 🤫 **Quiet** | `0x00` | Low fan ceiling, whisper acoustics, low power |
| ⚖️ **Balanced** | `0x01` | Dynamic thermal balance for everyday use |
| 🏎️ **Performance** | `0x04` | High CPU TDP / GPU TGP boost, aggressive fan curves |
| 🔥 **Turbo** | `0x05` | Maximum GPU TGP overclock + 100% full fan blast (AC only) |
| 🍃 **Eco** | `0x06` | Ultra-low power battery saver mode (DC auto-switch) |

- 🔘 **Physical Mode Button Intercept**: Cycles performance modes via low-level hardware scan codes & WMI `APGeEvent`.
- 🔌 **AC/Battery Auto-Switch**: Automatically toggles profiles when plugging in or unplugging power.

### 🌡️ Thermal Management & Fans
- 💨 **Auto Fan**: Hardware EC curve.
- 🚀 **Max Fan Override**: Instant 100% full speed cooling.
- 🎚️ **Dual Custom Fan Sliders**: Independent CPU & GPU fan sliders (0–100%).
- 📈 **Graphical Fan Curve Designer**: Interactive multi-point temperature-to-RPM editor.
- ❄️ **CoolBoost™ Toggle**: Raises thermal ceiling for emergency cooling.
- 📊 **Live Dual Tachometer**: Real-time RPM monitoring.

### 🌈 Keyboard RGB Lighting (Pulsar)
- 🎨 **9 Animated Effects**: Static, Breathing, Neon, Wave, Shifting, Zoom, Meteor, Twinkling, Off.
- 🖌️ **6 Neon Quick Presets**: Teal, Crimson, Cyan, Amber, Purple, White.
- 🎯 **4-Zone Independent RGB**: Custom per-zone colors with live neon color indicators.
- 🎭 **Full RGB Color Picker**: Custom 24-bit Hex/RGB selection.
- 💤 **30s Backlight Idle Timeout**: Auto-sleeps LEDs on inactivity.
- ⚡ **Zero-Lag Batching**: Non-blocking background worker (~30ms apply time).

### 🖥️ Hardware & Battery Controls
- 🔋 **80% Battery Charge Limiter**: Hardware register toggle to preserve battery lifespan.
- 🔒 **Windows Key Lock**: Disables Windows key during gaming.
- 🔊 **BIOS Startup Sound**: Toggle Acer Predator boot chime directly in BIOS/EC.
- ⚡ **LCD Overdrive (3ms)**: Toggles panel response time.
- 🖱️ **GPU MUX Switch**: Optimus, Discrete (NVIDIA Only), and Advanced Optimus.
- 🖥️ **Refresh Rate Switcher**: Switch internal display (60Hz ↔ 165Hz/240Hz) and external monitors via Win32 CCD API.

### 🎮 OSD & In-Game Gaming Overlay
- ⚡ **In-Game Gaming Overlay HUD (G-Helper Style)**: Real-time neon green FPS counter (powered by zero-injection Windows ETW kernel Present monitoring), CPU/GPU temperatures, fan RPMs, active GPU wattage (`0x0D`), 60-second rolling sparkline performance graph, and battery gauge. Toggle with `Ctrl + Shift + O`, drag anywhere with `Ctrl + Shift + Drag`.
- 🛡️ **100% Anti-Cheat Safe**: Uses Windows ETW kernel tracing (`Microsoft-Windows-DxgKrnl` Event ID 184 / `DXGI` Event ID 42). Zero DLL injection, zero API hooking (safe with Vanguard, EAC, BattlEye, Ricochet).
- 🔑 **Dedicated Predator Key Intercept**: Low-level hardware hook (`WH_KEYBOARD_LL`) intercepting Acer scan code `0x75` (`VK_0xFF`) to toggle the app window instantly without stealing in-game focus.
- 🏎️ **High CPU Priority Enforcement**: Automatically configures Windows Registry IFEO (`CpuPriorityClass=3`), Task Scheduler `<Priority>2</Priority>`, and runtime `ProcessPriorityClass.High` (`BasePriority=13`) for zero latency and micro-stutter immunity during AAA gaming.
- 🌊 **Floating Mode OSD**: Focus-safe banner with custom neon vector emblems on profile changes.
- 🎮 **Game Sync Profiles**: Automatically binds performance, fans, and RGB to game `.exe` launches.

---

## 🎮 Gaming Overlay HUD & Interactive Gestures (G-Helper Style)

Predator Control includes an ultra-lightweight, 3-mode in-game telemetry HUD inspired by **G-Helper**, rendering live performance stats directly on your screen without any performance impact or anti-cheat triggers.

### 🕹️ Display Modes
- **Light Mode (`Light`):** Compact minimal HUD (`FPS | GPU Temp & Watts | CPU Temp & Watts`).
- **Default Mode (`Default`):** Balanced monitor (`FPS | GPU & CPU Temp + Fan RPM | 60s Sparkline Graph | Power Draw`).
- **Full Mode (`Full`):** Complete hardware dashboard (`Default + GPU/CPU Load % Bars + VRAM & System RAM Bars`).

### ⌨️ Shortcuts & Gestures

| Gesture / Shortcut | Action | Description |
| :--- | :--- | :--- |
| **`Ctrl + Shift + Alt + O`** (or `Ctrl + Shift + O`) | **Toggle Overlay** | Shows or hides the overlay anywhere in Windows or games. |
| **`Ctrl + Shift + Alt + Click`** | **Cycle Display Mode** | Cycles: `Light` ➔ `Default` ➔ `Full` ➔ `Light`. |
| **`Ctrl + Shift + Alt + Drag`** | **Move Overlay** | Repositions HUD on screen; coordinates persist across reboots. |
| **`Ctrl + Shift + Alt + Mouse Wheel`** | **Scale HUD (50%–300%)** | Dynamically increases/decreases size in 10% steps. |
| **`Ctrl + Shift + Alt + Middle Click`** | **Reset Scale (100%)** | Instantly resets HUD scaling back to standard 100%. |

> [!TIP]
> **Anti-Cheat Safe:** The overlay utilizes Windows ETW kernel tracing (`Microsoft-Windows-DxgKrnl` Event ID 184 / `DXGI` Event ID 42). It requires zero DLL injection and zero API hooks, making it completely safe in games with anti-cheats (Vanguard, EAC, BattlEye, Ricochet).

## 🏗️ Repository Structure

```
PredatorControl/
├── src/
│   ├── Form1.cs                   # Main UI, layout, and IPC handlers
│   ├── WmiController.cs           # ACPI WMI hardware driver (Power, Fans, RGB, Battery)
│   ├── PredatorKeyHook.cs         # WH_KEYBOARD_LL low-level keyboard hook
│   ├── GameOverlayForm.cs         # In-Game Gaming Overlay HUD (G-Helper style)
│   ├── EtwFpsMonitor.cs           # Kernel ETW DxgKrnl / DXGI Present FPS monitor
│   ├── OSDOverlayForm.cs          # Focus-safe floating HUD overlay
│   ├── DisplayCcdController.cs    # Win32 CCD display & refresh rate switcher
│   ├── SingleInstanceIpc.cs       # Named Pipe server & client for single-instance IPC
│   ├── TelemetryService.cs        # CPU/GPU temperature and fan RPM poller
│   ├── ThemeManager.cs            # UI Color palettes (Predator Teal, Nitro Crimson, OLED Black)
│   ├── FanCurveForm.cs            # Graphical fan curve editor
│   ├── FanCurveGraph.cs           # Fan curve interactive graph engine
│   ├── GameSyncController.cs      # Auto profile switcher on game launch
│   ├── GameSyncForm.cs            # Game sync profile management UI
│   ├── DarkScrollPanel.cs         # Custom dark scroll panel
│   ├── Updater.cs                 # In-app GitHub release updater
│   ├── SelfCheck.cs               # Self-diagnostics & environment checks
│   ├── PredatorButton.cs          # Custom WinForms controls (Buttons, Sliders, Switches, Dropdowns)
│   └── PredatorControlApp.csproj  # .NET 10.0 Windows Forms project definition
├── assets/
│   ├── banner.jpg                 # Jelli jellyfish mascot hero banner
│   ├── preview_overlay.png        # In-Game Gaming Overlay HUD preview
│   ├── preview_dashboard.png      # Dashboard, Thermals & MUX Switch preview
│   └── preview_controls.png       # RGB Lighting & System Controls preview
├── README.md                      # Project documentation
└── .gitignore                     # Git ignore rules
```

---

## 📋 Changelog

### v1.2.1 — Jelli Toggle Fix & RGB Polish
- 🔧 **Jelli Toggle — True Off**: Toggling Jelli off now kills all WebView2 child Chromium processes, guaranteeing **zero CPU and zero RAM** footprint when disabled.
- 🔧 **Jelli Toggle — No Stuck Switching**: Fixed a hang in the Settings panel where the toggle would get stuck on "Switching..." if settings validation failed; now uses a direct IPC legacy path.
- 🔧 **Jelli Settings — Always Accessible**: "Expand Jelli" and "Attached flyout" mode buttons are now always enabled regardless of Jelli's on/off state.
- 🎨 **RGB Static — 4-Zone Colors Preserved**: Selecting "All Zones" no longer overwrites individual saved zone colors; each zone retains its color when switching back to static mode.
- 🎨 **RGB Animated Mode — Color Correct**: Animated modes (Breathing, Shifting, Zoom, Meteor, Twinkling) now correctly use the active color picker color instead of zone 0's saved color.
- 🎨 **RGB Brightness/Speed — Zone Aware**: Changing brightness or speed in static 4-zone mode now correctly re-applies all individual zone colors instead of a single color.
- 🎨 **RGB INI Sync — Correct Color Written**: `SyncLightingProfileIni` now writes zone 0's actual color (not the last animated color) to the `STATIC` section, preventing amber fallback on reboot.
- ⏱️ **RGB Zone Sequencing**: Increased inter-zone sleep from 15ms → 30ms for improved hardware EC reliability on static zone apply.

### v1.2.0 — Jelli UI Launch
- 🪼 **Jelli UI**: Full WebView2-hosted React/TypeScript jellyfish-themed interface.
- 🎛️ **Settings Panel**: Toggle Jelli on/off, choose Expanded or Attached flyout mode, configure floating metrics HUD (Off / Light / Default / Full).
- 📊 **Floating Metrics HUD**: Real-time CPU/GPU stats with multiple display modes.

### v1.1.7 — G-Helper Style Overlay HUD
- ⚡ **3-Mode Gaming Overlay HUD** (Light / Default / Full) with ETW kernel FPS counter.
- 🖥️ **Native NVML GPU Telemetry** for accurate wattage and load readings.
- 🎯 Tightened layout, README keyboard shortcut reference.

---

## 🗺️ Roadmap

- [x] 🪼 **Jelli UI Interface**: ✅ Live in v1.2.0
- [ ] 🎮 **Raw Input HID Hook**: Native hardware Predator Key sniffing (`UsagePage 0xFFA0`)
- [ ] ⚡ **ACPI Event Sniffing**: Deep `AcerEvent` / `APGeEvent` mapping
- [ ] 🌊 **RGB Wave Direction**: Direction toggle (Left-to-Right / Right-to-Left)
- [ ] 🔋 **USB Power-Off Charging**: Direct firmware toggle
- [ ] 🩺 **Hardware Health Diagnostics**: Battery wear % & SSD S.M.A.R.T. readout

---

## ⚙️ Building from Source

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows 10/11 x64

```powershell
# Clone the repository
git clone https://github.com/Hadisovic/PredatorControl.git
cd PredatorControl/src

# Build Release
dotnet build -c Release

# Publish Standalone Executable (~52 MB)
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o "..\publish\standalone"

# Publish Unpacked Folder
dotnet publish -c Release -r win-x64 --no-self-contained -p:PublishSingleFile=false -o "..\publish\unpacked"
```

---

## 🚀 Quick Setup & Usage

1. Download the latest release from [Releases](https://github.com/YS47/PredatorControl/releases).
2. Run **`PredatorControl-standalone.exe`** as Administrator.
3. *(Optional)* Toggle **"Start with Windows"** in the app settings to run silently in the system tray at login without any startup delay.

---

## 🧪 Controlling via Named Pipe IPC

You can interact with a running instance of Predator Control from PowerShell:

```powershell
$pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", "AcerPredatorControl_IPC_Pipe", [System.IO.Pipes.PipeDirection]::Out)
$pipe.Connect(1000)
$writer = New-Object System.IO.StreamWriter($pipe)
$writer.AutoFlush = $true

# Available commands: "SHOW" | "TOGGLE" | "CYCLE_MODE" | "EXIT"
$writer.WriteLine("TOGGLE")

$writer.Close()
$pipe.Close()
```

---

## 🛡️ Safe Bloatware Optimization

To silence Acer telemetry and bloatware without breaking hardware controls:
1. Run **`disable_acer_bloatware.bat`** as Administrator.
2. It disables the 6 telemetry daemons (`AcerCCAgentSvis`, `AcerDIAgentSvis`, `AcerQAAgentSvis`, `ASMSvc`, etc.) while safeguarding the **4 essential services** (`AASSvc`, `AcerLightingService`, etc.).
3. To restore factory Acer services at any time, run **`enable_acer_services.bat`**.

---

---

## 💡 Credits & Attribution

Special thanks and recognition to **[@supesonly](https://github.com/supesonly)** for the foundational inspiration and project base:
* [supesonly/Acer-P-Helper](https://github.com/supesonly/Acer-P-Helper)

---

<div align="center">

**Made with 🪼 and neon teal**

*Acer and PredatorSense are registered trademarks of Acer Inc. Predator Control is an independent open-source project.*

</div>
