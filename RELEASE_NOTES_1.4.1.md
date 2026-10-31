# Predator Control v1.4.1

Release version `1.4.1` (`1.4.1.0`).

## Highlights & Fixes
- **Windows & Menu Key Lock Restored**: Re-enabled hardware keyboard matrix & EC firmware lock (`SetGamingRgbKb` + `APGeAction.SetFunction(2)` + OEM Agent `WIN_KEY` synchronization with verified polarity), alongside the Win32 low-level keyboard hook (`WH_KEYBOARD_LL`). Marked `winKey` as `supported` on Predator Helios / Helios Neo profiles (`PH16-71`, `PHN16-71`) and synchronized Classic UI, Tray menu, Jelli UI, and GameSync.
- **GPU MUX Switch Support (`PH16-71` / `PHN16-71`)**: Full runtime MUX capability detection across `AcerAgentService` initialization packets, OEM registry (`dgpu_mode_capability`), and live `GPU_MODE` readback, enabling Hybrid (Optimus), Discrete GPU, and Automatic (Advanced Optimus) switching with dual OEM Agent + ACPI WMI (`SetGamingMiscSetting` `0x02`) dispatch. Fixed startup GPU mode indicator when no local override has been saved yet.
- **Predator Performance Profile Switching & Jelli Status Badges**: Restored multi-target Predator thermal/power profile dispatch (`SetGamingProfile`, `0x01000000 | mode`, `SetGamingMiscSetting 0x0B`, and OEM Agent `OPERATING_MODE`) and isolated Jelli UI header status pills from transient error banners or stale operation results.
- **IPC Cleanup & Optimization**: Unified `AcerAgentClient` write-gate authorization across supported hardware features, removed duplicate JSON parsing boilerplate, and cleaned up intermediate build artifacts.

## Packages
- **`PredatorControl-standalone.exe`** — Self-contained single-file Windows x64 executable (includes .NET runtime).
- **`PredatorControl-win-x64.exe`** — Lightweight framework-dependent single-file Windows x64 executable.
- **`PredatorControl-win-x64.zip`** — Archive distribution.
- **`checksums.txt`** — SHA-256 checksums for automatic updater and manual verification.
