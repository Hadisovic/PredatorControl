# Dead-code and maintenance analysis

Scope: source at audit baseline `3468846`, rechecked after security changes on `revisions`, 2026-11-03. No component, supported device, dependency, asset, binary or compatibility implementation was deleted solely because a text search found no caller.

## Method and dynamic entry points

Reviewed the tracked inventory, SDK project inclusion, frontend imports, command switches, native imports, resource loading, event subscriptions and targeted symbol references. `dotnet format style src/PredatorControlApp.csproj --verify-no-changes --diagnostics IDE0051 IDE0052 --severity info --no-restore` completed successfully with no reported findings. This is a scoped private-member analysis, not proof that every public API or runtime path is live. The full .NET analyzer run also reports maintenance/disposal/naming warnings; they are not treated as automatic deletion instructions.

Dynamic or indirect use that must be preserved includes:

- WinForms Designer/resource members, timer/event delegates, WndProc/hotkeys, tray and deferred actions.
- WMI methods and OEM names addressed as strings; compiled device profiles selected from manufacturer/model/board/BIOS evidence.
- WebView2 requests, per-control/menu dictionaries, embedded frontend resource names and JSON serialization.
- Native P/Invoke structures/delegates, ETW callbacks and the embedded loader resource.
- Debug-only `SelfCheck`, CLI diagnostics/repair/optimization/headless startup and scheduled-task arguments.
- Security tests that deliberately use reflection against non-public security functions. These methods are not dead merely because a direct C# caller search is empty.

## Removals and replacements

No standalone dead-code deletion or dependency removal was justified in this pass. Security fixes replace unsafe duplicated persistence/temp-file operations with `SafeFile`, and remove the obsolete mutex acquisition exception branch while consolidating lock acquisition. These are behavior-preserving/safety replacements, not claims that whole features were unused. No optional cosmetic refactor was applied.

| Candidate | Confidence/evidence | Decision |
|---|---|---|
| `Updater.PickAsset` wrapper (`src/Updater.cs:149`) | No production direct caller found; current selection uses `PickAssetInfo`; internal compatibility/test-facing surface may be used by out-of-tree checks | Retained; removal offers negligible benefit and is unrelated to security |
| Non-battery branch of `Program.ApplyHardwarePreferences` | In-tree call currently always supplies `batteryOnly: true`; earlier/general restoration intent is documented in its comments | Retained; consider a separate reviewed cleanup after boot-task/device validation |
| Public Acer Agent convenience APIs not routed by the current UI | Several capabilities use string-based OEM/bridge dispatch; the public DLL surface and external consumers are not exhaustively known | Retained; do not infer obsolete OEM functionality from local reference counts |
| WinForms partial files, `.resx`, custom controls and overlay variants | Project inclusion, events, conditionals and UI construction connect them indirectly | Retained |
| Root, `win-x64` and `experimental` compiled copies; release notes/previews | README/scripts/releases intentionally expose distribution/compatibility assets; compiled bytes do not receive source fixes automatically | Retained; owner should move publication to controlled release artifacts and reconcile source/binary provenance separately |
| `System.Management`, ServiceController, WebView2; React/build/lint dependencies | WMI/SCM/WebView2 and frontend build paths reference them; audit tools report no vulnerable packages | Retained |

Expected maintenance benefit comes from one tested elevated-file-write implementation, one mutex acquisition path, one bridge request validator and runnable CI checks. Device tables, thermal floors, Acer commands, frontend controls and release presentation are preserved. Speculative deletion would not provide comparable confidence.
