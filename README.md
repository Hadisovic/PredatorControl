<div align="center">

![Predator Control Banner](assets/banner.jpg)

# 🪼 Predator Control

[![Version](https://img.shields.io/badge/version-1.4.1-00E5FF?style=for-the-badge&logo=github&logoColor=white)](https://github.com/Hadisovic/PredatorControl/releases)
[![Jelli UI](https://img.shields.io/badge/Jelli%20UI-Coming%20Soon%20%E2%9C%A8-FF69B4?style=for-the-badge&logo=sparkles&logoColor=white)](#-jelli-ui--coming-soon)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-00B4CC?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/Hadisovic/PredatorControl)
[![Framework](https://img.shields.io/badge/.NET-10.0%20Windows-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-2ED573?style=for-the-badge)](LICENSE)
[![RAM](https://img.shields.io/badge/RAM%20Usage-%7E18%20MB-FF6B35?style=for-the-badge&logo=memory&logoColor=white)](https://github.com/Hadisovic/PredatorControl)

<br/>

**Predator Control** is a lightweight, bloat-free alternative to Acer PredatorSense for **Acer Predator & Nitro** laptops.
Direct ACPI WMI hardware control, **~95% less RAM**, zero background services!

</div>

> [!NOTE]
> ### 🪼 Jelli Cute UI Interface — Coming Soon!
> The backend ACPI WMI hardware control engine is fully operational and rock-solid (~18 MB RAM, zero bloat). 
> The brand-new **Jelli cute jellyfish-themed user interface** is currently in active development and will be released in an upcoming major update! Stay tuned!

---

## 🪼 Jelli UI — Coming Soon!

> 🚧 **Status:** In Active Development & Design Phase ✨

Predator Control is designed to be both featherlight and delightful. While the current release features a clean, high-performance dark dashboard, the upcoming **Jelli UI** overhaul is bringing:

- 🌸 **Cute Bioluminescent Jellyfish Mascot**: An interactive Jelli companion that reacts to your laptop's thermals, fan speeds, and active power profiles.
- 🌊 **Modern Glassmorphism Design**: Deep ocean dark backdrop with glowing `#00E5FF` neon teal and soft coral accents.
- 🪶 **Zero-Bloat Philosophy**: Handcrafted for near-zero CPU wakeups and minimal memory footprint (< 25 MB RAM).
- 🎛️ **Quick Floating Widget & Tray Flyout**: Instant performance cycling and thermal checks without opening full windows.

*The core ACPI WMI engine is completely functional today — the full Jelli visual interface will roll out in the upcoming release.*

---

## ✨ Why Predator Control?

Acer PredatorSense ships with **5+ background services**, an Electron launcher, and idles at **350–600 MB RAM**. 
Predator Control replaces all of that with direct ACPI WMI calls to your laptop's embedded controller.

### 🔬 Resource Comparison

| Metric | 🐌 Acer PredatorSense | 🪼 Predator Control | Improvement |
| :--- | :---: | :---: | :--- |
| **Idle RAM** | 350 – 600 MB | **~18 – 30 MB** | 🟢 ~95% less RAM |
| **Background Services** | 5+ | **0** | 🟢 100% bloat free |
| **Idle CPU** | 0.5 – 2.5% | **< 0.1% / 0.0%** | 🟢 Zero stutter |
| **Window Toggle Latency** | 2.5 – 5.0 s | **< 50 ms** | 🟢 Instant launch |
| **Mode Switch Latency** | 500 – 1200 ms | **< 30 ms** | 🟢 Direct EC write |
| **Game Focus Stealing** | ❌ Focus steals | **✅ Never** (`WS_EX_NOACTIVATE`) | 🟢 Game-safe OSD |

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

### 🎮 OSD & Game Sync
- 🌊 **Floating OSD HUD**: Focus-safe banner with custom neon vector emblems.
- 🎮 **Game Sync Profiles**: Automatically binds performance, fans, and RGB to game `.exe` launches.
- 🔑 **Predator Key Intercept**: Hardware key hook + OEM launcher redirection.

---

## 🏗️ Repository Structure

```
PredatorControl/
├── src/
│   ├── Form1.cs                   # Main UI, layout, and IPC handlers
│   ├── WmiController.cs           # ACPI WMI hardware driver (Power, Fans, RGB, Battery)
│   ├── PredatorKeyHook.cs         # WH_KEYBOARD_LL low-level keyboard hook
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
│   └── banner.jpg                 # Jelli jellyfish mascot hero banner
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

# Build Debug
dotnet build -c Debug

# Build Release
dotnet build -c Release

# Publish Unpacked Release Folder
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -o "..\publish\unpacked"

# Publish Single-File Standalone Executable
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o "..\publish\standalone"
```

---

## 🚀 Quick Setup & Usage

1. Download the latest release from [Releases](https://github.com/Hadisovic/PredatorControl/releases).
2. Extract the archive and run `PredatorControlApp.exe` as Administrator.
3. *(Optional)* Add a shortcut to Windows Startup or Task Scheduler with `-hidden` parameter to run silently in the system tray at login.

---

## 🧪 Controlling via Named Pipe IPC

You can interact with a running instance of Predator Control from PowerShell:

```powershell
$pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", "AcerPredatorControl_IPC_Pipe", [System.IO.Pipes.PipeDirection]::Out)
$pipe.Connect(1000)
$writer = New-Object System.IO.StreamWriter($pipe)
$writer.AutoFlush = $true

# Available commands: "SHOW" | "TOGGLE" | "CYCLE_MODE" | "EXIT"
$writer.WriteLine("CYCLE_MODE")

$writer.Close()
$pipe.Close()
```

---

## 🛡️ Safe PredatorSense Coexistence

To test Predator Control without completely uninstalling OEM software:
1. Open `services.msc`.
2. Set `Acer PredatorSense Service`, `Acer Agent Service`, and `Acer Care Center Service` to **Disabled**.
3. Reboot your system. Predator Control will take over hardware management with 0 bloat.

---

<div align="center">

**Made with 🪼 and neon teal**

*Acer and PredatorSense are registered trademarks of Acer Inc. Predator Control is an independent open-source project.*

</div>
