# Release 1.2.5 Binaries

This folder contains pre-built binaries for testing and release v1.2.5:

- **`PredatorControl-standalone.exe`** (~50.4 MB)
  - Self-contained standalone executable with native single-file assembly compression.
  - No .NET installation required; double-click to run anywhere on any PC.
  - Native `WebView2Loader.dll` is embedded and auto-extracted, with full Jelli companion support.

- **`PredatorControl-win-x64.exe`** (~3.4 MB)
  - Ultra-lightweight framework-dependent executable (comparable to G-Helper).
  - Requires .NET 10 Desktop Runtime installed.

- **`WebView2Loader.dll`** (~160 KB)
  - Native runtime library for Microsoft Edge WebView2.

- **`appicon.ico`**
  - Application icon asset.

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
