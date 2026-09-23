# Release 1.2.6 Binaries

This folder contains pre-built binaries for testing and release v1.2.6:

- **`PredatorControl-standalone.exe`** (~52.8 MB)
  - Self-contained standalone executable with native single-file assembly compression.
  - No .NET installation required; double-click to run anywhere on any Windows 10/11 x64 PC.
  - Native `WebView2Loader.dll` is embedded and SHA-256 verified, with full Jelli companion support.

- **`PredatorControl-win-x64.exe`** (~3.68 MB)
  - Ultra-lightweight framework-dependent single-file executable.
  - Requires .NET 10 Desktop Runtime installed on the machine.

- **`WebView2Loader.dll`** (~160 KB)
  - Native runtime library for Microsoft Edge WebView2.

- **`appicon.ico`**
  - Application icon asset.

### Features & Fixes in v1.2.6:
1. **System Optimization SCM Engine (New)**:
   - Built-in toggle under **SYSTEM OPTIMIZATION** interacting directly with Windows Service Control Manager (`sc.exe` + `ServiceController`).
   - Disables & stops the 6 telemetry daemons (`AcerCCAgentSvis`, `AcerQAAgentSvis`, `AcerDIAgentSvis`, `ASMSvc`, `AcerServiceSvc`, `AcerDeviceEnablingServiceV2`) while preserving essential hardware services (`AASSvc`, `AcerLightingService`).
   - Live status counter displays actual daemon runtime status matching Windows Task Manager.
2. **Smooth Scrolling Margins**:
   - Fixed WinForms layout shift on mouse wheel scroll and child control focus updates.
   - Non-negative bounds clamping eliminates empty top gaps.
3. **Dedicated Overlay Settings Dialog**:
   - Granular metric toggles: FPS, Chart, RAM, Temperatures, Power, Battery, Fan, Load, Labels.
   - Presets: Light, Default, Full, and Complete with scale and opacity sliders.
4. **Security & Integrity Hardening**:
   - `nvml.dll` qualified from `%WINDIR%\System32` to eliminate search path hijack risks.
   - Embedded `WebView2Loader.dll` verified against SHA-256 hash before loading.
   - IPC named pipe restricted to current interactive user token.
5. **Universal Chassis Adaptation**:
   - Predator vs. Nitro dynamic identification and UI adjustments.
   - Multi-generation keyboard ACPI fallback.
