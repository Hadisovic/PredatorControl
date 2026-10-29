# Predator Control 1.4.0 revision 2

Product/base version 1.4.0; informational version 1.4.0+rev.2; file/manifest version 1.4.0.2. This continues 1.4.0 rather than introducing a 1.4.1 release.

## Nitro keyboard correction

Detected Acer Nitro laptops now use the requested fixed-red, single-color keyboard policy. This includes AN515-54, AN515-58 and Nitro/Nitro V model identifiers. Nitro detection takes precedence over generic RGB method metadata and old saved RGB/keyboard-type overrides.

- The keyboard menu exposes backlight **On/Off** and the separate **30-second inactivity timeout** setting.
- A red single-color keyboard illustration replaces the four-zone RGB preview.
- Brightness sliders, effect selection, color pickers, RGB presets, zone buttons and RGB profile selection are absent for Nitro.
- Nitro's keyboard type cannot be overridden to RGB. The Classic compatibility selector reflects the fixed Nitro profile without changing the Classic window layout.
- Non-Nitro RGB keyboards retain their existing controls.
- The red illustration represents a fixed hardware color; the application does not send unsupported RGB writes to make a single-color keyboard red. Existing first-launch preservation and readback requirements for On/Off and timeout remain in place.

## Original 1.3.5 size restored

The Jelli window-geometry implementation was restored directly from the original source backup and verified to match exactly:

| Surface | Logical dimensions |
| --- | --- |
| Expanded dashboard | 430 × 600 |
| Attached flyout including companion area | 610 × 600 |
| Summary | 330 × 400 |
| Quick menu | 430 × 570 |
| Floating companion window | 180 × 180 |

Windows display scaling still applies. Current dashboard design and spacing are retained; only the window dimensions were reverted. The companion is not scaled down.

## Validation and use

Release self-contained publish, frontend build, ESLint, 54 isolated keyboard checks and six frontend tests pass. Tests cover AN515-54 despite misleading RGB metadata and saved RGB overrides. Browser verification with simulated AN515-54 data confirmed red-only presentation, zero sliders/color inputs/dropdowns in the keyboard menu, a 430 × 600 viewport and an unscaled companion.

No hardware commands, OEM service changes or user preference changes were executed during development. Physical firmware operation still requires testing on the actual model and BIOS. If the OEM configuration cannot be read or a write cannot be confirmed, the application reports failure/unverified state and does not save a false success.

Close the older running instance, extract this revision into a new folder, and launch its `PredatorControlApp.exe`. .NET is bundled; Jelli still requires the installed WebView2 Runtime. Older builds remain available for rollback. This revision is in `releases/1.4.0/revision-2/`.
