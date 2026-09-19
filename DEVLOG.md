# 📜 PredatorControl — Complete Development & Engineering Log
**Project:** PredatorControl (formerly Acer-P-Helper / PredatorSense Alternative)  
**Target Hardware:** Acer Predator Helios & Nitro Gaming Laptops (e.g., PH16-71, PH18-71, Neo 16)  
**Maintained by:** Yousef (YS47) & Hadisovic  
**Repository:** `https://github.com/YS47/PredatorControl` (upstream sync with `Hadisovic/PredatorControl`, branch `Revs`)  
**Timeline:** September 5, 2026 – September 16, 2026+  

---

## 📑 Table of Contents
1. [Executive Summary & Motivation](#1-executive-summary--motivation)
2. [Phase 1: Project Genesis, Unpacking & Reverse Engineering](#2-phase-1-project-genesis-unpacking--reverse-engineering)
   - [The Acer Bloatware Problem](#the-acer-bloatware-problem)
   - [Decompiling OEM Binaries & Named Pipe IPC](#decompiling-oem-binaries--named-pipe-ipc)
   - [The RGB Keyboard Backlight Saga](#the-rgb-keyboard-backlight-saga)
   - [The "Amber Light on Boot" Heist](#the-amber-light-on-boot-heist)
   - [30-Second Backlight Idle Sleep Fix](#30-second-backlight-idle-sleep-fix)
3. [Phase 2: Core Hardware Power, Fan Modes & Startup Fixes](#3-phase-2-core-hardware-power-fan-modes--startup-fixes)
   - [ACPI Dual-Dispatch Power Profiles](#acpi-dual-dispatch-power-profiles)
   - [Zero-Delay Scheduled Logon Task (v1.0.1 Hotfix)](#zero-delay-scheduled-logon-task-v101-hotfix)
   - [Restoring WMI Sensors & Standalone Single-File Build](#restoring-wmi-sensors--standalone-single-file-build)
4. [Phase 3: GPU MUX Switch Reverse Engineering (v1.1.0)](#4-phase-3-gpu-mux-switch-reverse-engineering-v110)
   - [Investigating Acer Advanced Optimus / Display Switcher](#investigating-acer-advanced-optimus--display-switcher)
   - [WMI Display Mode Payloads](#wmi-display-mode-payloads)
   - [UI Integration & Release v1.1.0](#ui-integration--release-v110)
5. [Phase 4: Acer Bloatware & Telemetry Elimination](#5-phase-4-acer-bloatware--telemetry-elimination)
   - [Service Audit & Classification](#service-audit--classification)
   - [Automated Service Optimization Engine](#automated-service-optimization-engine)
6. [Phase 5: High CPU Priority & Latency Immunity](#6-phase-5-high-cpu-priority--latency-immunity)
   - [Three-Tier Priority Architecture](#three-tier-priority-architecture)
   - [Registry IFEO & Verification](#registry-ifeo--verification)
7. [Phase 6: The Dedicated Predator Key Hardware Hook](#7-phase-6-the-dedicated-predator-key-hardware-hook)
   - [The Challenge](#the-challenge)
   - [Focus Bug in App Toggle](#focus-bug-in-app-toggle)
   - [Developing Custom Low-Level Diagnostic Tools (`KeySniffer`)](#developing-custom-low-level-diagnostic-tools-keysniffer)
   - [The Hardware Revelation: VK=0xFF & SC=0x75](#the-hardware-revelation-vk0xff--sc0x75)
   - [Implementation & Breakthrough Confirmation](#implementation--breakthrough-confirmation)
8. [Phase 7: In-Game Gaming Overlay HUD & ETW Engine](#8-phase-7-in-game-gaming-overlay-hud--etw-engine)
   - [Architecture & Design Decisions](#architecture--design-decisions)
   - [Zero-Injection Kernel ETW Present Monitoring](#zero-injection-kernel-etw-present-monitoring)
   - [Double-Buffered GDI+ Canvas & 60s Sparklines](#double-buffered-gdi-canvas--60s-sparklines)
   - [Click-Through & Interactive Dragging](#click-through--interactive-dragging)
   - [Controls & Persistence](#controls--persistence)
9. [Phase 8: Roadmap & Next Objectives](#9-phase-8-roadmap--next-objectives)
   - [Feature 3: Parallelized GPU Telemetry & iGPU Support](#feature-3-parallelized-gpu-telemetry--igpu-support)
10. [Complete Git Commit Chronology](#10-complete-git-commit-chronology)
11. [Architecture & Reference Index](#11-architecture--reference-index)

---

## 1. Executive Summary & Motivation

Acer gaming laptops (Predator Helios, Triton, Nitro) ship with **PredatorSense** / **Acer Care Center**, bulky Electron/UWP bloatware suites consuming 500MB–1.5GB of RAM, running multiple background telemetry daemons (`AcerCCAgentSvis`, `AcerDIAgentSvis`, `ASMSvc`), causing micro-stuttering in competitive gaming, and suffering from long-standing firmware bugs (such as keyboard backlights constantly resetting to Amber on reboot).

**PredatorControl** was developed as a clean, hyper-optimized, standalone C# .NET WinForms replacement:
- **RAM usage:** ~18MB (vs 1GB+ for PredatorSense)
- **Startup time:** <200ms instantaneous zero-delay boot
- **Zero background telemetry:** Silences all intrusive Acer daemons while keeping essential OEM hardware pipes intact
- **Features supported:** 4-Zone RGB Lighting, Fan Curves (Auto, Max, Custom), Power Profiles (Quiet, Balanced, Performance, Turbo), LCD Overdrive, Battery Charge Limiter (80%), GPU MUX Switch (Optimus, dGPU, Auto), and Dedicated Hardware Predator Key Toggle.

---

## 2. Phase 1: Project Genesis, Unpacking & Reverse Engineering

### The Acer Bloatware Problem
Early investigation revealed that PredatorSense communicates with the EC (Embedded Controller) and BIOS through two main interfaces:
1. **WMI (`root\wmi`):** Querying `Acer_SetGamingProfile`, `Acer_GetGamingProfile`, and hardware thermal sensors.
2. **Acer Agent Service Named Pipe (`\\.\pipe\AcerAgentPipe` / `AASSvc`):** Direct binary serialization for real-time fan RPM, RGB keyboard packet bursts, and discrete GPU MUX switching.

### Decompiling OEM Binaries & Named Pipe IPC
Custom C# scratch utilities (`AsarViewer.cs`, `CheckFuncStrings.cs`, `DumpAgentCallers.cs`, `DumpDispatcher.cs`) were built to decompile Acer's `.asar` Electron frontend packages and disassemble `AcerService.exe` and `AASSvc.exe`.  
Key discoveries:
- PredatorSense writes binary command structures over a local Windows Named Pipe.
- Function dispatch IDs: `0x4210` (RGB lighting), `0x4130` (Fan mode), `0x23450` (Power profiles), `0x6001` (Display/MUX mode).

### The RGB Keyboard Backlight Saga
The 4-zone RGB keyboard implementation initially suffered from desyncs, wrong zone mapping, and missing color latching:
- **Firmware color latch flag:** `payload[8] = 0x03` was identified as the required latch instruction to force the keyboard EC microcontroller to persist live colors without resetting.
- **Direction & Animation handling:** Byte `payload[4]` dictated animation sweep direction.
- **Zone mapping:** Fixed 4-zone bitmask `(1 << z)` to address Left, Mid-Left, Mid-Right, and Right zones individually or simultaneously in Static mode.
- **Debounced live slider:** Added non-blocking debounced disk I/O for smooth real-time color picking.

### The "Amber Light on Boot" Heist
A major firmware bug on Acer Helios laptops is that on cold boot or reboot, the keyboard backlight snaps to an unsightly amber/orange color regardless of user settings:
1. **Root Cause:** Acer's OEM services read `LightingProfile.ini` on startup with an unexpected byte offset, falling back to OEM factory default (Amber: `#FF6600`).
2. **The Multi-Stage Fix (`commit 1814690`):**
   - Corrected `LightingProfile.ini` format and active mode offset.
   - Dual-dispatched RGB packets on startup directly to `AcerAgentService` and firmware EC memory.
   - Purged all hardcoded amber presets from disk and registry.
   - Mirrored RGB state to `HKLM:\SOFTWARE\OEM\PredatorSense\RGB`.
   - Introduced a **delayed settling guard** (250ms and 1000ms after boot) to override Acer's late startup reset.

### 30-Second Backlight Idle Sleep Fix
Acer's firmware forcefully shuts down the keyboard backlight after 30 seconds of inactivity:
- Solved via Hardware Service Named Pipe `APGeAction` call `SetGamingMiscSetting` (`commit 5343959`), giving users the choice to keep the backlight on permanently or honor timeout.

---

## 3. Phase 2: Core Hardware Power, Fan Modes & Startup Fixes

### ACPI Dual-Dispatch Power Profiles
- Acer laptops utilize specific ACPI method calls to shift TDP, fan tables, and PL1/PL2 power limits.
- Supported modes:
  - **Quiet:** Low wattage, silent fan curve.
  - **Balanced:** Stock factory balanced profile.
  - **Performance:** Elevated TDP limits, aggressive fan response.
  - **Turbo:** Maximum factory GPU/CPU overclock + thermal ceiling.
- **Fan Mode Decoupling:** Reverted an automatic fan override in Turbo mode so users retain manual control over fan speeds even in Turbo mode (`commit 8e14b8c`).

### Zero-Delay Scheduled Logon Task (v1.0.1 Hotfix)
- Standard Windows Task Scheduler tasks often impose an arbitrary 1-to-2 minute startup delay or fail to start when waking from hybrid sleep.
- Updated `StartupManager.cs` (`commit 6e9c66a` & `5e34300`):
  - Injected `<Delay>PT0S</Delay>` into the Task Scheduler XML definition.
  - Added early boot hardware task with exact working directory targeting `PredatorControlApp.exe`.
  - Added single-click tray icon response to instantly open the dashboard.

### Restoring WMI Sensors & Standalone Single-File Build
- Fixed missing `System.Management.dll` reference in standalone builds (`commit 05b366a`).
- Configured single-file build options:
  - Compressed executable target (`-p:EnableCompressionInSingleFile=true`) yielding a clean, self-contained ~52MB binary requiring zero pre-installed runtimes.

---

## 4. Phase 3: GPU MUX Switch Reverse Engineering (v1.1.0)

Acer Predator 2023–2025 laptops feature an internal display MUX chip supporting **NVIDIA Advanced Optimus**.

### Investigating Acer Advanced Optimus / Display Switcher
Through reverse-engineering of `AcerDisplayModeSwitcher` and WMI method calls under `root\wmi:Acer_DisplayModeSetting`:
- Mode **0**: Dynamic Switching / Optimus (iGPU displays, dGPU sleeps when idle)
- Mode **1**: Discrete GPU Only (internal panel directly wired to RTX graphics card for minimum latency and maximum FPS)
- Mode **2**: Auto / Advanced Optimus (automatic display switching controlled by NVIDIA driver)

### UI Integration & Release v1.1.0
- Added 3 interactive MUX switch buttons on the dashboard: `Optimus`, `Discrete GPU`, `Auto`.
- Added dynamic status detection to display the currently active mode.
- Added user confirmation dialog warning that changing to/from dGPU mode requires a system reboot.
- Released in `commit 2498c8f` and `71c11de`.

---

## 5. Phase 4: Acer Bloatware & Telemetry Elimination

### Service Audit & Classification
The user requested a full audit of all active Acer services on the machine to eliminate background overhead:

| Service Name | Display Name | Verdict | Action Taken | Rationale |
| :--- | :--- | :---: | :---: | :--- |
| **`AcerCCAgentSvis`** | Acer Care Center Service | ❌ Bloat | **Disabled & Stopped** | Redundant telemetry & driver update updater |
| **`AcerDIAgentSvis`** | Acer Device Information Service | ❌ Bloat | **Disabled & Stopped** | Usage tracking and hardware data harvesting |
| **`AcerQAAgentSvis`** | Acer Quick Access Service | ❌ Bloat | **Disabled & Stopped** | Useless on-screen caps-lock & toggle popups |
| **`ASMSvc`** | Acer System Monitor Service | ❌ Bloat | **Disabled & Stopped** | CPU-heavy polling daemon |
| **`AcerServiceSvc`** | Acer Service Helper | ❌ Bloat | **Disabled & Stopped** | Legacy updater hook |
| **`AcerDeviceEnablingServiceV2`** | Acer Device Enabling Service | ❌ Bloat | **Disabled & Stopped** | Non-essential background trigger |
| **`AASSvc`** | Acer Agent Service | 🟢 **ESSENTIAL** | **PRESERVED (Running)** | Required for Hardware Named Pipe (RGB, MUX, Fan RPM) |
| **`AcerLightingService`** | Acer Lighting Service | 🟢 **ESSENTIAL** | **PRESERVED (Running)** | Low-level firmware interface for 4-zone lighting controller |

### Automated Service Optimization Engine
- Implemented `OptimizeAcerServices()` inside `src/Form1.cs` (`commit daeb3a7`).
- Automatically verifies service status on application launch.
- Created standalone utility scripts:
  - `disable_acer_bloatware.bat`: One-click script to stop and disable the 6 bloatware services.
  - `enable_acer_services.bat`: Recovery script to restore all services if ever needed.

---

## 6. Phase 5: High CPU Priority & Latency Immunity

To prevent PredatorControl from dropping fan speed adjustments, telemetry, or hotkey responses during 100% CPU spikes in AAA games:

### Three-Tier Priority Architecture
1. **Runtime Process Priority:**  
   In `src/Program.cs`, the process immediately elevates itself:
   ```csharp
   Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
   ```
2. **Task Scheduler Priority Elevation:**  
   In `src/StartupManager.cs`, updated the Task Scheduler XML definition to specify:
   ```xml
   <Priority>2</Priority>
   ```
   *(Windows Task Scheduler defaults to priority 7 [idle/background]; priority 2 guarantees High I/O and High CPU scheduling at logon).*
3. **Windows Registry IFEO (Image File Execution Options):**  
   In `src/Form1.cs`, registered registry keys under:
   `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\<ExeName>\PerfOptions`  
   with `CpuPriorityClass = 3` (High Priority) for:
   - `PredatorControlApp.exe`
   - `PredatorControl-win-x64.exe`
   - `PredatorControl-standalone.exe`

### Verification
Confirmed via PowerShell: `BasePriority = 13` (High Priority level), ensuring zero latency impact from background gaming load (`commit 1b3d7d6`).

---

## 7. Phase 6: The Dedicated Predator Key Hardware Hook

### The Challenge
The user requested that pressing the dedicated physical **Predator key** on the keyboard should seamlessly toggle the PredatorControl dashboard open and closed.

### Focus Bug in App Toggle
Inspection of `PredatorKeyHook.cs` and `Form1.cs` revealed that `ToggleApp()` already existed, but had a logic flaw:
```csharp
// Old flawed condition:
if (this.Visible && Form.ActiveForm == this)
{
    this.Hide();
}
```
If a game or browser was in focus, `Form.ActiveForm == this` evaluated to `false`, meaning pressing the key while gaming would never hide the app.  
**Fixed in `commit 4b8850c`:** Changed to `this.Visible && this.WindowState != FormWindowState.Minimized`.

### Developing Custom Low-Level Diagnostic Tools (`KeySniffer`)
Even with the toggle logic fixed, pressing the Predator key did nothing. The existing hook was checking for typical Acer scancodes: `VK_F23`, `VK_F24`, `0x71`, `0x5B`.

To stop guessing, we built a dedicated low-level Win32 Diagnostic Sniffer (`tools/KeySniffer.ps1` and compiled `tools/KeySniffer.exe`):
- Installed a low-level keyboard hook (`WH_KEYBOARD_LL`, ID `13`) via `SetWindowsHookExW`.
- Monitored `WM_KEYDOWN`, `WM_SYSKEYDOWN`, Virtual Key code, hardware Scan Code, and injection flags.

### The Hardware Revelation: VK=0xFF & SC=0x75
The user ran `KeySniffer.ps1` and pressed the physical Predator key:
```text
=== Key Sniffer started 16/09/2026 11:10:49 PM ===
[23:10:51.517] KEYDOWN  0xX2-6  255  0xX2-6  117  0xX2-4  VK_0xFF
[23:10:51.698] KEYDOWN  0xX2-6  255  0xX2-6  117  0xX2-4  VK_0xFF
[23:10:51.871] KEYDOWN  0xX2-6  255  0xX2-6  117  0xX2-4  VK_0xFF
```
**The Mystery Was Solved:**
- **Virtual Key Code:** `0xFF` (Decimal `255`, reserved OEM virtual key)
- **Scan Code:** `0x75` (Decimal `117`)

### Implementation & Breakthrough Confirmation
Updated `src/PredatorKeyHook.cs` with the confirmed hardware values:
```csharp
private const int VK_OEM_PREDATOR = 0xFF;              // 255 - confirmed Predator hardware key
private const int ACER_SCANCODE_PREDATOR_ACTUAL = 0x75; // 117 - confirmed Predator hardware scancode
```
Updated `IsPredatorKey()`:
```csharp
private static bool IsPredatorKey(int vkCode, int scanCode)
{
    if (vkCode == VK_OEM_PREDATOR || scanCode == ACER_SCANCODE_PREDATOR_ACTUAL)
        return true;
    ...
}
```
Recompiled and launched the application.  
**User confirmation:**
> *"ITS WORKING"*

Committed as `5d793a9` and pushed to `origin/Revs`.

---

---

## 8. Phase 7: In-Game Gaming Overlay HUD & ETW Engine

Following user request and reference design, a complete gaming overlay HUD was designed and integrated into version **1.1.5**.

### Architecture & Design Decisions
1. **Zero Game Injection**: Traditional overlays inject DLLs (Detours / MinHook) into DirectX / Vulkan swapchains, risking anti-cheat bans (Easy Anti-Cheat, BattlEye, Ricochet, Vanguard). PredatorControl implements a 100% external, kernel-level ETW (Event Tracing for Windows) frame monitor that never touches game memory.
2. **Double-Buffered Canvas**: Built using high-performance GDI+ rendering with smooth anti-aliased geometry, dark translucent backdrop (`#E10C1018`), neon cyan borders (`#4600E5FF`), and dual neon metrics (Green `#00FF80` for GPU, Teal `#00E5FF` for CPU).
3. **True Click-Through with Interactive Dragging**:
   - In standard mode, the window has `WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, allowing mouse clicks to pass straight through to the game with zero latency.
   - When holding `Ctrl + Shift`, `GameOverlayForm` removes `WS_EX_TRANSPARENT`, displays a glowing drag bar hint, and allows dragging anywhere on the screen with left mouse click.
   - Screen coordinates are saved to `HKCU\Software\PredatorControl\OverlayX` and `OverlayY`.

### Zero-Injection Kernel ETW Present Monitoring (`EtwFpsMonitor.cs`)
- Uses Windows Event Tracing via `advapi32.dll` (`StartTraceW`, `ControlTraceW`, `EnableTraceEx2`, `OpenTraceW`, `ProcessTrace`).
- Target Providers:
  - Windows 11: `Microsoft-Windows-DxgKrnl` (`{802EC45A-1E99-4B83-9920-87C98277BA9D}`) — Event ID 184 (`Present_Info`).
  - Windows 10: `Microsoft-Windows-DXGI` (`{CA11C036-0102-4A2D-A6AD-F03CFED5D3C9}`) — Event ID 42 (`Present_Info`).
- In-kernel event filtering (`EVENT_FILTER_TYPE_EVENT_ID`) delivers only Present timestamps directly into a lock-free QPC ring buffer.
- Auto-tracks active foreground window (`GetForegroundWindow` / `GetWindowThreadProcessId`) and filters out desktop shells (`explorer.exe`, `dwm.exe`).

### Double-Buffered GDI+ Canvas & 60s Sparklines
- **Large Neon FPS Counter**: Prominent 32pt bold header display.
- **60-Second Real-Time Sparkline**: Rolling performance history rendering CPU and GPU loads with antialiased gradients and min/max baseline markers.
- **Dual Thermals & Fan Tachometers**: CPU & GPU temperatures (°C) and live fan RPMs.
- **System Power & Battery Gauge**: Active GPU wattage (Acer EC sensor `0x0D`), battery charge percentage, and AC plugged/charging status.

### Controls & Persistence
- **Global Hotkey**: `Ctrl + Shift + O` (registered via Win32 `RegisterHotKey`).
- **Tray Menu**: Quick toggle item in the system tray menu (`Gaming Overlay (Ctrl+Shift+O)`).
- **Dashboard Switch**: Hardware toggle in System & Hardware Controls panel.
- **State Persistence**: Preserved across reboots via `HKCU\Software\PredatorControl\OverlayEnabled`.

---

## 9. Phase 8: Roadmap & Next Objectives

### Feature 3: Parallelized GPU Telemetry & iGPU Support
- **Objective**: Accelerate GPU sensor refresh latency on discrete NVIDIA RTX GPUs and add integrated Intel/AMD GPU telemetry.
- **Key Architecture**:
  - Parallelize sensor reads via asynchronous task batching to avoid serial EC bus round-trips.
  - Add integrated graphics metrics (frequency, load, memory) alongside discrete GPU stats.

---

## 9. Phase 9: Multi-Mode Gaming Overlay HUD & Native NVML/RAPL Telemetry (v1.1.7)

### Motivation & Visual Alignment
The user requested a faithful, pixel-precise recreation of the multi-mode gaming overlay HUD:
- **Light Mode:** Ultra-compact HUD displaying `FPS | GPU: [temp]° [gpu_power]W | CPU: [temp]° [cpu_power]W`.
- **Default Mode:** Balanced telemetry showing `FPS | GPU: [temp]° [rpm]RPM | CPU: [temp]° [rpm]RPM | 60s Sparkline Graph | [gpu_power]W / [cpu_power]W`.
- **Full Mode:** Complete hardware monitor showing `FPS | GPU: [temp]° [rpm]RPM | CPU: [temp]° [rpm]RPM | Graph | [gpu_power]W / [cpu_power]W | [gpu_load]% [bar] / [cpu_load]% [bar] | [vram]GB [bar] / [ram]GB [bar]`.

### Zero-Overhead Hardware Telemetry: Fixing "Stuck on dGPU"
1. **The Issue:** Acer WMI hardware method `0x0D` on Helios laptops often returns `0` or null for discrete GPU wattage when the GPU is in dynamic power states or idle.
2. **Native NVIDIA NVML P/Invoke (`src/NvmlGpuMonitor.cs`):**
   - Dynamic binding to `C:\Windows\System32\nvml.dll`.
   - Direct querying of `nvmlDeviceGetPowerUsage` (returns live milliwatts `mw / 1000.0f`).
   - Direct querying of `nvmlDeviceGetUtilizationRates` (live dGPU load %).
   - Direct querying of `nvmlDeviceGetMemoryInfo` (live VRAM used and total in GB).
   - Direct querying of `nvmlDeviceGetTemperature` (dGPU core temp).
3. **Intel RAPL CPU Package Power:**
   - Queried via Windows Performance Counter `\Energy Meter(rapl_package0_pkg)\Power` / 1000.0f.
   - Provides live CPU power consumption in Watts without requiring third-party kernel drivers.
4. **Physical RAM Utilization:**
   - Win32 `GlobalMemoryStatusEx` provides live RAM usage and total capacity in GB.

### Hotkeys & Interactive Gestures
- **`Ctrl + Shift + O`**: Toggle overlay on/off.
- **`Ctrl + Shift + Click`**: Cycle display mode (`Light` ➔ `Default` ➔ `Full` ➔ `Complete` ➔ `Light`).
- **`Ctrl + Shift + Drag`**: Move the overlay anywhere on the screen with coordinates persisted to Registry.
- **`Ctrl + Shift + Mouse Wheel`**: Dynamically scale HUD from 50% to 300% in 10% steps.
- **`Ctrl + Shift + Middle Click`**: Instantly reset HUD scale to 100%.
- **Hover & Key Guard:** Mouse input is only intercepted when the cursor is positioned directly over the overlay window, ensuring zero interference with active games.

---

## 10. Complete Git Commit Chronology

```text
ed44fb6 2026-09-08 rev_00: base busted state with backups and revisions
1f7c229 2026-09-08 rev_01: remove coolboost, mux, boot sound; fix static rgb and zones; remove acer agent client
a3a0b0b 2026-09-08 rev_02: fix static rgb mode timing (50ms reset / 20ms latch) and multi-zone ordering
bc568a9 2026-09-08 rev_03: fix firmware color latch flag (payload[8]=0x03), direction (payload[4]), and custom colors
06c2eb1 2026-09-08 rev_04: restore pristine working RGB packet structure, fix Static mode illumination & multi-zone
b790976 2026-09-08 rev_05: eliminate SetGamingLEDBehavior, add SetGamingRgbKb zone control, sync LightingProfile.ini
21c259d 2026-09-08 rev_06: fix 4-zone bitmask (1<<z) for static mode and unify animation color handling
c794901 2026-09-08 rev_07: fix All Zones button and 4-zone sequential hardware loop in static mode
badfe9f 2026-09-08 feat: add Jelli-themed README, hero banner, and clean .gitignore
e68a0aa 2026-09-08 fix: update badge and link URLs to Hadisovic/PredatorControl
8c670b3 2026-09-08 fix: clean and properly formatted GitHub markdown rendering
40f13c0 2026-09-08 docs: highlight Jelli cute UI implementation as coming soon
99c100a 2026-09-08 Add standalone and win-x64 releases, update LCD Overdrive WMI payload
e5555ed 2026-09-08 Remove obsolete revision executables, keep active running EXE, win-x64, and standalone
656e4b8 2026-09-08 Clean repository: remove historical revisions, old backups, and temporary files
5343959 2026-09-08 Fix 30-sec backlight idle sleep via Hardware Service Named Pipe, APGeAction, and SetGamingMiscSetting
8517795 2026-09-08 Pin update checker to Hadisovic/PredatorControl GitHub releases and set version to 1.0.0
05b366a 2026-09-08 Fix System.Management.dll reference stub replacing with runtime library to restore WMI sensors
c112daa 2026-09-08 Add UI preview screenshot to assets
ff3b7c8 2026-09-08 Add updated dashboard and controls preview screenshots
84a3900 2026-09-08 Add credits to original creator supesonly/Acer-P-Helper
6e9c66a 2026-09-10 Fix startup task: eliminate logon delay (PT0S), prevent task skip on reboot, add boot task
5e34300 2026-09-10 Bump version to 1.0.1 hotfix (zero-delay startup, boot limiter, and single-click tray)
d96d3d2 2026-09-10 Fix hardware power and fan modes: connect Acer OEM agent service, dual-dispatch ACPI profiles
8e14b8c 2026-09-11 Revert automatic fan mode override on Turbo mode selection: keep active fan mode
1814690 2026-09-14 Fix keyboard backlight getting stuck on Amber on boot/reboot: correct LightingProfile.ini offset
890f87c 2026-09-14 Fix RGB effect speed slider double-scaling calculation and optimize live RGB responsiveness
2498c8f 2026-09-16 Bump version to 1.1.0: Add GPU MUX switch support (Optimus, Discrete GPU, Auto)
71c11de 2026-09-16 Update release binaries to v1.1.0 with latest commit SHA
daeb3a7 2026-09-16 Add automatic Acer OEM services optimization to silence bloatware and telemetry
1b3d7d6 2026-09-16 feat: enforce High CPU Priority class, logon task priority, and IFEO registration
4b8850c 2026-09-16 fix: Predator key true toggle -- hides app regardless of focus state
5d793a9 2026-09-16 fix: Predator key -- add confirmed hardware codes VK=0xFF SC=0x75 captured via sniffer
1e94952 2026-09-17 fix: resolve overlay BackColor transparency crash and package standalone and win-x64 binaries
49a556c 2026-09-17 docs: update README with Gaming Overlay HUD, new screenshots, 4 essential Acer services, and 40MB footprint
1.2.2   2026-09-19 Release v1.2.2: Dedicated Overlay Settings dialog, universal chassis adaptation, Nitro/Predator profile routing, Ctrl+Shift hotkeys, embedded native WebView2Loader with zero-DLL dependency, and non-exp binaries.
```

---

## 11. Architecture & Reference Index

### Key Codebase Files
- **`src/Program.cs`**: Single instance enforcement (Mutex), High CPU priority assignment, application bootstrap.
- **`src/Form1.cs`**: Primary UI form, sensor telemetry polling timer, service optimization trigger, hardware profile dispatchers.
- **`src/PredatorKeyHook.cs`**: Low-level Win32 keyboard hook (`WH_KEYBOARD_LL`) capturing `VK=0xFF`, `SC=0x75` to toggle the app window.
- **`src/GameOverlayForm.cs`**: In-game Gaming Overlay HUD with GDI+ rendering, click-through, dragging, and sparklines.
- **`src/EtwFpsMonitor.cs`**: Zero-injection kernel ETW Present monitor (`DxgKrnl` / `DXGI`) for real-time FPS calculation.
- **`src/AcerAgentClient.cs`**: Windows Named Pipe client communicating directly with `\\.\pipe\AcerAgentPipe` (`AASSvc`).
- **`src/WmiController.cs`**: WMI interface interacting with `root\wmi` Acer hardware classes.
- **`src/TelemetryService.cs`**: Background telemetry worker polling hardware sensors, power, battery, and CPU usage.
- **`src/StartupManager.cs`**: Windows Task Scheduler integration for zero-delay, elevated startup.
- **`tools/KeySniffer.ps1`**: Standalone PowerShell Win32 keyboard hook sniffer for hardware key diagnostics.
- **`disable_acer_bloatware.bat`**: Utility script to disable 6 background Acer telemetry services.

---
*Created automatically for PredatorControl engineering documentation.*
