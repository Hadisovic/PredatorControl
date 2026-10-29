# Predator Control 1.4.0

Feature release; assembly/file version 1.4.0.0. Built for Windows x64.

## Dashboard

Jelli remains the desktop companion. Its expanded dashboard now has Overview, Cooling, Keyboard, Games, and Settings tabs; responsive cards; Nitro/Predator device identification; teal, crimson, and OLED themes; and inline command feedback. Unavailable measurements are labelled explicitly. Compact, summary, quick-menu and gaming-overlay interactions remain available.

## Keyboard correction

- First launch preserves the current keyboard firmware state. Lighting is restored only after an explicit compatible preference has been saved in this version.
- The reported Nitro AN515-58 configuration defaults conservatively to an on/off keyboard. It has no brightness slider. The 30-second inactivity timeout is independent of backlight power.
- If your AN515-58 SKU has an RGB keyboard, select **Keyboard → Keyboard compatibility → RGB keyboard** and restart Predator Control. Selecting a type does not send a hardware command.
- Unknown keyboards do not receive RGB commands and are not labelled as red backlit keyboards. RGB-capable configurations retain their brightness/effects/zones. Game-profile RGB controls are hidden for non-RGB configurations.
- A firmware operation requires a registered OEM hotkey number and a readable APGeAction/AcerGenericMethod configuration. The application preserves unrelated configuration bits, uses one selected transport, and requires matching readback before saving a setting. It no longer guesses the Predator hotkey number or broadcasts several possible lighting encodings.
- Missing OEM registration/readback is shown as unavailable or unverified. This is intentionally not treated as success. Matching readback validates the interface response, not physical illumination.
- Legacy RGB_Mode, Brightness and Backlight30s keys are preserved for rollback but are not automatically migrated to binary backlight power. Choose the two keyboard settings explicitly after upgrading. An existing Off firmware state is preserved until you choose On.

## Validation

- Release self-contained publish and full frontend/C# build: passed.
- 25 isolated keyboard checks: passed, including missing preferences, legacy Off settings, rejected writes, missing readback, and all power/timeout combinations. These tests access no hardware.
- Six frontend tests and ESLint: passed.
- Simulated browser checks: Nitro binary, RGB, unknown keyboard, confirmed/rejected commands, and 390px/760px layouts.
- Development dependencies updated to Vite 8.3.5 and the patched source-map-js dependency. npm audit reports zero vulnerabilities.

Physical AN515-58/BIOS validation, keyboard wake after 30 seconds, real service failure recovery and Windows DPI testing remain to be performed on target hardware. No firmware commands, OEM service changes, or startup task changes were executed during this implementation. Other security findings in CODE_REVIEW.md remain outside this release's scope.

## Run and rollback

Use the new executable in `releases/1.4.0/win-x64/PredatorControlApp.exe`, or extract the release ZIP into a new directory. The old root/experimental executables are earlier builds and were not replaced. A .NET runtime is included; the installed WebView2 Runtime is still required for Jelli.

Close any older running instance before launching 1.4.0 so the single-instance mechanism does not redirect you back to the old UI. Source backup: `C:\Users\youse\Desktop\ACERCONTROL\Source-before-1.4.0.zip`. Existing user preferences were not modified during development.

To rerun automated checks:

```powershell
dotnet run --project tests/Keyboard.Tests/Keyboard.Tests.csproj
dotnet build src/PredatorControlApp.csproj -c Release
cd jelli-ui
npm test
npm run lint
npm audit
```

For UI-only testing, run Vite bound to localhost and open `/tests/preview.html`. Query options `?keyboard=rgb`, `?keyboard=unknown`, and `?keyboard=binary&fail=1` exercise simulated states. This fixture is excluded from the production frontend build.
