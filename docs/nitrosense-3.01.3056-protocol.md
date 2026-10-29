# NitroSense 3.01.3056 interoperability findings

Analysis date: 2026-10-29. These are interface facts, not a claim that every feature works on every Nitro.

## Artifacts and provenance

The supplied `NitroSense_V3.01.3056_MSFT_SIGNED_20260424` directory was inspected without running its installer. The MSI embedded CAB was read with the Windows Installer database API and expanded as data. The x64 APPX was unpacked as ZIP data. Original artifacts were not modified. ILSpy 11.2.0.10050 inspected the managed UI/client; PSSvc is native and does not contain managed metadata. Native UTF-16 interface strings were inspected; full native control-flow decompilation was not performed.

| Artifact | File version | Authenticode | SHA-256 |
|---|---|---|---|
| NitroSense.msi | package | Valid, Acer Incorporated | `9A2FF0C24ABF78B3367A20241E3651D0735130EC26C2719FD3290259D526075E` |
| NitroSense.exe, APPX Win32 | 3.1.3056.0 | Valid, Acer Incorporated | `F71A2EEBF07F11E91337A0F5DE7FEEFDA54E96D8B007D3904ADF4F459856B2CD` |
| TsDotNetLib.dll, APPX Win32 | 3.1.3056.0 | Valid, Acer Incorporated | `C227A6B03E3120F83681F114F2B72EE3D615F11CC136688861B16DEDF1433669` |
| PSSvc.exe, MSI FILE_APP_SVC_EXE | 3.1.3056.0 | Valid, Acer Incorporated | `E6638FECC41730075D25587BFDDA7B4DDA2D170B64AC9DF9B3C03543999F3B4E` |
| PSCreateDefaultProfile.exe | 3.1.3056.0 | Valid, Acer Incorporated | `9F141AFBA9B8B3D07EBE1DEB4C95470CEFA0863BED59B3D51FD54E169ADE0D1B` |

Private inspection outputs and full proprietary decompilations are outside the repository under `.tools/nitrosense-analysis`. They must not be committed or distributed.

## Model and generation

`Startup` reads `C:\ProgramData\oem\NitroSense\Feature.ini`, including `MachineType.Type`. The package supplies separate model-specific `Plugs` folders. Their selection is installer/default-profile behavior, not proof that each listed machine implements every operation. The user's AN515-54 config states MachineType 1, Year 2019, CPU/GPU fans present, no system fan, PerKey 0. The AN515-58 external reference is a different configuration/generation. Four red default color entries alone do not prove RGB.

The local UI reads the keyboard hotkey number from `HKLM\SOFTWARE\OEM\NitroSense\BK_Hotkey_Number`. The installed AN515-54 value is 132. No hotkey number is guessed when the value is absent.

## Client/service framing

Confirmed in local `TsDotNetLib.IPCMethods.SendCommandByNamedPipe` and independently exercised through a private, randomly named test pipe. The capture never connects to the Acer service.

- Endpoint: local `PredatorSense_service_namedpipe` (native string uses lowercase; Windows pipe names are case-insensitive).
- Request: little-endian UInt16 command, UInt8 argument count, followed by each argument's little-endian Int32 byte length and payload. Strings are UTF-16LE plus NUL. UInt32 and UInt64 arguments must remain distinct.
- Single scalar reply: UInt8 count 1, Int32 payload length (4 or 8), then little-endian scalar. The implementation validates the header and reads fragments completely. It rejects truncated, oversized or cancelled replies.
- WMI results use the low status byte; zero is success. Higher bytes are operation-specific. Dispatch or pipe connection alone never proves success.
- Production authenticates the connected pipe server PID against the running, Authenticode-verified Acer PSSvc service. It does not trust the pipe name alone. Total exchange deadline: two seconds; no write retry/fan-out.

## Recovered operations

All setters below require explicit physical-test authorization before being exercised during development. Read-only observations do not grant cross-model write support.

