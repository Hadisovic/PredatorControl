# Predator Control 1.4.1

Compact dashboard refinement. File/assembly version: 1.4.1.0.

- Expanded dashboard reduced from 760 × 760 to 480 × 560 logical pixels, approximately 53% less screen area. Windows display scaling still applies.
- Attached flyout panel reduced to 480 pixels wide, plus Jelli's existing 180-pixel companion area.
- Short tab headings replace oversized slogans. Telemetry, buttons, navigation and cards use tighter spacing and smaller corner radii.
- Keyboard On/Off choices sit alongside their labels. Compatibility explanations and keyboard-type selection remain accessible in an expandable section.
- Overview quick actions are concise; detailed battery health moves into an expandable row.
- Jelli, themes, keyboard startup protection, on/off control and independent timeout behavior from 1.4.0 are retained.

Validation: full Release publish, frontend build, lint and all six frontend tests. Browser checks use simulated Nitro data at 480 × 560 and narrow layouts; no hardware commands are issued during UI testing. Physical model/BIOS validation remains pending as documented in the 1.4.0 release notes.

Close any older running instance before launching `PredatorControlApp.exe` from this release. Existing 1.4.0 binaries are preserved. The self-contained Windows x64 build includes .NET; Jelli still needs the installed WebView2 Runtime.
