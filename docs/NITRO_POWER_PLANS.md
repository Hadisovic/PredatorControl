# Acer Nitro Performance Profiles (NitroSense 3.01.3056 Reverse Engineering & Implementation)

Updated 2026-10-29 following binary and configuration analysis of `NitroSense 3.01.3056` (`NitroSense.exe`, `TsDotNetLib.dll`, `PSSvc.exe`, `PSAdminAgent.exe`, `PSCreateDefaultProfile.exe`, `SdkWrapperLibrary.dll`, and `Plugs\*\Feature.ini` / `HW_Support.ini` / `NitroSense.ini`).

## 1. Two Native Nitro Performance-Profile Architectures in NitroSense 3.01.3056

NitroSense 3.01.3056 selects its performance-mode UI and backend in `NitroSense.MainWindow.setMachineType()` based on `Plugs\<Model>\Feature.ini` (`[Check machine type] MachineType`):

### A. `MachineType = 0` & `MachineType = 1` (`Freed` & `Octavia`, including local `Nitro AN515-54` / `Octavia_CFS`)
- **UI Page**: `Nitro_MonitoringPage` / `Nitro_MonitoringPage_Acermode` (`[Default Setting] Acer power mode`)
- **Backend (`windows-power-plan`)**: Native Windows Power Plans via `powrprof.dll` (`PowerEnumerate`, `PowerGetActiveScheme`, `PowerSetActiveScheme`).
- **Supported Modes**:
  | Application value | NitroSense enum (`PowerPlanMode`) | Label | Windows scheme GUID | Power source |
  |---|---|---|---|---|
  | `0` | `PowerSaver = 0` | `Power saver` | `a1841308-3541-4fab-bc81-f71556f20b4a` | AC + Battery |
  | `1` | `Balance = 1` | `Balanced` | `381b4222-f694-41f0-9685-ff5bb260df2e` | AC + Battery |
  | `2` | `AcerBalance = 2` | `Balanced (Acer Optimized)` | `HKLM\SOFTWARE\OEM\NitroSense\AcerGUID` (only when registered and distinct from standard plans) | AC + Battery |
  | `4` | `HighPerformance = 3` | `High performance` | `8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c` | AC + Battery |
- **Fan & Power Limit Behavior**:
  - Changes OS CPU power policy via Windows power schemes (`PowerSetActiveScheme`); `PSAdminAgent.exe` (`PowerSchemeChange::PowerSchemeProcessMessage`) optionally applies CPU `[Processor power management]` subgroup settings when configured in `Plugs\<Model>\Feature.ini`.
  - Does **not** call `SetOperationMode`, `SetGamingProfile`, or `SetGamingMiscSetting(0x0B)`.
  - Does **not** modify EC fan mode (`Auto` / `Max` / `Custom` / `CoolBoost`).

### B. `MachineType = 2` (`Jimny` 2022 Nitro models: `AN515-46`, `AN515-47`, `AN515-58`, `AN517-42`, `AN517-43`, `AN517-55`)
- **UI Page**: `Nitro_MonitoringPage_2022` (`Operating Mode` selector)
- **Backend (`nitrosense3-opmode` / `nitrosense3-wmi-opmode`)**:
  - Named pipe `\\.\pipe\PredatorSense_service_namedpipe` (`PSSvc.exe`) command `30` (`0x1E`, `kSvcCmdSetOperationMode`) with 4-byte `uint32` payload `mode` (`0`, `1`, or `4`), or direct WMI `AcerGamingFunction.SetGamingMiscSetting` when `PSSvc` is absent and direct WMI readback is verified.
- **Supported Modes**:
  | Mode value (`OperationModeType`) | Internal enum name | NitroSense UI label (`MUI_Operating_Mode_*`) | `SetOperationMode` (`cmd 30`) | `SetGamingMiscSetting` (`cmd 63`) `gmInput` | GPU OC level (`gpu_set_oc_level`) | Power / BatteryBoost requirement |
  |---|---|---|---|---|---|---|
  | `0` | `Quiet` | `Quiet` | `1E 00 01 04 00 00 00 00 00 00 00` | `0x000B` (`(0 << 8) \| 0x0B`) | `0` (Normal, `0 MHz`) | AC required + BatteryBoost ready (>= 40%) |
  | `1` | `Default` | `Default` | `1E 00 01 04 00 00 00 01 00 00 00` | `0x010B` (`(1 << 8) \| 0x0B`) | `1` (Medium, `+50 MHz` core / `+100 MHz` mem) | AC or Battery |
  | `4` | `Extreme` | `Performance` | `1E 00 01 04 00 00 00 04 00 00 00` | `0x040B` (`(4 << 8) \| 0x0B`) | `2` (Turbo, `+100 MHz` core / `+200 MHz` mem) | AC required + BatteryBoost ready (>= 40%) |
  - Neither Predator `Turbo (5)` nor `Eco (6)` exists on Nitro. Predator `SetGamingProfile` is never called by NitroSense.
- **Exact `PSSvc.exe` Disassembly (`treadstone::TsClientCommandProcessor::set_operation_mode` at RVA `0x12C60`)**:
  1. Reads 4-byte `uint32 mode` from IPC packet payload.
  2. Encodes `ulong gmInput = ((ulong)mode << 8) | 0x0B` (`0x000B`, `0x010B`, `0x040B`).
  3. Invokes `root\WMI:AcerGamingFunction.SetGamingMiscSetting(gmInput, out uint gmOutput)` (`RVA 0x152E0`).
  4. Maps `mode` to GPU overclock level (`0 -> 0`, `1 -> 1`, `4 -> 2`) and invokes `gpu_set_oc_level(level)` (`RVA 0x13230`), which reads `[OC_GPU]` offsets from `Plugs\<Model>\NitroSense.ini` and applies NVAPI clock offsets when `GPU_OC = 1` in `HW_Support.ini`.