| Operation / originating UI helper | IPC command and argument | Response / decoding | Local evidence and implementation status |
|---|---|---|---|
| Temperatures/RPM, WMIFunction.GetAcerGamingSystemInformation | 13, UInt32 `1 + (sensor << 8)` | UInt64; status bits 0–7, value bits 8–23 | Managed client and native `GetGamingSysInfo` strings. Existing direct-WMI reader retained. CPU temp 1, CPU RPM 2, GPU RPM 6, GPU temp 10. |
| Auto/Max/Custom, CommonFunction.set_all_fan_mode | 15, UInt64 `0x410009 / 0x820009 / 0xC30009` | UInt32, low status byte | Managed constants, captured packets; native `SetGamingFanBehavior` strings. Existing bounded direct-WMI route retained. |
| Fan percentage, set_single_custom_fan_speed | 16, UInt64 `fan + (percent << 8)`, CPU 1 / GPU 4 | UInt32, low status byte | Managed client and native `SetGamingFanSpeed` strings. Application retains 10–100% safety floor. Native UI helper sometimes returns true without checking the response; this bug is not reproduced. |
| CoolBoost, set_coolboost_state | 17, UInt64 `7 + (enabled << 16)` | UInt32, low status byte | Confirmed local UI/client; native generic APGeAction/SetFunction strings. Reviewed AN515-54 selects direct APGe when its getter and setter metadata validate; otherwise requires an operational trusted PSSvc. No write-time fallback. |
| CoolBoost, get_coolboost_state | 20, UInt32 `519` | UInt64; status bits 0–7, state bits 8–15 must be 0/1 | Confirmed local UI/client. Direct APGeAction.GetFunction query returned a valid state on this machine. Required readback; mismatch fails, missing readback is unverified and does not save the setting. |
| Keyboard configuration, get_backlight_* | 20, UInt32 `0x80001 + (hotkey << 8)` | UInt64; status low byte; brightness bits 32–39; idle seconds bits 40–47 | Local UI confirms correct registry source and APGe-shaped configuration. Existing APGe direct read/modify/write adapter retained, now reads the model-appropriate registry path and checks status. |
| Keyboard idle timeout, Backlightoff_Checkbox_* | 17, UInt64 `0x80002 + (hotkey << 8) + (brightness << 32) + (seconds << 40)` | UInt32 status | Local UI; 0/30 seconds. Existing adapter preserves other bits and verifies configuration readback. No trial writes performed. |
| Legacy scalar keyboard brightness | 27, UInt64 `(brightnessLevel - 1) * 25 << 16` | UInt32 status | Local Popup_Lighting. Distinct from Predator's 16-byte animation payload. Not enabled on the user's fixed-color profile merely because a method exists. |
| Zone RGB | 28, UInt64 `zoneBit + (R<<8) + (G<<16) + (B<<24)` | UInt32 status | Local managed Popup_Lighting. No new real RGB profile enabled without SKU/configuration verification; existing Predator transport unchanged. |
| Zone enable mask | 29, UInt64 `8 + (zoneFlags << 40)` | UInt32 status | Managed client/UI. Restricted to verified lighting implementations; not implemented as a generic Nitro fallback. |
| Newer operation modes | 30, operation enum | Model-dependent service operation | Enum Quiet 0, Default 1, Render 2, Edit 3, Extreme 4, Turbo 5. Newer 2022 UI exposes a subset. These values are NOT sent as SetGamingProfile payloads to the 2019 AN515-54. |
| 2019 power selection | Windows PowerSetActiveScheme | Windows API status plus active-GUID readback | Implemented for reviewed AN515-54: installed Power saver, Balanced, registered Acer Optimized and High performance plans. Discovery uses PowerEnumerate/PowerGetActiveScheme. No missing plans are created; see NITRO_POWER_PLANS.md. |
| LCD/profile settings | 9 / 10 | Feature-specific packed fields | Local client helpers and native Set/GetGamingProfile strings. Not enabled from refresh rate or generic Nitro branding. |
| Miscellaneous controls | 33 / 34 | Feature-specific packed fields | Local client/native Set/GetGamingMiscSetting strings. No new generic misc setters enabled. |
| Battery health | Separate BatteryControl class | Actual status-array contract, documented in hardware guide | NitroSense package does not establish Acer Care Center battery interoperability. Existing battery return-code check retained; this AN515-54 returned error 1. |

## Golden vectors and tests

The installed PSSvc executable exactly matches the analyzed MSI hash. Its recovered cmd-20 query closed the pipe with zero response bytes on this machine, despite a trusted running service. This is an observed interoperability failure, not an invented successful result. Direct APGe readback succeeded, so discovery selected `nitrosense3-apge`. A setter never retries through a different backend. Full native analysis of why PSSvc closes this query remains unresolved.

`tests/Compatibility.Tests/nitrosense3-golden-vectors.json` contains nine requests emitted by the signed TsDotNetLib DLL listed above into isolated local test pipes. The capture invokes only its serialization method. Production contains an original encoder, not the Acer library. Routine tests compare complete packets against these captured bytes, including command, argument count, size and byte order. Response tests cover fragmentation, truncation, invalid counts/lengths, status errors and cancellation. Fakes cover unavailable/untrusted PSSvc, exact profile gating, one setter plus readback and failure without retries.

## External research versus local proof

[nitrocli research](https://github.com/thebugitself/nitrocli/blob/main/docs/REVERSE_ENGINEERING.md) guided the inspection. Pipe framing, command IDs, fan payloads, sensor map, CoolBoost and keyboard configuration layout were independently checked against the local signed managed binaries. Native strings corroborate WMI names but do not replace native control-flow analysis. nitrocli's AN515-58 physical RGB/battery/performance results do not apply to this AN515-54. No EC access or firmware command guessing was introduced.
