# Verification results

Dates: 2026-11-02 to 2026-11-03 (Asia/Amman). Baseline source: `3468846`; target branch: `revisions`. Raw command logs and controlled baseline/publish fixtures are retained locally outside the repository. No raw private scanner inventory or production diagnostic dump is committed.

## Environment and execution boundaries

- Windows x64; .NET SDK `10.0.400`, Node `v24.22.0`, npm `11.19.1`, Python `3.12.10`.
- The installed shared Desktop runtime was `10.0.11`; ordinary framework-dependent test runs used the available shared runtime. All three .NET suites were additionally built/run self-contained with `RuntimeFrameworkVersion=10.0.14`. The security host's generated runtime configuration lists both Microsoft.NETCore.App and Microsoft.WindowsDesktop.App `10.0.14` under `includedFrameworks`.
- Tests used temporary files, synthetic device/service outcomes, fake streams, local test certificates, random IPC/mutex names, readonly trust checks and a harmless batch-variable prefix. No hardware writes, live application pipe/mutex manipulation, host service changes, task registration, real updater replacement, elevated exploit or private-key installation was performed.
- Counts below are unique checks, not the sum of repeated runs: **268 passed** = 207 compatibility + 24 keyboard + 24 security + 10 frontend + 3 scanner. The baseline had 252 checks; 16 security checks were added.

## Command results

Commands are relative to the repository unless a different directory is specified. `npm` is invoked as `npm.cmd` in the PowerShell command environment where needed.

| Check | Exact command / invocation | Result |
|---|---|---|
| Baseline .NET build | `dotnet build src/PredatorControlApp.csproj -c Release -p:SkipJelliBuild=true` | PASS; zero ordinary-build warnings/errors |
| Baseline .NET tests | `dotnet run --project tests/Security.Tests -c Release -p:SkipJelliBuild=true`; same command with `tests/Compatibility.Tests`; `dotnet run --project tests/Keyboard.Tests -c Release` | PASS; 8, 207, 24 respectively |
| Baseline frontend (in `jelli-ui`) | `npm.cmd test`; `npm.cmd run build`; `npm.cmd run lint` | PASS; 10 tests, TypeScript/Vite build, ESLint |
| Baseline scanner tests | `python -m unittest discover -s tests -p test_secret_scan.py` | PASS; 3 tests |
| Final ordinary build/test runner | `./scripts/Test-Compatibility.ps1 -SkipInstall` | PASS; Debug and Release builds, frontend production build/type-check/lint/tests, .NET compatibility/keyboard/security executables and Python tests |
| Final .NET 10.0.14 runs | `dotnet run --project tests/Security.Tests -c Release -r win-x64 --self-contained true -p:RuntimeFrameworkVersion=10.0.14 -p:SkipJelliBuild=true`; repeat with `tests/Compatibility.Tests` and `tests/Keyboard.Tests` | PASS; 24, 207, 24 respectively. Separate from shared-runtime runs; no doubled count |
| Native/security tests | `dotnet run --project tests/Security.Tests -c Release -p:SkipJelliBuild=true` | PASS; 24 checks, including actual Windows temporary filesystem/pipe behavior and fake network/parser inputs |
| NuGet inventory/advisories | `dotnet list src/PredatorControlApp.csproj package --vulnerable --include-transitive` | PASS; no vulnerable packages reported by configured NuGet source |
| NuGet CI audit gate | `dotnet restore src/PredatorControlApp.csproj --force-evaluate -p:NuGetAudit=true -p:NuGetAuditMode=all '-p:WarningsAsErrors=NU1901%3BNU1902%3BNU1903%3BNU1904'` | PASS; restore succeeds and vulnerability warnings would be errors; no warning suppression |
| npm advisories (in `jelli-ui`) | `npm.cmd audit --audit-level=low --json` | PASS; zero vulnerabilities at every severity at query time |
| Private-member analysis | `dotnet format style src/PredatorControlApp.csproj --verify-no-changes --diagnostics IDE0051 IDE0052 --severity info --no-restore` | PASS execution, no reported findings; this is not comprehensive proof of liveness |
| Full .NET analyzer build | `dotnet build src/PredatorControlApp.csproj -c Release -t:Rebuild -p:SkipJelliBuild=true -p:RunAnalyzers=true -p:AnalysisMode=All` | PASS execution, **983 warnings / 0 errors**. Baseline with identical options: **1,084 warnings / 0 errors**. This is not a clean all-rules result |
| Source + reachable history scan | `python tools/secret_scan.py --path . --history --output <private-log-directory>/final-secret-scan.json` | PASS; zero secret findings; two binary/large objects skipped. Final inventory count is not a vulnerability count |
| Standalone packaging | `dotnet publish src/PredatorControlApp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:SkipJelliBuild=true -o <private-log-directory>/publish-standalone` | PASS; candidate built, not launched, committed or distributed |
| Framework-dependent packaging | Same publish command with `--self-contained false` and `-o <private-log-directory>/publish-framework` | PASS; candidate built, not launched, committed or distributed |
| Diff hygiene | `git diff --cached --check`; targeted diff of hardware tables/protocol implementations, README, assets and tracked release artifacts | PASS; no whitespace errors or changes to those protected content areas. Async await policy changed in OEM transport files; command payloads did not |
| Workflow parse/pins | Python 3.12 / PyYAML 6.0.3 `safe_load` of `.github/workflows/security-checks.yml`, read-only-permission assertion and full-SHA regex on each `uses` | PASS. An initial optional Node YAML parse attempt failed because `js-yaml` was not installed; the available Python parser was used without adding a project dependency |
| Action ref verification | `git ls-remote https://github.com/actions/checkout.git refs/tags/v6 refs/tags/v6.0.0`; corresponding `setup-node` v6, `setup-dotnet` v5, `setup-python` v6 tag queries | PASS; workflow uses returned immutable 40-character commits |
| Gitleaks / TruffleHog / CodeQL | Tool availability checked; no such executable found | NOT RUN; custom scanner and .NET analyzers are the tools actually used |
| Hosted GitHub Actions run | New workflow is local source configuration | NOT RUN; GitHub blocked workflow publication and the owner chose to keep it local |
| Initial workflow publication | `git push origin HEAD:refs/heads/revisions` | BLOCKED by GitHub: saved OAuth token has `repo`, `read:org`, `gist` but lacks `workflow` scope. The owner requested a source/report push with the workflow retained locally |

