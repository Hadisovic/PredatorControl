# Nitro recovery report

Power-plan follow-up: actual Windows power-plan selection for the reviewed AN515-54 is now implemented, replacing the overlay-only limitation recorded during the initial recovery below. See [the power-plan implementation and validation report](NITRO_POWER_PLANS.md). The combined compatibility runner now passes 152 assertions; no physical plan changes were tested.

## Recovery baseline

Continued the existing `feature/hardware-compatibility` working tree at commit `03addd4d8aa7e5fa76c615aaba830c0d06c31156`. No reset, checkout replacement, source deletion, push or merge was performed. Recoverable tracked and untracked source plus binary Git diff was saved before edits in `test-builds/checkpoint-before-nitrosense-repair.zip` outside the repository.

HEAD is the last committed comparison baseline, not a newly physically validated good version. Earlier 1.3.5 history and original source archives remain available. Prior compatibility changes and the first AN515-54 correction were uncommitted together, so there is no distinct Ponytail commit to blame or revert. No Ponytail/simplification workflow was invoked for this repair.

## Regression matrix

| Feature | Committed comparison baseline | Previous compatibility changes / first fix | Cause and recovery |
|---|---|---|---|
| CPU/GPU temperatures | Generic known WMI indices | Initial catalog omitted AN515-54 and suppressed all mapped reads; first fix restored them | Exact board profile and independent advertised-sensor discovery retained; local getter observations available. |
| Fan RPM | Direct WMI | Same catalog regression; first fix restored | Native service strings and signed managed UI corroborate existing selectors/decoding. |
| Auto / Max / Manual | Existing packed fan behavior and speed | Initially denied; first fix restored bounded writes | Full packets checked against official client captures. No physical writes tested. |
| CoolBoost | Speculative multiple protocols; WMI fan encoding did not match NitroSense | Gate blocked it entirely | Recovered exact generic setter/readback. Native strings map these to APGeAction. Its getter validates on the real machine; selected direct backend avoids the PSSvc query that closed without a response. No write-time fallback. |
| Performance | Firmware/Windows behavior conflated | First fix Windows overlay backend | Local 2019 UI uses Windows plans, whereas 2022 mode enum is a different route. Explicit Windows labels retained; full OEM plan parity remains unimplemented. |
| Keyboard | Conservative fixed-red/APGe policy | Adapter looked under AcerAgentService; OEM hotkey was missed | Local binary reads NitroSense registry. Correct path is scoped to reviewed AN515-54; status checked, no guessed hotkey; confirmation still required. |
| Battery | Presence/service false positives | First implementation called wrong getter; first fix corrected actual array contract | No additional speculative fallback. Firmware error remains unavailable, not reported as successful. |
| Startup | Broad restoration and some automatic service actions | Central authorization/per-device preference preservation | Gates retained; Nitro discovery performs reads only. Normal launch may restore existing explicit preferences. Development validation uses diagnostics only. |
| UI/tray | Some optimistic saves and independent assumptions | Capabilities shared but acknowledgement could persist unverified verifiable toggles | Readback-required toggles now revert without saving if unconfirmed; tray state follows confirmation. Classic/Jelli use the same controller. |
| OEM services | Predator-oriented optimization risk | Safer allowlist, but PSSvc omitted from dependency inventory | PSSvc now explicitly inventoried/protected and pipe PID authenticated. No service changed during investigation. |

## Source review and targeted changes

Previous changes span README/docs, service batch/PowerShell scripts, device identification/settings, WmiController/keyboard, AcerAgentClient, Form1/GameSync, TelemetryService, Jelli bridge/frontend/theme and both assertion runners. Their working diff is preserved with the checkpoint and private audit output. This pass keeps identity/privacy checks, authorization, read-only discovery, bounded telemetry, preference preservation and frontend capability projections. It does not broadly refactor those files.

Changes in this pass: new NitroSense3Protocol encoder/reader and narrowly permitted PSSvc exchanges; service authentication/dependency additions in AcerAgentEndpoint/OemServiceManager; scoped keyboard registry selection and getter-status check; reviewed-profile CoolBoost binding/probe/refresh; single confirmed CoolBoost path in WmiController; confirmation-aware Classic/Jelli shared toggle handling and tray behavior; captured-vector and simulated failure tests. Proprietary sources and DLLs are not in the repository.

Existing Predator fan, power, RGB and miscellaneous encodings were not replaced with NitroSense scalar formats. Synthetic PH16-71/PHN16-71 tests remain in the suite. This does not prove every other Predator model is physically compatible; the pre-existing limited reviewed catalog remains a known limitation.

## Tests and limitations

- Before this repair: 105 compatibility assertions passed, despite the missing real-device and protocol coverage.
- After repair: 132 compatibility assertions, including nine independently captured official-client packets; 24 keyboard assertions; 8 frontend tests.
- `dotnet test tests/Compatibility.Tests/Compatibility.Tests.csproj -c Release -p:SkipJelliBuild=true`: passed.
- `dotnet test tests/Keyboard.Tests/Keyboard.Tests.csproj -c Release`: initially failed because the standalone runner could not reference a production-only protocol constant; corrected to an ordinary backend identifier, then passed.
- `dotnet build src/PredatorControlApp.csproj -c Debug -p:SkipJelliBuild=true`: passed with zero warnings/errors.
- `npm --prefix jelli-ui run build` and `npm --prefix jelli-ui test`: passed (TypeScript/Vite and eight tests).
- Release regular and self-contained single-file publishing is performed after these checks. Exact binaries and hashes accompany the testing package.
- An initial private reference-capture harness blocked on synchronous pipe write before the server read began; concurrent reading corrected the harness. This was confined to a randomly named test pipe, never the hardware endpoint.

Implemented/automatically tested: exact-profile fans, telemetry mapping, serialized CoolBoost with selected APGe/trusted-service backend and readback, keyboard configuration policy, conservative battery handling and shared capability gates. Physically observed read-only: sensors/Auto-mode, valid CoolBoost APGe state and keyboard configuration. Final discovery resolves binary keyboard power/timeout and CoolBoost; no setters were exercised. Automatic CoolBoost is opt-in when no saved preference exists, preventing newly detected Nitro support from enabling it merely because a power profile is restored. Physical setters remain unverified. Full native service control flow, newer Nitro operation-mode semantics, real RGB SKU profiles, missing Windows power-plan recreation and Care Center IPC are not implemented or claimed verified. PSSvc's zero-byte response to the recovered query remains an interoperability limitation; no services were restarted or modified to work around it.

## Physical testing procedure

Do not run normal UI startup as a read-only test; it can restore explicit preferences. Use `--diagnostics <path>` for discovery. No installer, service modification, stress test, EC access or reboot is needed for this stage.

Before a write, obtain the operator's explicit approval for that category (fans, CoolBoost, keyboard, power or battery separately). Confirm exact identity/profile and absence of concurrent NitroSense/PredatorControl writers. Record original state, apply one bounded reversible operation, verify independently, restore the original state when appropriate, and record confirmed/failed/unverified. Stop on unexpected behavior; never retry alternate undocumented commands. Physical write categories remain **Not Run — Explicit Approval Required**.

See [the protocol specification](nitrosense-3.01.3056-protocol.md) for binary hashes, sources, generation restrictions and precise request/response layouts.
