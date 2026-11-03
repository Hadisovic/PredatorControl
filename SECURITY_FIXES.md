# Security and reliability remediations

Audit started 2026-11-02 and completed 2026-11-03 (Asia/Amman). Baseline: `3468846`, branch `revisions`. This document describes source changes; tracked/distributed executables are unchanged. See `SECURITY_AUDIT.md` for prerequisites, severity and remaining risks, and `VERIFICATION_RESULTS.md` for commands and limitations.

## A01 — Elevated file writes and redirection

**Root cause and impact.** Game Sync truncated a pathname in LocalAppData; RGB used a predictable `.tmp` pathname; diagnostics wrote selected/fixed desktop paths; crash logging appended in a user-writable directory. Redirection could make the elevated operation write to another object. A harmless hardlink fixture reproduces unintended target modification with the baseline Game Sync implementation. Actual elevation into a protected system target was not attempted. Exploitation depends on the attacker's ability to establish a relevant link/reparse arrangement under the applicable Windows policy.

**Fix.** `src/SafeFile.cs` opens each directory component relative to a handle with `OBJ_DONT_REPARSE` and checks reparse attributes. New files use create-new semantics, deny write/delete sharing, and are renamed through the held file handle into the verified directory handle. The final name is not resolved again through the original path. Existing hardlinks are replaced as directory entries, not truncated. Reads reject linked/reparse files and enforce a 1 MiB bound, including growth during reading. Game Sync, RGB persistence and diagnostics use this shared primitive. Failed renames delete only the temporary file identified by the held handle. Corrupt RGB backups are bounded, safe copies instead of unchecked moves. No data-format migration is introduced.

Crash logs instead use an admin-protected, per-user directory under `%ProgramData%\PredatorControl\logs`. Existing insecure directories/files are rejected. Logging is serialized, rotates after 1 MiB before the next append, and caps remembered exception keys at 128. The current exception entry can take the log beyond the rotation threshold; this is a bounded retention policy for ordinary exceptions, not a hard per-entry memory limit.

**Files.** `src/SafeFile.cs`, `GameSyncController.cs`, `RgbProfileManager.cs`, `Program.cs`, `Form1.Compatibility.cs`, `Form1.Tray.cs`.

**Verification.** Security checks cover the actual Game Sync save against a harmless hardlink, rejection of junction ancestors and oversized/linked reads, repeated Unicode/percent/exclamation-mark saves, failed-rename cleanup, and deterministic directory rename plus junction substitution between validation and save. The baseline hardlink regression fails; the final implementation passes. A directory-handle-only approach was rejected during testing because it did not block directory write access on this host.

**Compatibility/limits.** Local drive paths and ordinary files remain supported. UNC/device paths and reparse-backed settings/export folders are refused rather than followed with elevated privileges; choose an ordinary local folder. NTFS semantics were exercised on this Windows host. Other filesystems, cloud placeholders, low-integrity-to-high-integrity exploit chains and every Windows 10 build still need VM validation. No privileged system file was targeted.

## A02 — Update authenticity, version and resource limits

**Root cause and impact.** A hash from the same release source detected corruption but did not independently authenticate the executable. A compromised release source could supply matching malicious bytes and hashes. Header-only HTTP completion also left body reads without an operation-wide deadline or size cap. Application of `UpdateInfo` lacked an independent newer-version check.

**Fix.** Keep HTTPS, manual redirect checks, protected staging, hash verification, backup and rehash-before-swap. Additionally require Windows Authenticode trust for the protected installed executable and the staged executable, then compare signing public keys. This derives trust continuity from the installed publisher without inventing a certificate, key or online signature service. A renewed certificate using the same key is supported; a publisher-key change requires a verified manual install. Match the staged signed executable's file version to newer release metadata. Unsigned builds still discover updates, but automatic installation fails closed with a manual-install message.

Metadata is capped at 4 MiB, checksum text at 256 KiB and executables at 256 MiB. Explicit cancellation covers response bodies and hashing; the apply operation has a ten-minute deadline and metadata a twenty-second deadline. Every redirect is checked; URL credentials and nonstandard ports are rejected. Invalid asset names, malformed/non-object release entries, tag/version mismatches, downgrade metadata and batch-line/hash injection are refused. HTTP certificate revocation checking is enabled. Batch system-tool paths derive from `Environment.SystemDirectory`, not inherited `SystemRoot`. Failure to start the helper is reported rather than treated as success.

