# Hardware compatibility

PredatorControl uses one `HardwareCapabilities` snapshot for Classic, Jelli, tray, power cycling, Game Sync and firmware authorization. A visible button is not an authorization boundary: the controller and low-level transports enforce the selected device/backend policy again.

## Identification and privacy

`DeviceDetector` independently reads ComputerSystem, ComputerSystemProduct, BIOS, BaseBoard and SystemEnclosure. Identity retains model/product/version/vendor/SKU, BIOS version/date, board manufacturer/product/version and chassis types. Missing, placeholder or contradictory identity fields cannot turn an unidentified machine into an Acer gaming laptop. Textual family names can select branding; firmware profiles require an exact normalized model.

The system serial is read locally. It participates only in a local preference-isolation hash, combined with the compatibility fingerprint. Serial changes never select firmware commands. Neither the raw serial, device UUID, compatibility fingerprint nor local preference hash is included in JSON diagnostics or the Jelli bridge. No cloud detection or runtime implementation download exists.

## Resolution

NitroSense 3.01.3056 recovery adds model-scoped APGe CoolBoost and corrects the Nitro keyboard registry source. See [protocol evidence](nitrosense-3.01.3056-protocol.md) and [the recovery report](nitrosense-recovery-report.md). These do not enable this older protocol on newer Nitro models. Physical setters remain untested; valid read-only state and exact device/backend authorization are required.

1. Normalize and validate identity.
2. Select an exact model profile, preferring additional SKU, board and BIOS restrictions. Conflicting equally specific profiles fail closed.
3. Inspect known WMI method metadata, validated APGe configuration, battery status and trusted OEM service capabilities without changing firmware.
4. Resolve each feature independently. Operational battery status and an explicit Agent MUX mask can establish those implementations independently of branding.
5. Publish an immutable capability snapshot. Service availability can be reevaluated without repeating SMBIOS discovery. Telemetry availability changes update read-only channel evidence.

Profiles live in `src/HardwareCompatibility.cs`; production never reads command definitions from saved keyboard choices, serial numbers, downloadable plugins or arbitrary JSON. PH16-71/PHN16-71 retain the repository's legacy WMI encodings. Nitro is not a separate application: an exact Nitro profile can select the same backend when evidence establishes the same protocol, or another reviewed implementation can be added for a different protocol.

Read-only temperature and tachometer discovery is independent of writable profiles. `GetGamingSysInfo(0)` advertises supported sensor IDs; the resolver exposes only its documented standard channels. A missing writable profile must not suppress this advertised telemetry. The AN515-54 / Octavia_CFS profile also preserves its locally validated sensor mapping. This legacy Nitro now selects installed Windows power plans, with active-GUID readback, rather than sending Predator thermal-profile values. See [Windows power-plan support](NITRO_POWER_PLANS.md).

Battery discovery uses the actual `GetBatteryHealthControlStatus` MOF contract: query 1, two reserved input bytes, a function-list byte, two return-code bytes and five status bytes. A nonzero return code cannot authorize the setter even if the returned function-list byte contains the health-mode bit.

| State | Meaning and UI |
|---|---|
| Supported | Profile/operational evidence permits this implementation. Interactive controls may be offered. This is distinct from a physical validation claim. |
| Unsupported | Non-Acer hardware or explicit negative profile evidence. No Acer firmware command. |
| Unknown | Insufficient or contradictory evidence. No writable control. |
| Unavailable | A required implementation/interface/service is missing or stopped. Disabled/hidden controls and diagnostic explanation. |

Every capability records its backend, evidence, required service, writable flag and ability to verify. A service being installed, running or trusted is distinct from its interface responding correctly. Discovery never installs, starts or disables a service. The explicit repair button/`--repair-services` retains existing signature and consent requirements. Standalone scripts route through that same path. Known hardware services are excluded from optimization on every family.

## Control and telemetry behavior

