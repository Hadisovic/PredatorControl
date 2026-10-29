# PredatorControl compatibility implementation report

Date: 2026-10-29. Repository: Hadisovic/PredatorControl. Local checkout: `PredatorControl-revisions`. Branch: `feature/hardware-compatibility`. Changes are local and reviewable; nothing was pushed, merged or released.

## A. Baseline

Exact base commit: **`03addd4d8aa7e5fa76c615aaba830c0d06c31156`**, latest `revisions` when fetched. The supplied workspace contained source archives without Git history, so a separate checkout was created; the archive copies were not modified.

| Baseline command | Result |
|---|---|
| `dotnet restore src/PredatorControlApp.csproj` | Passed |
| `dotnet build src/PredatorControlApp.csproj -c Debug -p:SkipJelliBuild=true` | Passed; zero warnings/errors |
| `dotnet build src/PredatorControlApp.csproj -c Release -p:SkipJelliBuild=true` | Passed; zero warnings/errors |
| `dotnet run --project tests/Keyboard.Tests -c Release` | Passed; 54 assertions |
| `npm ci --no-audit --no-fund` in `jelli-ui` | Passed; lockfile unchanged |
| `npm run build` | Passed |
| `npm test` | Passed; 6 tests |
| `npm run lint` | Passed |

.NET and npm were initially absent from PATH. A local .NET 10.0.403 SDK and npm launcher were prepared under the parent workspace's `.tools` folder; no Acer driver or service was installed. Node 24 was already available. No baseline failures remained after tool setup.

The [audit](COMPATIBILITY_AUDIT.md) records the actual source findings: incomplete identity, brand-based feature assumptions, fixed power-mode lists, battery false positives, universal Nitro keyboard policy, multi-transport writes, unverified state/persistence, incomplete Agent replies, automatic startup repair and drift between standalone service scripts and application policy.

## B. Implementation

- **Identity:** independent, bounded read-only SMBIOS queries; normalized model plus product/SKU/board/BIOS evidence; strict Acer/product-vendor validation; contradictory identity cannot select firmware. Serial and local preference hashes are excluded from JSON and record printing.
- **Resolver:** compiled, restricted profiles with explicit backend/method/service bindings; more specific SKU/board/BIOS profiles take precedence; ties and contradictory positive/negative evidence deny writes. Capability states are Supported, Unsupported, Unknown and Unavailable.
- **Shared snapshot:** Classic, Jelli, tray, hotkeys, automatic power rules and Game Sync use the same modes/capabilities. Backend identity determines Jelli branding. Sensor availability is read-only evidence and does not confer write permission.
- **Authorization:** a common serialized gate validates feature support and parameters before dispatch. Low-level WMI, APGe and Agent setters require the selected authorized scope. Power/fans/CoolBoost no longer try competing encodings/transports. Existing signature/consent protections remain intact.
- **Results:** acknowledged writes without hardware readback are Unverified. Battery and keyboard confirmation requires matching readback. Failed commands do not report success. MUX requests remain pending restart verification.
- **Safety:** bounded fan inputs; safe Auto prerequisite for manual control; partial failure, sensor loss/staleness and orderly disposal recover temporary manual sessions. No registry-based charging enforcement or automatic startup service repair. Explicit service scripts delegate to shared application policy.
- **Settings:** legacy preferences/profile files remain. New hardware preference bindings isolate device/firmware configurations and archive replaced values. Loading does not overwrite unsupported saved choices. Lighting restoration still requires an explicit compatible preference.
- **Diagnostics:** serialized JSON with identity, profile, backend/capability evidence, service/interface/sensor availability and last operation status. Tray, Jelli and read-only `--diagnostics` export paths are available.

## C. Key files

