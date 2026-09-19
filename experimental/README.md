# Experimental Test Binaries

This folder contains pre-built experimental binaries for testing on Predator and Nitro laptops:

- **`PredatorControl-exp-win-x64.exe`** (~3.4 MB)
  - Framework-dependent single-file executable.
  - Requires .NET 10 Desktop Runtime installed.

- **`PredatorControl-exp-standalone.exe`** (~52.8 MB)
  - Self-contained single-file executable with native assembly compression (`EnableCompressionInSingleFile=true`).
  - No .NET installation required; double-click to run anywhere.
  - Reduced by 55.7% from the uncompressed 119 MB build.

- **`PredatorControl-exp-standalone.zip`** (~46.7 MB)
  - Compressed zip archive of the standalone executable.

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