- Power modes carry firmware values and AC/battery restrictions. Classic, automatic rules, Game Sync editor, tray and Jelli consume the same list. Unsupported saved values remain saved and cannot dispatch. No Turbo/Eco inference is made for Nitro.
- WMI sensor indices belong to profiles. A tachometer never authorizes PWM writes. Missing or implausible measurements are N/A; a valid fan reading of zero remains zero. Windows CPU usage, NVML GPU measurements and battery telemetry remain available independently of Acer controls. Utilization is never synthesized from temperature/power.
- Manual fans require an Auto recovery path. Inputs are 10–100%; curves require fresh CPU and GPU temperatures. Partial command failure, missing sensors, stale telemetry, orderly disposal and shutdown request firmware Auto for temporary manual sessions. An acknowledgement cannot prove that firmware accepted Auto. Process kill, machine crash, firmware hang and power loss cannot be repaired by managed shutdown code; firmware thermal protections remain essential.
- AN515-58's conservative fixed-red profile requires a readable APGe configuration. Other Nitro models remain unknown unless positively identified. Exact RGB fixtures demonstrate SKU/board/BIOS precedence and different zone/effect sets; they do not add unverified real-machine profiles. The existing WMI RGB adapter supports 1–4 explicitly declared contiguous zones and effects using its existing encodings, not arbitrary per-key keyboards.
- Keyboard idle timeout is separate from RGB effects. Read-modify-write preserves unrelated configuration bits, and matching readback confirms keyboard changes. No first-launch RGB/binary defaults are written. Saved legacy profile choices cannot enable a transport.
- Charge-limit telemetry, capability, preference and actual state remain distinct. Only a valid BatteryControl support/status response plus setter authorizes control. A service or registry entry alone is insufficient. Acknowledgement without matching readback is Unverified; neither UI claims the limit is active.
- MUX choices require the explicit supported mask from a trusted Acer Agent endpoint. Reading a current mode does not establish all modes. Requests remain pending verification after an operator-approved restart; the existing UI confirmation is preserved. Tests never switch a real GPU or reboot.
- Overdrive is independent of refresh rate. Windows-key suppression remains the existing Windows hook; speculative keyboard firmware lock packets were removed. Display refresh switching continues to validate Windows-enumerated display modes. Physical hotkeys can invoke only authorized power/fan operations.

Operation results distinguish `confirmed`, `failed` and `unverified`. WMI acknowledgement without state readback is Unverified. Failed power/fan/toggle operations do not advance their accepted preference. RGB selections remain desired lighting preferences; the interface reports the operation as unverified/failed and does not claim firmware readback. The OEM lighting INI is not rewritten after an unverified command.

## Settings migration

Legacy keys and RGB/Game Sync profile files are preserved. New hardware preference writes carry a versioned binding derived from device identity/board/firmware and a private serial hash. Before replacing a value from another binding, its previous value is archived under `CompatibilityPreferences`. Returning to that device/firmware can read its archived preference. Application/Jelli preferences remain independent. BIOS/board changes invalidate hardware preference bindings and require reconfirmation rather than copying commands blindly. Legacy unbound preferences can restore only through current capability authorization.

Power automatic rules now save firmware values rather than fragile menu indices. Old AC/battery index tables are used solely for migration, and unsupported legacy choices are not overwritten during loading. Startup repair was removed; RGB restoration still requires an explicitly configured compatible preference.

## Diagnostics

Use **Export Hardware Diagnostic Report** in the tray or **Export privacy-safe compatibility report** in Jelli Settings. The report includes application version, normalized identity, selected profile, capabilities, backend/evidence/reasons, OEM service status, known WMI interfaces, observed sensor availability and last operation result.

For explicitly selected physical-device read-only validation, a packaged executable can also run:

```powershell
.\PredatorControlApp.exe --diagnostics "$env:USERPROFILE\Desktop\PredatorControl-compatibility.json"
```

This command bypasses startup restoration, optimization, service repair and normal dashboard actions. Inspect the JSON before sharing it. It excludes raw serials, UUIDs, service binary paths and unrelated process/system information by design.

## Adding a verified profile

1. Obtain an opt-in read-only report for the exact model, SKU, board and BIOS. Identify the actual WMI/service protocol and parameter schema from reliable device-specific evidence.
2. Add a restricted `DeviceProfile` entry to the compiled catalog. Cite the evidence in its explanation and the compatibility matrix; use SKU/board/BIOS exceptions where appropriate.
3. Declare each feature/backend/method/service independently. Add sensor indices, exact power values/restrictions and keyboard zones/effects only when established. Use `UnsupportedFeatures` for reliable negative evidence; conflicting evidence must deny writes.
4. If a different firmware protocol is required, implement it behind the controller/gate and add low-level authorization and parameter validation. Never discover an encoding by trying several writes on the user's machine.
5. Add synthetic identity, positive/negative, missing-service, firmware-change, command-failure and no-write assertions. Simulation evidence alone cannot promote the profile to physically tested.
6. Follow the physical procedure below, recording readback limitations and recovery behavior. Record evidence status separately from control availability.

## Compatibility matrix

