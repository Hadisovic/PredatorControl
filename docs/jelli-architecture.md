# Jelli UI integration

## Source baseline and boundary

Implemented on `jelli`, based on PredatorControl master `8d998521eca771c7a17a3e9507308885c6ab3d1b`, fetched on 2026-09-17. The imported canvas comes from Jelli master `c493a7933cd41ae3feaf08f9136d5cc112e0ccc4`, fetched the same day. The separate Jelli reference checkout was not edited.

The existing WinForms process remains the elevated hardware authority. One WebView2 hosts React and the actual upstream canvas. This avoids Electron, Tauri/Rust duplication, a localhost server, a second tray icon, and a second startup task. WebView's sandboxed browser/renderer/utility processes are internal implementation details, disposed with the host.

The native Form1 controls remain an internal command adapter and an explicit fallback. No WMI commands, EC constants, service optimization logic, telemetry calculations, display driver logic, fan interpolation, Game Sync profile behavior, or ETW monitoring were rewritten. The two UI controls expose internal invocation methods so the new adapter can reuse existing handlers without reflection. Form1 gains shell routing, telemetry snapshot capture, overlay suspension, and lifecycle cleanup. RGB sync temporarily uses the existing lighting queue and yields to Game Sync.

## Feature inventory and locations

| Existing capability | Jelli destination | Existing implementation reused |
| --- | --- | --- |
| Quiet/Balanced/Performance/Turbo/Eco | System / Performance; quick menu | Power buttons and live AC gating |
| AC and battery automatic profiles | System / Performance | Existing profile dropdown handlers |
| Auto/Max/Custom fans | System / Cooling; quick menu | Existing fan-mode handlers |
| CPU and GPU fixed speed | System / Cooling, Custom | Existing committed sliders, 10–100% |
| Precise CPU/GPU curves; reset/apply | Cooling / Edit fan curve | Native graph editor, ocean palette; original interpolation |
| Internal refresh rates | System / Displays; quick menu | Existing CCD paths |
| External monitors and driver-reported rates | System / Displays | Existing monitor discovery and apply handler |
| Hybrid/Discrete/Advanced Optimus | System / GPU; quick menu | Capability bitmask, existing confirmation/restart handling |
| 80% battery limit | System / Battery care; quick menu | Existing battery operation and rollback |
| Nine keyboard effects, presets, custom colors | Lighting; quick menu | Existing lighting handlers |
| Individual four-zone and all-zone color | Lighting keyboard/zone controls | Existing zone selection; firmware effect gating |
| RGB brightness/speed and 30s timeout | Lighting | Existing controls |
| LCD overdrive and Windows/Menu key lock | System / Displays and Hardware; quick menu | Existing handlers/hooks |
| Native theme selection | Settings / Application | Teal/Crimson/OLED theme selection for native editors/fallback |
| Game Sync enable, profile create/edit/delete, executable picker | Games / Configure game profiles | Complete native Game Sync editor |
| Startup task registration | Settings / Application | Existing elevated scheduled-task path |
| Update check, notes, download, replacement | Settings / Application | Existing updater, embedded UI assets travel with executable |
| Tray and Predator key | Dashboard open/collapse; original quick actions retained | Existing single-instance pipe and hooks |
| Gaming overlay, Ctrl+Shift+O | Games and Settings; quick menu/tray/hotkey | Native zero-injection ETW overlay |

Native curve/Game Sync/update/reboot dialogs are retained deliberately during migration. Their labels use ↗ where they open an advanced native editor. The native fallback can be opened from Settings or with `--legacy-ui`. Hardware functions merely described in historical documentation but absent from the actual master UI were not invented.

## Communication

`JelliProtocol.cs` defines C# records; `jelli-ui/src/bridge/types.ts` and `client.ts` define the frontend contract. The dedicated version-1 WebView message channel carries `{version,id,action,data}` requests, `{version,type:"response",id,data,error}` responses, and state/layout/cursor/suspend events. Requests are limited to 16 KiB. Unknown versions/actions/control IDs and invalid settings/ranges are rejected. No script host objects are exposed.