| File(s) | Purpose |
|---|---|
| `src/HardwareCompatibility.cs` | Identity model, profiles, evidence resolver, capability snapshot, gate, preference/fan safety policies, diagnostics serializer |
| `src/DeviceDetector.cs` | Real SMBIOS identity provider |
| `src/WmiController.Compatibility.cs` | Read-only discovery, targeted service reevaluation, telemetry availability, low-level payload authorization and test injection |
| `src/WmiController.cs` | Selected firmware command paths, bounded/acknowledged results, fan recovery, battery readback, guarded lighting and misc controls |
| `src/KeyboardPolicy.cs`, `WmiController.Keyboard.cs`, `KeyboardFirmwareTransport.cs` | Profile-based keyboard behavior, atomic read-modify-write, readback and low-level scope protection |
| `src/CompatibilitySettings.cs`, `RegistryConfig.cs` | Local device/firmware preference binding and archival migration |
| `src/Form1.cs`, `Form1.Compatibility.cs`, `Form1.Keyboard.cs` | Classic/tray/startup/hotkey/Game Sync capability use, dynamic choices, watchdog and operation feedback |
| `src/GameSyncForm.cs` | Supported mode/effect choices while retaining unavailable saved preferences |
| `src/AcerAgentClient.cs`, `AcerAgentEndpoint.cs`, `AcerAgentProtocol.cs` | Trusted endpoint selection, bounded framing and explicit MUX mask |
| `src/OemServiceManager.cs`, `Program.cs`, service scripts | Service evidence/dependency policy, explicit repair/optimization and read-only diagnostic entry point |
| `src/TelemetryService.cs`, `SelfCheck.cs`, `ThemeManager.cs` | Plausibility/missing readings, revised assertions and authoritative family branding |
| `src/Jelli/*`, `jelli-ui/src/App.tsx`, bridge types/dashboard helpers | Authoritative identity/capability serialization, adaptive controls, dynamic zone preview and status display |
| `tests/Compatibility.Tests/*`, `tests/Keyboard.Tests/*`, Jelli dashboard tests | Dependency-free synthetic controller/keyboard and frontend tests |
| `scripts/Test-Compatibility.ps1`, README and compatibility docs | Repeatable checks, audit, evidence matrix and physical validation procedure |

## D. Device compatibility