| Configuration | Implemented behavior / automated coverage | Evidence status |
|---|---|---|
| PH16-71 / PHN16-71 legacy Predator contracts | Existing power values, WMI fans, RGB, misc controls; per-method checks; MUX separately requires Agent mask | Existing repository implementation; automated simulation only in this work. Physical validation pending. |
| AN515-54 / Octavia_CFS, observed BIOS V1.33 | CPU/GPU temperatures and RPM; Auto/Max/Custom fans and monitored curves; installed Windows power plans with readback; validated APGe CoolBoost and binary keyboard configuration; no Predator Turbo/RGB/MUX inheritance | Read-only getters observed on 2026-10-29; protocol and power-plan details in the recovery reports. Physical setters not tested. |
| AN515-58 conservative fixed-red profile | APGe read-modify-write on/off/timeout; no RGB fan-out | Existing conservative policy plus operational read requirement; automated simulation only in this work |
| Nitro 5 RGB SKU/board/BIOS fixture | Exact precedence, explicit effects, 4-zone and 2-zone synthetic layouts | Automated simulation only; fixture profiles are **not** shipped as real hardware claims |
| Nitro V ANV fixture | Explicit fixture modes only | Automated simulation only; production uses conservative fallback without a reviewed exact profile |
| Nitro tachometer-only fixture | RPM readable; manual writes denied | Automated simulation only |
| Service installed, no battery status | Charge-limit control denied | Automated simulation only |
| Nitro without MUX / explicit negative profile | No switching choices/writes | Automated simulation only |
| Predator with unavailable Agent | WMI features remain; Agent-dependent MUX unavailable | Automated simulation only |
| Same model, changed BIOS | Restricted RGB exception does not match; hardware preference binding changes | Automated simulation only |
| Unknown Acer / malformed identity | Read-only/unknown fallback; firmware-advertised standard sensors remain readable; no speculative firmware writes | Automated simulation only |
| Non-Acer | Acer firmware writes unsupported | Unsupported; negative simulation coverage |
| Other model/firmware encodings and per-key RGB | No guessed writes | Experimental/unverified until documented evidence is added |

Evidence labels: **Physically tested**, **Verified through documented evidence**, **Automated simulation only**, **Experimental/unverified**, **Unsupported**. Read-only physical observations do not establish successful writes. AN515-54 evidence and sources are recorded in [the regression report](AN515-54_REGRESSION_FIX.md).

## Developer checks

Use .NET 10 SDK, Node and the existing npm lockfile:

```powershell
.\scripts\Test-Compatibility.ps1
```

The script installs frontend dependencies with `npm ci`, builds/checks Jelli, restores/builds C# Debug and Release, and explicitly runs both test projects. Their `dotnet test` targets execute the dependency-free assertion runners and propagate failures. The Windows compatibility runner injects `IHardwareTestIo`, fake identities/services/sensors/outcomes and AC status into the production controller. The keyboard runner is platform-independent. No test changes Windows identity registry fields, starts an application dashboard, installs services/drivers or touches firmware. The injected controller skips Windows power-overlay synchronization as well.

Do not run a frontend build concurrently with C# resource embedding: Vite replaces `dist` while MSBuild reads it.

## Opt-in physical validation

Physical write stages remain **Not Run — Operator Approval Required**. Read-only AN515-54 observations are recorded separately in the regression report. Select and record the exact physical test device locally and obtain informed operator approval before proceeding. Do not publish serials in the record.

1. **Read-only discovery:** use `--diagnostics`. Check manufacturer/model/SKU/board/BIOS, selected profile, services, method metadata and sensor plausibility. Compare Classic/Jelli visibility using a machine whose saved hardware preferences have been reviewed; normal dashboard launch may restore explicitly saved preferences, so it is not the read-only test entry point.
2. **Low-risk controls:** operator approves a specific supported control. Record initial state, apply one supported setting, read back if available, record Confirmed/Failed/Unverified, and restore the initial state safely. Unknown keyboards receive no trial writes.
3. **Thermal/power controls:** separately approve fan/performance testing and actively monitor temperatures/RPM. Verify AC/battery restrictions, loss-of-sensor recovery and orderly Auto recovery. No automatic stress tests, zero-fan commands or disabled thermal protection.
4. **Higher-risk controls:** separately approve battery limit, firmware startup options and MUX/reboot testing. Record original values and recovery steps first. Restore the original battery/firmware preference if safely supported; for MUX, document how to return to the original graphics mode and the required approved restart. Never reboot, reset the EC, change BIOS options or install drivers as part of automated validation.

Stop on unexpected behavior. Do not try an alternate undocumented encoding. Add the redacted report and exact outcome to the matrix rather than marking a mock pass as a physical pass.
