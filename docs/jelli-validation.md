# Validation record · 2026-09-17

Branch: `jelli`. Baselines and architecture: [jelli-architecture.md](jelli-architecture.md).

## Automated checks

- `.NET` Debug and Release builds: passed; no new compiler/analyzer warnings remain.
- Existing `SelfCheck.Run()` assertions executed through the Debug smoke-test entry point without creating hardware controllers.
- TypeScript strict check and production Vite build: passed.
- ESLint: passed.
- Node tests: 3 passed (single-click delay, double-click cancellation/no flash, cancellation on drag/context/unmount).
- Native WebView smoke tests: 27 passed. They cover coordinate clamping, settings validation/defaults, actual canvas mount, exactly two metric slots, real click arbitration, dashboard controls, semantic requests, errors, protocol rejection, remote navigation blocking, RGB default/rate/coalescing/stop/restore, flyout, quick menu, gaming hide/return, Escape, and renderer crash/reconnect.
- `npm audit`: zero vulnerabilities after updating Vite from the upstream dependency version to 8.3.0.
- `.NET` vulnerable-package query including transitive dependencies: none reported by the configured NuGet source.
- Self-contained Windows x64 single-file Release publish: passed. UI resources and native WebView loader are included. Output executable is approximately 119 MB on disk.
- Staged whitespace check: passed. Imported canvas trailing whitespace was removed in PredatorControl only.
- Protected-source comparison: WmiController, AcerAgentClient, TelemetryService, EtwFpsMonitor, DisplayCcdController, GameSyncController, GameProfile, SingleInstanceIpc, Updater and Program are byte-for-byte unchanged from the recorded PredatorControl master baseline.
- Jelli reference checkout remained clean at the recorded upstream revision.
- Source scan found no AI/provider/chat/runtime imports, networking calls, embedded secrets, or developer-specific absolute paths in the integration.
- Process inspection after smoke-test exit found no remaining app-owned WebView processes.

## Visual inspection

Captured the actual WebView compact, dashboard, attached-flyout and menu surfaces. Also captured the native compact window bounds to verify real desktop transparency rather than only a transparent browser canvas. Corrected a keyboard-focus label that initially appeared over the creature. Captures are ignored local test artifacts, not product telemetry or committed screenshots.

## Performance measurements

The smoke fixture uses the production host/renderer but no real hardware backend. Across recent runs, aggregate private working set was about **109–120 MiB**, private bytes about **155–168 MiB**, and summed total working sets about **395–407 MiB** (including shared pages in each process). Five WebView processes were present. Active drawing measured **600–601 canvas draws per 10 seconds** on high-refresh displays. Active UI CPU was about **1.2–1.6% of this 24-logical-processor machine**. These short measurements do not establish long-duration stability or a complete-product memory pass.

## Remaining acceptance gates

The current tool session is not elevated and an existing elevated PredatorControl instance is running. It was not terminated or replaced for testing. As a result, physical hardware writes, native real-game ETW/focus/click-through behavior, mixed-DPI/docking, startup tasks and live updater replacement remain unverified. The native fan-curve and Game Sync editors remain intentional migration surfaces rather than React rewrites.

**The full product's below-200 MB idle limit is not verified.** Do not infer that result from the UI-only fixture. Existing hardware handlers that do not expose firmware failures retain that limitation; the bridge reports exceptions it receives and does not claim successful physical verification. Run the manual checklist in the architecture document on an elevated compatible test machine before merging or distributing.
