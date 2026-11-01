# Predator Control v1.4.2

Release version `1.4.2` (`1.4.2.0`).

## Highlights & What's New

- ⚡ **Instant Zero-Lag UI**: Power mode switching (Quiet, Balanced, Performance, Turbo, Eco), fan profiles, and the hardware Mode key now react in <1ms with immediate visual feedback and non-blocking background hardware dispatch.
- 🎯 **GPU MUX Switch Reliability**: Improved GPU mode detection and switching (Hybrid, Discrete, Automatic) across Predator Helios and Nitro systems with child-worker service detection.
- 🛡️ **Hardware & Thermal Safety**: Added automatic thermal floor protections during high loads, verified fan write confirmations, and crash-safe fan speed recovery on startup.
- 🔒 **Security Hardening**: Hardened auto-start scheduled tasks to admin-only system directories, enforced HTTPS redirect security on updates, and verified local IPC connections.
- 🚀 **Lower Memory & Resource Usage**: Substantially lowered RAM and CPU footprint through aggressive working set trimming, lightweight telemetry streaming, and low-memory WebView2 targets.
- 🧹 **Modular Architecture**: Cleanly modularized the core engine into dedicated modules for greater stability and maintainability while preserving 100% of the classic UI design.

## Packages
- **`PredatorControl-standalone.exe`** — Self-contained single-file executable (no .NET installation required).
- **`PredatorControl-win-x64.exe`** — Lightweight single-file executable (requires .NET 10).
- **`PredatorControl-win-x64.zip`** — Portable archive bundle.
- **`checksums.txt`** — SHA-256 integrity checksums.
