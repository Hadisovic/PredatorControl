# Experimental Test Binaries

This folder contains pre-built experimental binaries for testing on Predator and Nitro laptops:

- **`PredatorControl-exp-win-x64.exe`** (~3.5 MB)
  - Framework-dependent single-file executable.
  - Requires .NET 10 Desktop Runtime installed.

- **`PredatorControl-exp-standalone.zip`** (~46.8 MB)
  - Full uncompressed self-contained standalone executable (`PredatorControl-exp-standalone.exe`, ~119 MB) and `WebView2Loader.dll`.
  - No .NET installation required; double-click to run anywhere.
  - Provided in a zip package to adhere to GitHub's 100 MB per-file tracking limit.

### Features in this Experimental Build:
1. **Dedicated Overlay Settings Dialog**:
   - Access via **⚙ Options** button in the dashboard or via tray icon menu.
   - Granular metric toggles: FPS, Chart, RAM, Temperatures, Power, Battery, Fan, Load, Labels.
   - Presets: Light, Default, Full, and Complete.
   - Sliders: Size (Scale) and Transparency (Opacity).
   - Custom CPU & GPU Color swatches and Color Picker.
   - Reset button to restore all defaults.
   - "Overlay only in games" auto-hide mode.
2. **Universal Chassis Adaptation**:
   - Predator vs. Nitro dynamic identification and UI adjustments.
   - Triple-fan monitoring (Helios 18 / Triton 17X).
   - Multi-generation keyboard ACPI fallback.
