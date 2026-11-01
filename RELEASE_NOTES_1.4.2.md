# Predator Control v1.4.2

Release version `1.4.2` (`1.4.2.0`).

## Zero-Lag Hardware UI & Architecture Polish
- **Instant Hardware Mode & Fan UI (<1ms Response)**: Eliminated UI delay when switching power modes (Quiet, Balanced, Performance, Turbo, Eco), fan profiles (Auto, Max, Custom), or pressing the Mode key. Employs optimistic UI state updates (immediate button highlights, status labels, tray checkmarks, Jelli updates, and OSD popups) with asynchronous background hardware dispatch and automatic rollback if hardware rejects the command.
- **Parallel Hardware I/O & Fast IPC Timeouts (`WmiController.cs`, `AcerAgentClient.cs`)**: Run WMI commands and `AcerAgentClient` loopback TCP commands concurrently with tight 350ms timeouts instead of serial blocking waits.
- **`Form1` Modularization (Classic UI 100% Preserved)**: Cleanly separated the monolithic `Form1.cs` into organized C# partial classes (`Form1.cs`, `Form1.UI.cs`, `Form1.Events.cs`, `Form1.Tray.cs`, `Form1.Telemetry.cs`, `Form1.Preferences.cs`) while keeping every feature, control, and aesthetic of the Classic UI completely intact.

## Hardware Logic & Reliability Fixes
- **GPU MUX Switch & `AASSvc` Child Worker Verification**: Fixed OEM service process verification on systems where `AASSvc` (`AcerCentralService.exe`) spawns `AcerAgentService.exe` as a child worker in the same `DriverStore\FileRepository` package, including unelevated WMI fallback verification. Restores full GPU MUX capability detection (`GpuModeMask = 7`), live `GPU_MODE` readback, and Hybrid / Discrete / Advanced Optimus switching.
- **H1 — Strict Predator Fan & Power Write Verification (`WmiController.cs`)**: Removed `|| _cachedObj != null` fallbacks from `SetFanBehavior`, `SetFanSpeed`, and `ApplyPredatorPowerProfile`. OEM Agent calls (`AcerAgentClient.SetOperatingModeAsync`, `SetFanModeAsync`, `SetCustomFanSpeedAsync`) are now awaited via `AgentAccepted` instead of fire-and-forget tasks, and `ApplyPredatorPowerProfile` verifies `GetOperatingModeAsync` readback to report `"confirmed"` when matched.
- **H2 — Hardware Thermal Floor Protection (`HardwareCompatibility.cs`, `Form1.cs`)**: Added `FanCurveSafety.FloorSpeed(cpuTemp, gpuTemp)` enforcing minimum fan speeds at high temperatures (`>=80°C -> 55%`, `>=85°C -> 70%`, `>=90°C -> 85%`, `>=95°C -> 100%`) across both custom fan curves (`ApplyFanCurve`) and fixed manual fan speeds (`EnforceManualThermalFloor`).
- **H3 — Crash-Resilient Manual Fan Recovery (`WmiController.cs`, `Form1.cs`)**: Persists `ManualFanActive` in `HKCU\SOFTWARE\PredatorControl` whenever manual fan control is engaged and clears it when returning to Auto/Max. On startup, `RecoverCrashLeftoverManualFans()` immediately restores firmware Auto before evaluating saved preferences if a prior session crashed or was force-killed.
- **H4 — Confirmed Hotkey Scan-Code Filtering & Chassis Gating (`PredatorKeyHook.cs`)**: Removed unverified speculative scan-code pairs (`0x54`, `0x6C`, `0x6D`) and gated OEM key hook interception (`IsModeKey`, `IsPredatorKey`, WMI event watchers, and `AppKey` registration) to verified Acer gaming chassis (`Predator` and `Nitro`).