Commands are semantic and allowlisted. Control IDs such as `power.quiet`, `fans.cpu`, `battery.limit`, and `rgb.color` map to existing C# handlers. The UI never receives raw Acer opcodes. Control availability comes from existing control states, GPU capability bits, and OS display rates. The old public `SHOW / TOGGLE / CYCLE_MODE / EXIT` named pipe is unchanged.

State snapshots run once per second and reuse TelemetryService snapshots. There is no JavaScript sensor poller. Unsupported values display an em dash; unavailable options are omitted or disabled. Desktop FPS is unavailable unless the native overlay is sampling. GPU utilization is explicitly labeled as the existing backend estimate; its calculation is unchanged.

The host supplies a single cursor/window snapshot at about 31 Hz. The canvas consumes cached values, interpolates eyes using upstream logic, and renders at up to 60 Hz even on high-refresh monitors. Hardware actions execute in C#, outside the React renderer process. Existing synchronous native handlers may temporarily delay the host's message responses; the renderer remains separate. Exceptions are returned as errors. Existing handlers that swallow firmware failures or optimistically update native state retain that limitation; the new UI does not add success notifications claiming verified hardware state.

Navigation is restricted to the packaged origin. HTML/CSS/JS are embedded resources served by WebView resource interception, not by an HTTP server or writable downloaded page. CSP denies networking, frames, plugins and external scripts. Downloads, new windows, browser shortcuts and permission requests are blocked. DevTools require `--jelli-devtools`.

## Creature integrity

Only BlobCanvas was imported, not the standalone application shell. Bell geometry, tentacles, physics, organs, particles, glows, color interpolation, eye morphing, blink, proximity, petting, shy/surprised/happy/annoyed/dizzy/mad/sad/sleep transitions and their timing constants remain upstream code. Window APIs and click actions are the integration seams. The chat store, config store, typing/thinking triggers and their rendering branches were removed. No providers, keys, messages, memory, TTS, networking, AI sidecars or AI packages were imported. Hardware state is never an input to the expression state machine.

The canvas publishes its dominant HSL color at most five times per second, with a quantized change threshold. CSS variables are updated directly, without React state updates or pixel-buffer sampling. The two floating metrics use one restrained CSS breathing animation; reduced-motion preferences disable it.

## Desktop and gaming

The WebView HWND sits on a black WinForms host with DWM glass extended across the client area. Do not use `TransparencyKey` here: Windows color-key hit testing ignores the separately composed WebView visuals and passes mouse input through the host. Native input regression tests exercise the Windows hit-test path, clicks, dragging and wheel scrolling in addition to renderer-level event tests.

Single click waits for Windows' double-click interval before showing the quick summary. A second click cancels it and opens the dashboard. Drag cancels pending click arbitration. Right click opens a themed keyboard-navigable menu. Escape, outside click, and focus loss close transient surfaces. Expand is the default; the attached flyout opens left when right-hand space is insufficient.

Physical desktop anchor coordinates persist in the existing HKCU `SOFTWARE\PredatorControl` registry under `JelliUI`. Layout clamps against per-monitor work areas, supports negative coordinates, and reflows on display/DPI changes. Creature size and panel size use the current monitor's DPI. Per-monitor transitions and docking still require a physical multi-monitor acceptance pass.

Gaming hides the entire desktop host, stops cursor messages, signals suspension and invokes WebView `TrySuspendAsync`. Restoring calls `Resume`, restores layout/state and restarts cursor updates. The native HUD always uses `WS_EX_TRANSPARENT`, `WS_EX_NOACTIVATE`, `HTTRANSPARENT` and `MA_NOACTIVATE`. The modifier-key polling/drag path was removed. Settings control corner, monitor, graphs, power/battery and utilization columns. The original ETW and sparkline data calculations are unchanged.

