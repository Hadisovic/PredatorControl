# Compatibility audit

Baseline: `03addd4d8aa7e5fa76c615aaba830c0d06c31156`, fetched from `revisions` on 2026-10-29. Work branch: `feature/hardware-compatibility`. The original archive folders were preserved.

The baseline restored and built in Debug/Release with zero warnings. Its keyboard runner passed 54 checks; Jelli build, 6 tests and lint passed. No application executable, driver installer or firmware test was launched for the baseline.

| Finding in baseline | Resolution |
|---|---|
| Identity defaulted to Acer; broad substrings selected gaming families | Independent SMBIOS reads, strict manufacturer/product vendor checks, normalized model tokens, conflicting identities fail closed |
| Family/refresh rate implied CoolBoost, overdrive, boot sound, USB charging or MUX | Reviewed profiles and independent operational evidence; no refresh-rate or NVIDIA-brand inference |
| Nitro keyboards were universally binary; saved RGB preference could affect detection elsewhere | Exact conservative AN515-58 policy plus operational APGe readback; explicit SKU/board/BIOS profiles may establish RGB; preferences never authorize writes |
| Fixed power lists were repeated in Classic, tray, cycling, Game Sync, automatic profiles and Jelli | Shared typed `PowerMode` snapshot with AC/battery restrictions and stable firmware values |
| Power/fans/CoolBoost dispatched multiple encodings and transports without waiting | One selected backend; all physical write transports use a shared gate; no retry of rejected/ambiguous firmware writes |
| Readable RPM was confused with fan-control availability | Sensor mappings and fan-write capabilities are independent; manual control requires an Auto recovery implementation |
| Curves reused stale sensor history and marked failed writes applied | Sensor-loss/staleness recovery, bounded duty cycles, raw-temperature ramp-up, only acknowledged targets advance applied state |
| Battery support accepted chassis, services or registry remnants | Requires supported BatteryControl status response and setter; matching readback confirms state; no registry enforcement fallback |
| Current MUX mode was expanded to mask 7 | Only an explicit valid supported-mode mask is accepted; no direct WMI MUX fallback |
| OEM replies assumed one read; malformed result substring could imply success | Bounded fragmented JSON/framed replies, opcode checks and structured result parsing; one endpoint per dispatched request |
| OEM endpoint identity was unchecked | Trusted Acer service binary plus service process ownership for TCP/pipe selection |
| Startup automatically repaired services | Removed. Explicit repair preserves existing consent and Authenticode checks |
| Standalone optimization could disable Nitro transport dependencies | Scripts delegate to the application's shared service policy; known hardware services are preserved |
| Service-install scripts registered adjacent executables and fabricated battery enforcement | Scripts delegate to explicit application repair; no battery registry writes or ACTIVE claims |
| Saved preferences could be interpreted as actual hardware state or overwritten on unsupported devices | Loading suppresses persistence; versioned device/firmware bindings archive prior values; battery state comes from readback |
| Diagnostics concatenated unescaped JSON and lacked reasons | JSON serializer, capability/backend/service/sensor evidence and last operation result; serials and local identifiers excluded |
| Jelli inferred branding from model strings | Backend identity and chassis family are authoritative; actual supported zone count/effects drive controls |

Reviewed paths include WmiController and its keyboard adapter, Form1 startup/restoration, tray, IPC-triggered cycling, hotkeys, Game Sync and its editor, OEM service management, TelemetryService, SelfCheck, AcerAgentClient, RegistryConfig, Jelli's C# bridge and React state/control components.

The audit found no authoritative, exhaustive model/SKU/board/BIOS compatibility database in this repository. Consequently this change preserves the existing PH16-71/PHN16-71 implementation contracts as legacy profiles and the conservative AN515-58 keyboard policy, while other configurations need operational evidence or a reviewed profile. Existing source encodings are not new physical validation. No model series is declared universally compatible.