## Security Hardening
- **S1 — Admin-Only Scheduled Task Binary Staging (`SecureLaunch.cs`, `Form1.cs`)**: Scheduled tasks (`PredatorControl` logon task and `PredatorControlBoot` SYSTEM boot task) now stage the executable into `%ProgramFiles%\PredatorControl\PredatorControl.exe` and verify that the binary and every ancestor directory are writable only by `NT AUTHORITY\SYSTEM`, `BUILTIN\Administrators`, or `NT SERVICE\TrustedInstaller` with no reparse points before registering.
- **S2 — Rooted `System32` Process Launches (`SecureLaunch.cs`, `OemServiceManager.cs`, `Updater.cs`, `BatteryHealthMonitor.cs`, `Form1.cs`)**: Replaced all bare executable names (`sc.exe`, `net.exe`, `pnputil.exe`, `cmd.exe`, `powercfg.exe`, `shutdown.exe`, `schtasks.exe`, `tasklist.exe`, `find.exe`, `timeout.exe`, `certutil.exe`, `chcp.com`) with rooted `%SystemRoot%\System32` paths, and moved update staging and batch execution out of `%TEMP%` into admin-only `%ProgramData%\PredatorControl` and `%SystemRoot%\System32`.
- **S3 — Redirect-Safe HTTPS Updater Host Validation (`Updater.cs`)**: Validates both the binary download URL and `checksums.txt` URL against `IsTrustedUpdateUri` (`github.com` and `objects.githubusercontent.com` over HTTPS) and disables automatic HTTP redirects to validate every redirect hop (up to 5 hops).
- **S4 — `ProgramData` ACL & Reparse-Point Checks (`AcerAgentClient.cs`, `WmiController.cs`)**: Verifies `SecureLaunch.IsAdminOnly` before passing `C:\ProgramData\oem\AcerAgentService\BIOSLogo\AcerLogo.gif` to `AcerAgentService` or reading `C:\ProgramData\Acer\QA\settings.json`.
- **S5 — Post-Connect Loopback Socket Ownership Verification (`AcerAgentEndpoint.cs`, `AcerAgentClient.cs`)**: Closes the check-then-connect gap on `127.0.0.1:46933` by verifying via `GetExtendedTcpTable` (`TCP_TABLE_OWNER_PID_CONNECTIONS`) after `ConnectAsync` that the server side of the established loopback connection is owned by the trusted `AcerAgentService` PID before transmitting any packet.

## Memory & Runtime Optimization
- **Win32 Working Set Trimming & Compacting GC (`Program.cs`, `Form1.cs`, `JelliHostForm.cs`)**: Added rate-limited `EmptyWorkingSet` trimming with post-startup Gen 2 heap compaction and automatic working-set release when transitioning to tray or compact Jelli mode.
- **WebView2 Low-Memory Target & Embedded Flags (`JelliHostForm.cs`)**: Enabled `CoreWebView2MemoryUsageTargetLevel.Low` and added embedded Chromium flags (`--renderer-process-limit=1`, `--disable-features=AudioServiceOutOfProcess,IsolateOrigins,site-per-process`, `--disable-background-networking`, `--disable-extensions`) to reduce `msedgewebview2.exe` memory retention.
- **Differential Compact-Mode Telemetry (`JelliHostForm.cs`, `Form1.Jelli.cs`, `jelli-ui`)**: Replaced 1Hz full-dashboard JSON serialization with lightweight `telemetry` delta messages while Jelli is in compact desktop mode, eliminating continuous JSON and DOM allocation churn.
- **Lazy Secondary Window & PerformanceCounter Loading (`Form1.cs`, `TelemetryService.cs`)**: Deferred `GameOverlayForm` (`EtwFpsMonitor` kernel trace session and GDI double buffers) and Windows `PerformanceCounter` (`pdh.dll` / `Energy Meter`) initialization until explicitly enabled by the user.

## Packages
- **`PredatorControl-standalone.exe`** — Self-contained single-file Windows x64 executable (includes .NET runtime).
- **`PredatorControl-win-x64.exe`** — Lightweight framework-dependent single-file Windows x64 executable.
- **`PredatorControl-win-x64.zip`** — Archive distribution.
- **`checksums.txt`** — SHA-256 checksums for automatic updater and manual verification.
