# AN515-54 regression correction — 2026-10-29

The first compatibility test build omitted the user's Nitro AN515-54 from its compiled profiles. Its fallback disabled writable features and also discarded the existing temperature/fan sensor map. Synthetic tests passed because they covered AN515-58 and invented fixtures, not this actual machine. Compilation and packaging checks did not catch the device regression.

A second implementation error called a nonexistent `GetBatteryHealthControl` method and treated status as a scalar. The real interface is `GetBatteryHealthControlStatus`, with array fields. The production parser now follows that contract and tests its actual field shapes.

## Device and read-only observations

- Acer Nitro AN515-54, baseboard Octavia_CFS, BIOS V1.33.
- Existing Gaming-WMI getter returned CPU/GPU temperatures and both fan RPMs. One sample was 72/49 °C and 4285/4225 RPM; these are observations, not fixed values or performance targets.
- `GetGamingFanBehavior(9)` reported CPU/GPU Auto mode.
- `BatteryControl.GetBatteryHealthControlStatus` returned code 1. Its accompanying function list/status cannot establish support when the return code is nonzero, so charge-limit writes stay denied.
- APGeAction exists, but the keyboard adapter has no configured, validated OEM keyboard hotkey. Its presence alone does not authorize keyboard writes.
- No firmware setters, service changes, driver installations, power changes or reboots were performed during troubleshooting.

## Changes

- Added the AN515-54 / Octavia_CFS profile. It uses the existing bounded Gaming-WMI Auto/Max/Custom fan encodings and documented two-fan sensor map, with the existing manual-fan recovery/watchdog protections.
- Enabled Windows efficiency/balanced/performance through the Windows power-overlay backend and readback. No Predator firmware power-profile values or Turbo mode are exposed on this model.
- Kept firmware-advertised, documented read-only sensor channels available even without a writable model profile. Readable RPM never grants manual fan permission.
- Corrected battery query names, input/output fields, return-code validation and status-array parsing. Existing independent battery authorization remains required.
- Preserved backend-generated power labels in Classic, Jelli and tray menus. Diagnostic exports now include actual read-only sensor samples.

## Verification

- Release compatibility runner: 105 assertions passed, including the exact AN515-54 board case, sensor mask, no-write discovery, Windows power isolation, denied Turbo and real battery response shapes.
- Keyboard regression runner: 24 assertions passed.
- Frontend tests: 8 passed.
- Debug build: succeeded, zero warnings/errors.
- Both Release single-file variants are republished under `2026-10-29-an51554-fix1`, with distinct filenames.
- Physical fan/power/battery/keyboard writes remain untested. These are offered for operator testing only where the resolver authorizes the implementation.

## Primary evidence

- [Acer AN515-43/AN515-54/AN517-51 manual, NitroSense pp. 33–34](https://global-download.acer.com/gdfiles/document/user%20manual/637024736673988138_user%20manual_acer_2.0_a_a.pdf): model-specific fan modes and Windows power-plan behavior.
- [Upstream Linux Acer WMI implementation](https://github.com/torvalds/linux/blob/master/drivers/platform/x86/acer-wmi.c): Gaming-WMI sensor IDs, advertised sensor mask, fan IDs and behavior encoding.
- [Linux kernel BatteryControl interface documentation](https://kernel.org/doc/html/next/wmi/devices/acer-wmi-battery.html): actual battery status method and array contract, also checked against this machine's WMI method metadata.
- [G-Helper Windows power backend](https://github.com/seerge/g-helper/blob/main/app/Mode/PowerNative.cs): signatures and overlay GUIDs, matching this application's existing Windows power synchronization API. This is a Windows power control, not evidence of an Acer firmware thermal mode.