**Files.** `src/Updater.cs`; `OemServiceManager.VerifyWinTrust` is reused, with its unmanaged string allocation now released.

**Verification.** Tests reject unsigned executables, different signing keys (including identical certificate display names), malicious redirects, oversized/stalled streams, malformed metadata, version mismatch/downgrade input and invalid handoff paths/hashes. Key-comparison fixtures are self-signed local test certificates; they are never accepted by or installed into a production trust store. Redirect tests use an in-memory HTTP handler. The existing shell test executes only harmless variable assignments, never replacement or relaunch commands.

**Compatibility/limits.** There is no release signing key or signing workflow in this repository. No successful signed-update installation, certificate rotation, revocation, interruption/power-loss rollback or elevated replacement was executed. WinVerifyTrust's existing cached/offline revocation policy remains; HTTP TLS revocation checking does not substitute for fresh code-signing revocation evidence. The owner must establish signing, timestamping and secure key custody and validate signed samples in a disposable VM. Existing unsigned users require a manual transition. There is no archive extraction in this updater, so an archive traversal fix was not applicable.

## A03 — IPC authorization and exclusive startup

**Root cause and impact.** The same-user pipe ACL did not express a per-command privilege policy: text could reach mode cycling or exit without checking the client's token. The headless `--boot-limit` path also preceded the GUI mutex, allowing separate application processes to reach hardware code concurrently. A complete mixed-integrity attack was not executed.

**Fix.** Authenticate through the pipe's impersonation token, require the same Windows user and session, allow harmless `TOGGLE`/`SHOW`, and require enabled administrator membership for `CYCLE_MODE`/`MODE`/`EXIT`/`QUIT`. Unknown commands are rejected instead of implicitly showing the UI. Clients request identification-only impersonation; no shared secret is introduced. The protected DACL grants data, read-attributes and synchronization rights, excluding pipe-instance creation. First-instance creation prevents silently joining a squatted pipe. Retain the existing 256-character/five-second bounds and limit successful dispatch to four requests per second. Both GUI and headless battery startup acquire the same machine-wide mutex; a headless launch skips preference writes when another instance owns it.

The activation client's deadline covers both asynchronous connection and writing; it does not synchronously drain/flush to an untrusted pipe server. Invalid framed commands are rejected before opening the live pipe.

**Files.** `src/SingleInstanceIpc.cs`, `Form1.cs`, `Program.cs`.

**Verification.** A real, randomly named Windows pipe exercises the production ACL/rights and token authorization, without opening the live application pipe. The policy matrix covers elevated/unelevated, wrong-user/session, unknown and privileged command cases. A random mutex test checks exclusion/reacquisition. Existing oversized/stalled-input tests remain. Testing caught and corrected the need for `ReadAttributes` on legitimate pipe clients.

**Compatibility/limits.** Other-user/session and medium-client-to-elevated-server OS integration still require separately provisioned Windows accounts/tokens. The matrix is not represented as that integration test. Older external senders requesting generic pipe-write rights may be denied; the current application's activation client uses the narrowed rights. Name squatting or a same-user client repeatedly holding connections can still deny IPC availability; it does not grant primary hardware ownership.

## A04 — WebView trust and persisted preferences

**Root cause and impact.** A persistent elevated WebView profile lived in user-writable LocalAppData; cache/script poisoning across that boundary required investigation. Message checking accepted more URLs under the local origin than the two actual documents. Preference validation did not constrain all persisted fields and `Save` discarded the validated copy.

**Fix.** Use a fresh `v2` per-user profile below protected ProgramData, refusing unsafe ancestors and never copying old browser cache. Serve only embedded-origin resources and deny external resource requests, navigation, windows, downloads and permissions. Native messages and outbound state require the exact root/index document. The existing action set is retained behind a bounded, typed message parser. Native control dispatch remains the existing per-control allowlist with backend capability checks. Production DevTools are disabled; debug builds retain explicit opt-in. CSP forbids workers and form submissions. Clamp overlay size/position, validate enums/monitor string length, and persist the validated settings copy.

**Files.** `src/Jelli/JelliHostForm.cs`, `JelliProtocol.cs`, `JelliSettings.cs`, `jelli-ui/index.html`.

