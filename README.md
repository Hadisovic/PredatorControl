<div align="center">

![Predator Control Banner](assets/banner.jpg)

# 🪼 Predator Control

[![Version](https://img.shields.io/badge/version-1.1.5-00E5FF?style=for-the-badge&logo=github&logoColor=white)](https://github.com/YS47/PredatorControl/releases)
[![Jelli UI](https://img.shields.io/badge/Jelli%20UI-Coming%20Soon%20%E2%9C%A8-FF69B4?style=for-the-badge&logo=sparkles&logoColor=white)](#-jelli-ui--coming-soon)
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
> ### 🪼 Jelli Cute UI Interface — Coming Soon!
> The backend ACPI WMI hardware control engine and Gaming Overlay HUD are fully operational and rock-solid (~40 MB RAM, zero bloat). 
> The brand-new **Jelli cute jellyfish-themed user interface** is currently in active development and will be released in an upcoming major update! Stay tuned!

---

## 🪼 Jelli UI — Coming Soon!

> 🚧 **Status:** In Active Development & Design Phase ✨

Predator Control is designed to be both featherlight and delightful. While the current release features a clean, high-performance dark dashboard, the upcoming **Jelli UI** overhaul is bringing:

- 🌸 **Cute Bioluminescent Jellyfish Mascot**: An interactive Jelli companion that reacts to your laptop's thermals, fan speeds, and active power profiles.
- 🌊 **Modern Glassmorphism Design**: Deep ocean dark backdrop with glowing `#00E5FF` neon teal and soft coral accents.
- 🪶 **Zero-Bloat Philosophy**: Handcrafted for near-zero CPU wakeups and minimal memory footprint (~40 MB RAM).
- 🎛️ **Quick Floating Widget & Tray Flyout**: Instant performance cycling and thermal checks without opening full windows.

*The core ACPI WMI engine and Gaming Overlay are completely functional today — the full Jelli visual interface will roll out in the upcoming release.*

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

## 🗺️ Roadmap

- [ ] 🪼 **Jelli UI Interface**: Complete cute jellyfish visual overhaul & interactive mascot (**In Development 🚧**)
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