- **Active Profile Readback & Persistence**:
  - Hardware readback uses `AcerGamingFunction.GetGamingMiscSetting(0x0B)` (`PSSvc` command `64` / `0x40` `kSvcCmdWMIGetGamingMiscSetting` with `uint32` payload `0x0B`: `40 00 01 04 00 00 00 0B 00 00 00`), where `gmOutput & 0xFF` is status (`0` = OK) and `(gmOutput >> 8) & 0xFF` is the active mode (`0`, `1`, or `4`).
  - NitroSense also persists the user's selected mode in `HKLM\SOFTWARE\OEM\NitroSense\Overclock\CurrentOperationMode`.
- **AC / Battery & BatteryBoost (`GetGamingSysInfo(0x0002)`) Coordination**:
  - `PSAdminAgent.exe` (`HotKeyAction::check_battery_boost` at RVA `0x17020` and `HotKeyAction::reset_Operation_Mode` at RVA `0x17130`) queries `GetGamingSysInfo(0x0002)` (`PSSvc` command `13` / `0x0D`) and checks byte 5 (`(output >> 40) & 0xFF == 1`, battery >= 40% and AC online).
  - When on battery or when BatteryBoost is inactive (< 40% battery), `Quiet (0)` and `Performance (4)` are disabled in the UI and hardware switches to `Default (1)` without overwriting the saved AC preference.
- **Fan Control Coordination**:
  - Firmware adjusts EC thermal/acoustic targets and power limits via `SetGamingMiscSetting(0x0B)` when fans are in `Auto`, but `Nitro_MonitoringPage_2022.Operation_Mode_Check` never sends `SetGamingFanBehavior`, `SetGamingFanSpeed`, or `SetCoolBoost`.
  - PredatorControl preserves active `Manual` (`Custom`) or `Max` fan sessions when switching performance profiles and only triggers opt-in `Auto-CoolBoost` when fans are in `Auto` mode (`0x01`).

## 2. Verification Status

| Profile ID | Target Hardware | Performance Backend | Verification Status |
|---|---|---|---|
| `nitro-an515-54-octavia` | Acer Nitro `AN515-54` (`Octavia_CFS`, `MachineType = 1`) | `windows-power-plan` (`0` Power saver, `1` Balanced, `2` Balanced (Acer Optimized), `4` High performance) | **Read-only discovery verified on local AN515-54 hardware** (`Balanced` active scheme `381b4222-f694-41f0-9685-ff5bb260df2e`). Physical plan-switch write tests require explicit user approval. |
| `conservative-an515-58` | Acer Nitro `AN515-58` (`Jimny_ADH`, `MachineType = 2`) | `nitrosense3-opmode` / `nitrosense3-wmi-opmode` (`0` Quiet, `1` Default, `4` Performance) | **Protocol recovered from signed NitroSense 3.01.3056 binaries & tested in synthetic test suite**. Requires physical `AN515-58` hardware verification. |
| `nitro-an515-46-jimny`, `nitro-an515-47-jimny`, `nitro-an517-42-jimny`, `nitro-an517-43-jimny`, `nitro-an517-55-jimny` | Acer Nitro `AN515-46`, `AN515-47`, `AN517-42`, `AN517-43`, `AN517-55` (`MachineType = 2`) | `nitrosense3-opmode` / `nitrosense3-wmi-opmode` (`0` Quiet, `1` Default, `4` Performance) | **Protocol recovered from signed NitroSense 3.01.3056 binaries & tested in synthetic test suite**. Requires physical hardware verification on each model. |
| `legacy-ph16-71`, `legacy-phn16-71` | Predator `PH16-71`, `PHN16-71` | `acer-wmi-profile` (`SetGamingProfile`: `0, 1, 4, 5, 6`) | **Preserved without regression** (isolated from Nitro backends). |

## 3. Automated Test Coverage

- **194 compatibility assertions** in `tests/Compatibility.Tests` covering:
  - Profile enumeration across Nitro `MachineType = 1` (`windows-power-plan`), Nitro `MachineType = 2` (`nitrosense3-opmode` / `nitrosense3-wmi-opmode`), and Predator (`acer-wmi-profile`).
  - Golden packet encoding for `SetOperationMode` (`cmd 30`: `0`, `1`, `4`), `WMISetGamingMiscSetting` (`cmd 63`: `0x000B`, `0x010B`, `0x040B`), and `WMIGetGamingMiscSetting` (`cmd 64`: `0x0B`).
  - Selection and confirmation of supported modes via `PSSvc` named pipe and direct WMI fallback.
  - AC/battery and BatteryBoost (`< 40%`) restrictions.
  - Failed commands, readback mismatches, and timeouts/cancellations.
  - Reading the current hardware profile (`ReadPowerMode`, `ActivePowerMode`).
  - UI/backend synchronization (`NitroPowerProfileCoordinator.ProjectControls`).
  - Startup saved-profile restoration (`RestoreSavedPowerMode`: skipping redundant writes when already active, rejecting unsupported or AC-only modes on battery).
  - Strict Nitro and Predator backend isolation and manual-fan session preservation.
- **24 keyboard regression assertions** in `tests/Keyboard.Tests`.
- **9 frontend assertions** in `jelli-ui/tests`.
