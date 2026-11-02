# Predator Control

A lightweight Windows control panel for supported Acer Predator and Nitro laptops, with a Classic dashboard, the optional Jelli companion, and a gaming metrics overlay.

[Downloads](https://github.com/Hadisovic/PredatorControl/releases) · [Issues](https://github.com/Hadisovic/PredatorControl/issues)

## Preview

| Jelli companion | Classic dashboard | System controls |
| --- | --- | --- |
| ![Jelli](assets/preview_jelli.png) | ![Dashboard](assets/preview_dashboard.png) | ![Controls](assets/preview_controls.png) |

![Gaming overlay](assets/preview_overlay.png)

## Compatibility

- Windows 10/11 x64 on supported Acer hardware.
- Features depend on the exact model, firmware, drivers, and installed Acer components. Predator and Nitro models expose different controls; unsupported options may be hidden or unavailable.
- Administrator access is required. Jelli also requires the [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/).
- GPU telemetry and display options depend on the installed GPU, driver, and panel. Some GPU mode changes require a restart.

## Install and use

1. Download an executable from [Releases](https://github.com/Hadisovic/PredatorControl/releases):
   - `PredatorControl-standalone.exe`: includes the .NET runtime.
   - `PredatorControl-win-x64.exe`: requires the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).
   - The ZIP asset provides the packaged build as an archive.
2. Keep the app in a dedicated folder and launch it as administrator.
3. Use the dashboard or tray menu to choose available controls. Switch between Jelli and Classic in Settings, or launch with `--legacy-ui`.
4. Enable **Start with Windows** in the app if desired. Use this setting again after switching executable versions to refresh startup registration.

Automatic updates require running the installed copy from a protected folder. Portable copies can be updated manually from Releases.

The latest packaged release is **v1.4.2**. Source on `master` also contains subsequent maintenance changes; existing release downloads are not rebuilt automatically.

## Features

Available where supported:

- Performance profiles and AC/battery preferences.
- Automatic, maximum, and custom fan control, fan curves, and CoolBoost.
- Keyboard brightness, effects, RGB zones, and saved lighting profiles.
- Battery charge limiting and health reports, startup sound/animation, USB charging, Windows key lock, and panel overdrive.
- GPU modes and display refresh-rate selection.
- Game Sync profiles, Jelli companion settings, and overlay customization.

The overlay offers Light, Default, Full, and Complete layouts. Metrics may be unavailable on some hardware or in some games. Game and anti-cheat compatibility varies.

| Shortcut | Action |
| --- | --- |
| `Ctrl + Shift + O` | Toggle overlay |
| `Ctrl + Shift + Click` | Cycle layout |
| `Ctrl + Shift + Drag` | Move overlay |
| `Ctrl + Shift + Scroll` | Adjust scale |
| `Ctrl + Shift + Middle Click` | Reset scale |

## Acer services and troubleshooting

Keep the Acer drivers and hardware services required by your model installed. The optional service optimizer targets selected background components; service repair asks for confirmation before installing or registering components. Neither option is required for initial setup.

If controls are unavailable, check model compatibility, Acer drivers/services, and administrator access. If Jelli cannot start, use Classic with `--legacy-ui` and check the WebView2 runtime.

For a compatibility report, run from PowerShell:

```powershell
.\PredatorControl-standalone.exe --diagnostics "$PWD\compatibility.json"
```

Review diagnostics before sharing them. Preferences, lighting profiles, and logs use the `PredatorControl` application-data folder and Windows settings. Protected startup and runtime files are managed by the app. Avoid manually editing those files or running multiple hardware-control applications simultaneously.

To switch versions, close the app from the tray, launch the desired executable, and refresh **Start with Windows** if enabled. `clean_switch_version.bat` is an optional helper for stopping existing instances; it does not change startup registration.

## Build and test

Requires Windows x64, the **.NET 10 SDK**, and **Node.js 24 LTS** with npm. Python 3.10+ is required only for the source scanner and its tests.

```powershell
git clone https://github.com/Hadisovic/PredatorControl.git
cd PredatorControl
npm.cmd ci --prefix jelli-ui
dotnet build src/PredatorControlApp.csproj -c Release

dotnet run --project tests/Compatibility.Tests -c Release
dotnet run --project tests/Keyboard.Tests -c Release
dotnet run --project tests/Security.Tests -c Release
npm.cmd test --prefix jelli-ui
npm.cmd run lint --prefix jelli-ui
python -m unittest discover -s tests -p "test_*.py"
```

The test suites use synthetic hardware data and temporary files. Privileged Windows integration and physical hardware behavior require separate validation in a disposable test environment and on supported devices.

Publish a self-contained executable:

```powershell
dotnet publish src/PredatorControlApp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish/standalone
```

## Source checks

Scan source and all fetched Git history, and save a redacted inventory of hardcoded values outside the repository:

```powershell
powershell -File tools/secret-scan.ps1 -History -OutputPath ..\hardcoded-scan.json
```

Git and Python 3.10+ must be on PATH. Exit codes are `0` for no findings, `1` for findings, and `2` for scanner errors. The JSON output lists locations, rules, and line fingerprints without printing matched secrets. Endpoints, paths, executable names, and constants are informational review items; hardware identifiers are often intentional.

The scanner checks versioned and unignored source files and, with `-History`, reachable text blobs across fetched refs. Dependency/generated directories, binaries, and files larger than 2 MiB are excluded; skipped large/binary text candidates are listed in JSON. Detection is heuristic and cannot prove that no secrets exist. Rotate or revoke any confirmed exposed credential.

For a reviewed false positive, create `.secret-scan-exceptions.json` with an array of exact exceptions, then rerun the scan:

```json
[
  {
    "rule": "credential-assignment",
    "path": "relative/path.cs",
    "fingerprint": "<64-character fingerprint from scanner output>",
    "reason": "Why this specific line is safe"
  }
]
```

Exceptions match one rule, path, and fingerprint; they do not suppress an entire directory. `-ExceptionsPath` selects an alternative exception file. Keep scan results and private audit notes out of commits.

## Project layout

- `src/`: Windows application and hardware integrations.
- `jelli-ui/`: Jelli frontend.
- `tests/`: synthetic compatibility, keyboard, boundary, and scanner checks.
- `scripts/` and `tools/`: optional helpers and maintenance commands.
- `assets/`: screenshots and branding.

## Credits

Inspired by [supesonly/Acer-P-Helper](https://github.com/supesonly/Acer-P-Helper). Acer, Predator, Nitro, and other product names belong to their respective owners.