## Keyboard sync

Off by default. Dominant canvas color is queued in C# and applied at most every 750 ms (1.33 Hz), only when the summed RGB delta reaches 35. Existing backend lighting coalescing remains in charge of actual writes. Manual zone colors and registry lighting preferences are not overwritten by transient sync. Disabling restores manual lighting; Jelli manual RGB actions disable sync first. A detected Game Sync session restores the manual baseline before taking its snapshot and prevents sync writes during the session. Sync is suspended while gaming hides Jelli.

## Build, update, and recovery

MSBuild runs the TypeScript/Vite build and embeds all resulting files. `tools/build-jelli.ps1` restores the lockfile, lints, tests, and publishes a self-contained single executable including native WebView loader extraction. The Microsoft Edge WebView2 Evergreen Runtime is a prerequisite, not a second manually launched product. No Node or development server is needed on the target computer. The existing executable-replacement updater therefore carries UI updates with the same payload.

Initialization has a 15-second handshake timeout and falls back to the native UI. Renderer failures trigger bounded reload attempts, then native fallback. Frontend request IDs, timeouts, watchdog/reconnect and error surfaces handle lost responses. Tests exercise the real WebView lifecycle without constructing any hardware controller. Production disposal closes WebView and its timers; smoke-test process inspection found no remaining PredatorControl WebView processes after exit.

## Performance status and release gates

The WebView host uses software canvas rendering plus a small V8 heap (`--max-old-space-size=16 --max-semi-space-size=1 --optimize-for-size`). These are Chromium implementation options and should be rechecked after Evergreen updates. They reduced this machine's isolated smoke-test footprint substantially while retaining about 60 canvas draws/second. The standalone Jelli renderer initially ran at this monitor's 165 Hz; the integration avoids that extra work.

One measured run: 112.4 MiB aggregate **private working set**, 159.4 MiB aggregate **private bytes**, and 395.0 MiB summed total working sets (the latter double-count shared DLL pages) across the test host plus five WebView processes. CPU was 1.37% of this 24-logical-processor machine during active drawing. These are measurements of the isolated UI fixture, not a guarantee for the full application. Results vary with WebView version, uptime and renderer state. The real hardware backend was intentionally not launched over the user's already-running PredatorControl instance.

The **complete application's below-200 MB idle requirement is not yet verified**. Neither is hardware action parity on the physical laptop, a real game's focus/click-through/ETW behavior, long-duration memory stability, or docking/mixed-DPI behavior. These remain release acceptance gates, not claims of completion. Use the manual checklist below before merging or distributing the Jelli build.

### Manual acceptance checklist

1. Exit the currently running PredatorControl through its tray. Launch the published Jelli executable elevated.
2. Confirm a single tray/startup presence, real CPU/GPU values, two configurable desktop slots, drag persistence, sleep/wake/pet/long-drag expressions, single/double/right click and keyboard navigation.
3. Compare every inventory row with the native fallback. Confirm disabled AC/GPU options and backend error/reboot dialogs; cancel MUX changes when merely testing the UI.
4. Exercise both presentation modes at every monitor edge, a negative-coordinate monitor, mixed DPI, resolution change, docking and monitor removal.
5. Verify RGB sync stays off initially, changes slowly when enabled, stops/restores manual zones when disabled, and yields to a real Game Sync profile.
6. Enter a game and toggle Ctrl+Shift+O. Confirm no creature, no intercepted clicks/focus, native real FPS, selectable corners/monitor, and clean return to the prior Jelli state.
7. Measure combined private working set and private bytes of the application and all its WebView child processes after at least ten idle minutes, then after repeated open/collapse/game cycles. Do not report a sub-200 MB pass from the UI-only fixture.
8. Exit and verify no app-owned WebView processes remain. Test startup and a packaged update on a compatible test machine.