The all-rules analyzer output includes broad existing exception/disposal/naming/context/P/Invoke-marshalling diagnostics. The final log has no CA5xxx diagnostics after the System32 import and HTTP revocation changes; that does **not** mean every security-relevant analyzer warning is resolved. No `NoWarn` or blanket suppression was added. Defaults still build without warnings; the optional all-rules sweep deliberately surfaces additional debt.

## Negative regression control

A separate detached Git worktree at `3468846` received only the new test source. The old `GetTrustedAsync` reflection invocation kept its original two-argument signature. The failure-cache test was omitted in that control because the original implementation would query real device APIs instead of returning the seeded null cache. No production source in that baseline worktree was patched.

Command: `dotnet run --project <baseline-worktree>/tests/Security.Tests -c Release -p:SkipJelliBuild=true`.

**Expected FAIL:** exit 1; 9 passed, 14 failed. The actual Game Sync hardlink fixture modifies the harmless linked target with the original implementation; the delayed reply parser fixture also fails under a non-pumping synchronization context. Several other failures report absent new validation methods: those demonstrate missing guards, not 14 independently reproduced exploits. The original implementation already rejected the synthetic null-DACL fixture; the explicit new guard is defense in depth, not a newly confirmed vulnerability.

During implementation, expanded tests also caught an over-restrictive pipe ACL and an invalid assumption about directory sharing preventing reparse changes. Both were corrected and are covered by the final passing real-OS checks. These development failures are not represented as passing baseline behavior.

## Checks not executed and release blockers

| Scenario | Status | Why / next verification |
|---|---|---|
| Real physical Acer fan, thermal, power, keyboard, MUX and battery writes | NOT RUN | No automatic physical writes were authorized; validate on the exact supported model/BIOS/service configuration |
| Genuine signed-update success, tampered signed sample, revocation, cert renewal/key rotation, downgrade executable and rollback during interruption | BLOCKED for a release-quality claim | No release signing infrastructure/sample chain or disposable elevated update VM was provisioned; tests check unsigned rejection, key comparison and parser/stream logic only |
| Mixed-integrity and different-account/session Windows pipe clients | BLOCKED for OS integration | Separate provisioned identities/tokens and an elevated test server are needed. Local-token integration and policy matrix passed |
| Actual startup-task registration, SYSTEM task versus GUI, service repair/optimization, malicious catalog membership, service executable replacement | NOT RUN | Would mutate the host; use a disposable Windows VM |
| Real WebView2 rendering/navigation/cache/UDF ACL/sandbox interaction and fallback | NOT RUN | Parser, configuration and frontend checks do not exercise Chromium. New protected profile needs VM/UI validation before release |
| Actual powercfg fallback timing, report browser read ACL and cleanup | NOT RUN | Mock/cache checks do not establish real device/subprocess behavior |
| Live NVML/ETW/display initialization and unsupported 32-bit execution | NOT RUN | Native structure/source review and Windows filesystem/pipe tests are narrower than these integrations |
| Other filesystems, UNC paths/cloud placeholders and all Windows 10 builds | NOT RUN / unsupported-path refusal | New elevated settings/output operations intentionally require ordinary local non-reparse paths; validate deployment-specific compatibility |
| Binary-wide security/decompilation analysis | NOT RUN | Inventory/version metadata and source audit do not establish every embedded executable's implementation |

## Final interpretation

One reproduced temporary-file redirection vulnerability is fixed, with additional trust-boundary and reliability hardening documented separately. No end-to-end privileged exploit was performed. New source builds and ordinary automated checks pass; old distribution artifacts remain unchanged. Highest remaining assessed risk is High conditional distribution risk until the owner builds, signs, validates and publishes a fixed release. Physical/VM checks above remain explicit release requirements, not inferred passes.
