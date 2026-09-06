# Predator Control — Project Status & Handover Report

**Generated:** September 5, 2026  
**Target Hardware:** Acer Predator Helios Neo 16 (`PHN16-71`)  
**Workspace:** `C:\Users\youse\Downloads\PredatorControl-Unpacked`

---

## 1. Executive Summary & Honest Assessment

Over the course of recent iterations, several features were attempted simultaneously (GPU MUX switch, 4-Zone RGB overhaul, BIOS Boot Animation/Sound), which led to regressions—specifically breaking the keyboard RGB that was previously working.

This document outlines:
1. **Exactly what was changed and what broke.**
2. **Where the project stands right now.**
3. **The technical root causes discovered.**
4. **Concrete, step-by-step actions for what to do next.**

---

## 2. Feature Breakdown & Current State

| Feature | Status | Details |
| :--- | :---: | :--- |
| **Keyboard RGB Lighting** | **Restored to WMI** | Attempted to rewrite RGB using `SetGamingRgbKb` and TCP agent packets, which broke lighting. **Reverted back to the original working WMI `SetGamingLEDBehavior` method** with the original action queue. |
| **GPU MUX Switch** | **Needs Native Solution** | Fixed the dropdown UI mapping (0 = Optimus, 1 = Discrete, 2 = Auto) and added the restart prompt dialog. However, the backend was attempting to route via TCP to `AcerAgentService:46933`, which fails when PredatorSense is uninstalled. |
| **BIOS Boot Animation & Sound** | **Needs Native Solution** | Unified into a single switch ("BIOS Boot Animation & Sound") in the UI. However, backend dispatched via TCP to `AcerAgentService:46933`, which fails when the service is not running. |
| **Fan Speeds & Custom Curves** | **Working** | Controlled directly via ACPI WMI (`SetGamingFanSpeed`, `SetGamingFanBehavior`). Unaffected. |
| **Power Modes (Quiet/Bal/Perf/Turbo)** | **Working** | Controlled directly via ACPI WMI (`SetGamingMiscSetting` Register `0x0B`) + Windows Power Scheme overlay. |
| **Battery Charge Limit (80%)** | **Working** | Controlled directly via ACPI WMI (`BatteryControl.SetBatteryHealthControl`). |
| **LCD Overdrive & CoolBoost** | **Intact** | Left untouched per user instructions. |

---

## 3. What Went Wrong & Key Technical Discoveries

### A. Why the RGB Broke
1. In an effort to support 4 individual zones, we switched from the laptop's verified working WMI method (`SetGamingLEDBehavior`) to `SetGamingRgbKb` (from reverse-engineering `AcerECKeyboardController.dll`) and added TCP packets to port 46933.
2. The laptop's Embedded Controller (EC) firmware did not accept this method for static zone writes, causing the keyboard lighting to stop responding.
3. **Resolution**: Restored the original `SetGamingLEDBehavior` logic in `src\WmiController.cs` that previously worked.

### B. Why the MUX Switch & Boot Sound Did Not Work
1. When reverse-engineering PredatorSense (`app.asar`), we found that PredatorSense communicates via TCP (`127.0.0.1:46933`) with a background process called `AcerAgentService.exe`.
2. When PredatorSense was uninstalled from Windows, **`AcerAgentService` was uninstalled along with it**, meaning port 46933 was closed. Every TCP call was actively refused by the system.
3. Even more importantly: **The core philosophy of Predator Control is 0 background services and 100% standalone operation** (as highlighted in `PREDATOR_SENSE_FEATURE_COMPARISON.md`). Relying on an external Acer background service violates this goal.
4. For GPU MUX and BIOS Boot Sound to work cleanly without PredatorSense installed, they must be controlled either:
   - Directly via **ACPI WMI** (`root\wmi` `AcerGamingFunction` / `AcerBiosConfigurationTool`), OR
   - Through direct kernel IOCTL / NVRAM variables without a bulky background service suite.

### C. Lack of Version Control
- Rapid edits across multiple files (`Form1.cs`, `WmiController.cs`, `AcerAgentClient.cs`) without a Git commit history made rollback difficult and caused confusion.

---

## 4. Current Codebase Files

- **`src\WmiController.cs`**:
  - RGB lighting restored to original working WMI implementation (`SetGamingLEDBehavior` with 4-zone grouping).
  - Fan controls, power modes, battery health, and sensors are active and working.
- **`src\Form1.cs`**:
  - Contains full UI controls for Fans, Curves, Power Modes, 4-Zone RGB chip selection, Battery Limit, and System Toggles.
  - GPU MUX dropdown has the restart prompt logic.
- **`src\AcerAgentClient.cs`**:
  - Implements the TCP client for OEM agent port 46933 (currently dormant if service is uninstalled).
- **`src\WmiController.cs.bak` / `src\Form1.cs.bak`**:
  - Backups of the clean pre-overhaul state.
- **`BACKUP_ORIGINAL_PRE_FEATURES/`**:
  - Pristine original source code archive.

---

## 5. Roadmap: What To Do Next (Clean Session Plan)

When starting a fresh chat or resuming work, execute in this exact sequence:

### Phase 1: Set Up Version Control First (MANDATORY)
1. Initialize a **Git repository** in the project directory.
2. Commit the current stable state as `v1.4.1-baseline`.
3. Create a branch for any new experiment so changes can be reverted in one click (`git checkout main`).

### Phase 2: Verify Restored RGB
1. Build and run `PredatorControlApp`.
2. Verify that keyboard lighting (Presets, Colors, Brightness) responds as it did originally.

### Phase 3: Implement Native GPU MUX Switch (No OEM Service)
1. Investigate the direct ACPI WMI method on `AcerGamingFunction` or BIOS NVRAM for GPU switching.
   - In Acer laptops, display mode is governed by an ACPI method or NVRAM variable (`DisplayMode` / `GPU_Direct`), not just a proprietary TCP service.
2. If WMI does not expose MUX switching on the PHN16-71, identify the exact registry/driver entry used by the NVIDIA driver (`DisplayModeSwitch` / Advanced Optimus NVAPI) which can switch MUX without Acer software.

### Phase 4: Implement Native BIOS Boot Sound & Animation
1. Check ACPI WMI `AcerGamingFunction.SetGamingMiscSetting` Register `0x04` (Boot Sound).
2. Test writing directly via elevated WMI to determine if BIOS NVRAM updates without AcerAgentService.

---

*Keep this document as the reference point for the next session.*