The full [compatibility matrix](HARDWARE_COMPATIBILITY.md#compatibility-matrix) distinguishes existing implementation contracts from simulations and physical evidence.

| Scenario | Automated result |
|---|---|
| PH16-71 / PHN16-71 legacy Predator fixtures | Existing legacy control contract preserved; independent MUX detection |
| AN515-58 fixed red | APGe binary/timeout controls; no RGB writes |
| Nitro RGB SKU/board/BIOS, 4-zone and 2-zone fixtures | Explicit profile precedence; only declared zones/effects dispatched |
| Nitro V ANV fixture | Only its explicitly declared modes |
| Tachometer-only Nitro fixture | Telemetry allowed; manual writes denied |
| Battery service with no operational status | No charge-limit false positive |
| No-MUX / stopped-Agent fixtures | Switching unavailable; unrelated WMI controls retained |
| Changed BIOS / conflicting profile | Restricted profile not inherited; conflicting evidence fails closed |
| Unknown Acer / non-Acer / malformed identity | Conservative fallback; no speculative Acer firmware writes |

These are **automated simulations**, not newly verified laptop support. Synthetic RGB/ANV profiles are confined to tests. Other real Predator/Nitro configurations need operational evidence or a reviewed exact profile; no series-wide compatibility claim is made.

## E. Automated results

Final checks run on Windows using `scripts/Test-Compatibility.ps1`. The explicit test targets execute console assertions; these counts are assertion checks, not a claim of framework-discovered xUnit cases.

| Command | Result |
|---|---|
| `npm ci --no-audit --no-fund` | Passed; 109 packages, existing lockfile |
| `npm run build` | Passed; TypeScript and Vite |
| `npm run type-check` | Passed |
| `npm test` | Passed; **8 tests** |
| `npm run lint` | Passed |
| `dotnet restore src/PredatorControlApp.csproj` | Passed |
| `dotnet build src/PredatorControlApp.csproj -c Debug -p:SkipJelliBuild=true` | Passed; zero warnings/errors |
| `dotnet build src/PredatorControlApp.csproj -c Release -p:SkipJelliBuild=true` | Passed; zero warnings/errors |
| `dotnet test tests/Compatibility.Tests/Compatibility.Tests.csproj -c Release -p:SkipJelliBuild=true` | Passed; **85 compatibility assertions** |
| `dotnet test tests/Keyboard.Tests/Keyboard.Tests.csproj -c Release` | Passed; **24 keyboard assertions** |
| Full `dotnet build ... -c Debug` with frontend build enabled during implementation | Passed; zero warnings/errors |
| `git diff --check` | Passed after whitespace review |

The 54 baseline keyboard assertions included repeated brand-wide policy cases. Those obsolete policy expectations were replaced by conservative authorization cases; all 20 original read-modify-write, state, failure and startup invariants remain, plus four no-inference assertions. Broader profile coverage moved into the 85 compatibility checks.

Tests exposed an unguarded synchronous RGB effect path; it was fixed and the denial case now passes. An attempted parallel frontend build/MSBuild embedding run raced on Vite's replaced `dist`; the final script runs these dependent operations sequentially.

**Test-isolation incident:** an early fake-controller run reached the real Windows power-overlay API and changed the OS overlay from Balanced to Performance. Windows event history established the previous Balanced value; it was restored, and the fake path now explicitly skips this API. Subsequent complete runs retained Balanced. No Acer EC/WMI/Agent/APGe firmware writes, GPU switch, reboot or driver/service installation was performed on physical hardware. This incident is not counted as hardware validation.

## F. Physical verification

**Not Run — Hardware Required** for every physical validation stage and every Acer firmware feature. No full application dashboard was launched to claim runtime or visual hardware verification. No GPU switch, charge-limit enforcement, fan behavior, lighting behavior or thermal/power behavior was physically certified by these tests.

The opt-in staged [procedure](HARDWARE_COMPATIBILITY.md#opt-in-physical-validation) requires an identified device, informed operator approval, initial-state recording, readback, safe restoration and separate approval/recovery plans for higher-risk changes.

## G. Remaining limitations

- There is no authoritative exhaustive model/SKU/board/BIOS database in the repository. Legacy PH16-71/PHN16-71 encodings are preserved, not newly certified. Other real devices need exact evidence; real Nitro RGB/ANV command profiles were not invented from synthetic fixtures.
- BatteryControl schema differences and Acer Agent protocol variants must pass the installed interface/status checks; unsupported shapes fail closed. WMI power/fan/RGB/misc acknowledgement cannot prove the resulting physical state.
- The existing RGB backend handles declared contiguous zones up to four and existing effect encodings. Per-key or different firmware protocols require a reviewed implementation.
- Auto recovery is best-effort acknowledged behavior, not proof of physical fan state. Firmware hangs, forced process termination, machine crashes and power loss cannot be guaranteed recoverable by managed code.
- Targeted service changes update authorization; a restart can be required to construct a newly available Classic section that was absent at initial UI construction. Jelli reflects the current backend snapshot and available controls.
- Visual layout and memory/idle-CPU claims were reviewed structurally, not rebenchmarked or validated through a live dashboard session. Existing companion/layout/style code was retained.
- No new CI workflow was added. The checked-in Windows test script is the reproducible local entry point.

## H. Regression review

Known legacy Predator payloads remain in restricted profiles; competing speculative paths were removed. Unknown configurations lose guessed controls rather than inheriting another family's commands. Both UIs, themes, companion, dashboard layouts, overlays, game detection, update security and IPC visibility actions remain. Hardware settings/profile files are preserved, unsupported preferences are not silently reset, and versioned bindings prevent blindly replaying a different device's new settings. Expensive identity discovery occurs once before UI construction; telemetry reuses its snapshot, and service reevaluation avoids repeat SMBIOS scans. Physical performance/compatibility still requires the opt-in procedure.