**Verification.** Tests reject foreign/lookalike origins, unserved same-origin documents, query variants, unknown/oversized actions, wrong types, missing values and invalid numeric channels/coordinates, while accepting representative current frontend commands. Preference bounds are checked. Frontend build/lint and all existing UI-logic tests pass.

**Compatibility/limits.** No real WebView2 navigation/rendering, UDF ACL or service-worker poisoning integration was run in an elevated VM. Browser sandbox access to the new UDF, UI fallback, display scaling and UI recovery need validation before release. Installed WebView2/.NET runtimes and owner-controlled build inputs remain trusted; this change does not authenticate every machine/runtime policy or environment override.

## A05 — OEM/native boundaries and deadlock prevention

**Fix and rationale.** Require protected ownership/ACLs/ancestors before caching Acer executable trust or accepting DriverStore candidates; retain signature, publisher, service PID and transport checks. Reject null DACLs explicitly. Destroy the marshaled WinVerifyTrust file-info string before freeing its structure, closing a per-verification unmanaged allocation leak. Assembly-wide System32 P/Invoke search restrictions cover Windows DLLs and NVML; the embedded WebView2 loader retains its separate protected, hashed and held-handle loading. Reject the hardcoded x64 ETW layout in a 32-bit process before invoking it. No struct offsets or Acer protocols are guessed or modified.

The OEM async transport/parser chain now avoids capturing the UI synchronization context, including high-level Acer Agent wrappers. Some callers synchronously wait for discovery/replies; context capture could otherwise deadlock them. `ConfigureAwait(false)` preserves ExecutionContext/AsyncLocal hardware authorization while allowing transport continuations to finish off the UI thread.

**Files.** `src/OemServiceManager.cs`, `SecureLaunch.cs`, `NativeSecurity.cs`, `EtwFpsMonitor.cs`, `AcerAgentClient.cs`, `AcerAgentProtocol.cs`, `NitroSense3Protocol.cs`.

**Verification/limits.** A non-pumping synchronization-context fixture with delayed in-memory streams checks both reply parsers. Existing synthetic hardware/service/transport tests pass. No memory-leak benchmark, actual OEM process launch, INF install, NVML initialization, ETW session, 32-bit execution or service ACL mutation was performed. INF preflight still does not independently bind an INF's digest to a catalog; installation relies on Windows `pnputil` enforcement and needs separate VM testing. Third-party OEM installations with writable paths may now be refused rather than trusted.

## A06 — Battery/report reliability and privacy

**Fix.** Serialize battery health probes and cache unsuccessful probes for one minute, preventing repeated fallback launches on each refresh. Jelli's synchronous state builder uses the existing background-populated cache instead of invoking WMI/powercfg on the UI thread. XML and HTML reports use protected per-user directories; only that user is granted read access for browser viewing. Wait for a successful powercfg exit, stop a timed-out child, bound XML input and prohibit DTD processing/resolution. Clean temporary XML in `finally`. No sensor values or thermal limits change.

**Files.** `src/BatteryHealthMonitor.cs`, `Jelli/Form1.Jelli.cs`, `SecureLaunch.cs`.

**Verification/limits.** A seeded failure-cache test performs no device query. Builds, UI tests and synthetic compatibility tests pass. Actual powercfg timeout/cleanup, browser opening with the per-user read ACL, missing-battery behavior and supported laptop battery readback remain physical/VM validation items. Historical reports from older builds are not deleted automatically.

## A07 — Regression and repository controls

Add the security executable and scanner tests to `scripts/Test-Compatibility.ps1`. A Windows Actions workflow runs builds, frontend lint/tests, all ordinary hardware-free suites, secret/history scanning, npm audit and a NuGet audit gate. Permissions are read-only, checkout does not retain credentials, actions are pinned to verified full commit IDs, and workflow execution is time-limited. No deploy, release-upload or signing credentials are added. Add the audit, disclosure policy, dead-code analysis and verification reports. Do not remove existing distribution files or alter the README.

The workflow's YAML and pins were checked locally. GitHub rejected workflow publication because the current OAuth token lacks `workflow` scope. The owner chose to keep the completed workflow local; it is not part of the published `revisions` commits. A local successful command is not a claim that hosted Actions ran. Broader analyzer warnings remain visible and are documented rather than suppressed.
