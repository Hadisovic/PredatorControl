using System.Drawing;
using System.Text;
using System.Text.Json;
using PredatorControlApp;

int passed = 0;
void Check(bool condition, string scenario)
{
    if (!condition) throw new Exception(scenario);
    passed++;
    Console.WriteLine("PASS " + scenario);
}
DeviceIdentity Id(string model, string bios = "V1.02", string sku = "") => new()
{ Manufacturer = "Acer", Model = model, BiosVersion = bios, Sku = sku, BoardProduct = "TEST-BOARD", SerialNumber = "SECRET-SERIAL" };
HardwareDiscovery Discovery(string model) => new()
{
    Identity = Id(model), KeyboardConfigurationReadable = true,
    Methods = new HashSet<string> { "SetGamingProfile", "SetGamingFanBehavior", "SetGamingFanSpeed", "SetGamingKBBacklight", "SetGamingRgbKb", "SetGamingMiscSetting", "GetGamingSysInfo" },
    Services = new Dictionary<string, ServiceEvidence> { ["AASSvc"] = new("AASSvc", true, true, true, true), ["ASMSvc"] = new("ASMSvc", true, true, true) }
};
var predator = Discovery("Predator PH16-71");
var neo = Discovery("Predator Helios Neo PHN16-71");
var fixedRed = Discovery("Nitro AN515-58") with
{
    Services = new Dictionary<string, ServiceEvidence>
    {
        ["PSSvc"] = new("PSSvc", true, true, true, true),
        ["AASSvc"] = new("AASSvc", true, true, true, true),
        ["ASMSvc"] = new("ASMSvc", true, true, true)
    },
    NitroOperationModeReadable = true,
    NitroOperationMode = NitroSense3Protocol.ModeDefault,
    NitroBatteryBoostReady = true
};
var rgbProfile = DeviceProfileResolver.Profiles[0] with
{
    Id = "simulation-only-nitro-rgb", Model = "AN515-58", Sku = "RGB-SKU", Board = "TEST-BOARD", Bios = "V1.02",
    Evidence = "Synthetic RGB fixture only. This is not a verified real Nitro configuration.",
    PowerModes = [new(0, "Quiet"), new(1, "Default"), new(4, "Performance", OnBattery: false)],
    Keyboard = new(KeyboardKind.Rgb, "Explicit synthetic RGB SKU evidence", ZoneCount: 4, Effects: [0, 1, 8])
};
var profiles = DeviceProfileResolver.Profiles.Append(rgbProfile).ToArray();
var rgb = fixedRed with { Identity = fixedRed.Identity with { Sku = "RGB-SKU" } };
foreach (var (model, family) in new[] { (" PH16-71 ", DeviceFamily.PredatorHelios), ("predator phn16-71", DeviceFamily.PredatorHeliosNeo),
    ("PT14-51", DeviceFamily.PredatorTriton), ("Nitro AN515-58", DeviceFamily.Nitro5), ("ANV15-51", DeviceFamily.NitroV),
    ("AN16-41", DeviceFamily.Nitro), ("AN17-51", DeviceFamily.Nitro), ("UNKNOWN", DeviceFamily.UnknownAcer), ("BAN515-58", DeviceFamily.UnknownAcer) })
    Check(Id(model).Family == family, "Unambiguous family classification: " + model);
Check(Id(" nitro  an515-58 ").NormalizedModel == "AN515-58", "Model normalization preserves generation suffix");
Check((Id("AN515-58") with { Manufacturer = "Other Acer Compatible OEM" }).Family == DeviceFamily.Unsupported, "Manufacturer substring cannot authorize Acer");
Check((Id("PH16-71") with { ProductName = "AN515-58" }).NormalizedModel == "", "Contradictory model fields cannot select a profile");
Check(new DeviceIdentity().Family == DeviceFamily.Unsupported, "Missing manufacturer is not Acer");
Check(DeviceIdentity.Clean("To be filled by O.E.M.") == "" && DeviceIdentity.Clean(new string('x', 257)) == "", "Malformed and placeholder SMBIOS fields are ignored");
Check((Id("PH16-71") with { SerialNumber = "OTHER" }).CompatibilityKey == Id("PH16-71").CompatibilityKey, "Serial alone cannot select firmware");
Check((Id("PH16-71") with { BiosVersion = "V9.99" }).CompatibilityKey != Id("PH16-71").CompatibilityKey, "Firmware changes invalidate compatibility preference binding");
var predatorCaps = DeviceProfileResolver.Resolve(predator);
Check(predatorCaps.PowerModeCount == 5 && predatorCaps.SupportsRgbLighting && predatorCaps.CanWrite(HardwareFeature.FanCurve), "Legacy Predator fixture preserves modes, RGB and safe fan implementation");
Check(DeviceProfileResolver.Resolve(neo).ProfileId == "legacy-phn16-71", "Helios Neo selects its own legacy profile");
Check(!predatorCaps.SupportsGpuMux, "Predator branding does not imply MUX");
Check(!predatorCaps.SupportsCoolBoost, "Predator family does not imply CoolBoost");
Check(DeviceProfileResolver.Resolve(fixedRed).Keyboard.Kind == KeyboardKind.Binary, "AN515-58 fixed-red fixture resolves binary controls");
var rgbCaps = DeviceProfileResolver.Resolve(rgb, profiles);
Check(rgbCaps.ProfileId == rgbProfile.Id && rgbCaps.Keyboard.SupportsRgb, "Exact Nitro RGB SKU/board/BIOS profile takes precedence");
Check(rgbCaps.Keyboard.Effects!.SequenceEqual([0, 1, 8]) && rgbCaps.Keyboard.ZoneCount == 4, "Only explicit RGB effects and zones are exposed");
Check(!rgbCaps.PowerModeValues.Contains((byte)5), "RGB Nitro fixture does not acquire Predator Turbo");
Check(!DeviceProfileResolver.Resolve(rgb with { Identity = rgb.Identity with { BiosVersion = "V1.03" } }, profiles).Keyboard.SupportsRgb, "Different BIOS does not inherit RGB SKU compatibility");
Check(DeviceProfileResolver.Resolve(rgb, profiles.Append(rgbProfile with { Id = "ambiguous" })).ProfileId == "read-only", "Conflicting equally specific profiles fail closed");
var anvProfile = rgbProfile with { Id = "simulation-anv", Model = "ANV15-51", Sku = null, Bios = null, Board = null,
    Keyboard = new(KeyboardKind.Unknown, "Unknown"), Bindings = [new(HardwareFeature.Power, "acer-wmi-profile", "SetGamingProfile")],
    PowerModes = [new(1, "Default")] };
var anv = Discovery("ANV15-51");
Check(DeviceProfileResolver.Resolve(anv, [anvProfile]).PowerModeValues.SequenceEqual(new byte[] { 1 }), "Nitro V resolves only its explicit fixture mode");
var tachOnly = anvProfile with { Id = "simulation-tach-only", Bindings = [], PowerModes = [] };
var tachCaps = DeviceProfileResolver.Resolve(anv, [tachOnly]);
Check(tachCaps.Sensors.ContainsKey("cpuFanRpm") && !tachCaps.CanWrite(HardwareFeature.FanManual), "Fan telemetry does not imply manual control");
Check(!DeviceProfileResolver.Resolve(fixedRed).SupportsBatteryControl, "Installed battery service is insufficient to establish a charging control");
var battery = fixedRed with { BatteryStatusReadable = true, BatteryLimitEnabled = false,
    Methods = fixedRed.Methods.Append("BatteryControl.SetBatteryHealthControl").ToHashSet() };
Check(DeviceProfileResolver.Resolve(battery).SupportsBatteryControl, "Valid battery status and setter establish operational control");
var mux = predator with { GpuModeMask = 3 };
Check(DeviceProfileResolver.Resolve(mux).GpuModeMask == 3, "Explicit MUX mask exposes hybrid/discrete without invented Auto");
var missingAgent = mux with { Services = new Dictionary<string, ServiceEvidence> { ["AASSvc"] = new("AASSvc", true, false, true) } };
var degraded = DeviceProfileResolver.Resolve(missingAgent);
Check(!degraded.SupportsGpuMux && degraded.Results[HardwareFeature.GpuMux].Status == CapabilityStatus.Unavailable && degraded.CanWrite(HardwareFeature.Power), "Unavailable Acer Agent degrades dependent features independently");
Check(!DeviceProfileResolver.Resolve(mux with { GpuModeMask = 255 }).SupportsGpuMux, "Malformed GPU capability masks cannot enable switching");
Check(!DeviceProfileResolver.Resolve(predator with { Methods = new HashSet<string>() }).CanWrite(HardwareFeature.Power), "Missing method disables legacy controls");
Check(!DeviceProfileResolver.Resolve(Discovery("Unknown Acer model")).Results.Values.Any(c => c.CanWrite), "Unidentified Acer uses read-only fallback");
Check(!DeviceProfileResolver.Resolve(predator with { Identity = predator.Identity with { Manufacturer = "Other" }, BatteryStatusReadable = true, GpuModeMask = 7 }).Results.Values.Any(c => c.CanWrite), "Non-Acer rejects every Acer-specific operation");

using (var io = new FakeIo())
using (var controller = new WmiController(predator, io))
{
    var result = controller.SetPowerMode(4);
    Check(result.Accepted && !result.Confirmed && io.Writes.SequenceEqual([("SetGamingProfile", 4UL)]), "Power uses exactly one selected payload and reports unverified acknowledgement");
    io.Writes.Clear(); io.IsOnAc = false;
    Check(!controller.SetPowerMode(5).Accepted && io.Writes.Count == 0, "AC-only Turbo never dispatches on battery");
    io.IsOnAc = true; io.Succeed = false;
    Check(controller.SetPowerMode(1).Status == "failed", "Failed power write never reports success");
    io.Succeed = true;
    Check(!controller.SetFanSpeed(0, 50) && !controller.SetFanSpeed(101, 50), "Unsafe fan values cannot generate writes");
    io.Writes.Clear();
    controller.SetFanBehavior(3); controller.SetFanSpeed(50, 60); controller.RecoverAutomaticFans();
    Check(io.Writes.Count == 6 && io.Writes.Last() == ("SetGamingFanBehavior", 0x09UL | (1UL << 16) | (1UL << 22)), "Manual session can safely request firmware Auto through its selected backend");
    io.Sensors[1] = 200; io.Sensors[2] = 0;
    Check(controller.CpuTemp == null && controller.CpuFanRpm == 0 && controller.AuxFanRpm == null, "Telemetry rejects implausible temperature and preserves real stopped-fan zero");
}
using (var io = new FakeIo())
using (var controller = new WmiController(fixedRed, io))
{
    controller.SetRgbMode(0, 1, 2, 3, 100, 5, 0);
    controller.ApplyLightingSynchronous([Color.Red, Color.Red, Color.Red, Color.Red]);
    Check(io.LightingWrites == 0 && io.Writes.Count == 0, "Fixed-red Nitro receives no RGB writes, including synchronous startup/game paths");
    Check(controller.KeyboardControl.Apply(true, null).Confirmed && io.KeyboardIo.Writes == 1, "Verified binary fixture receives only read-modify-write APGe control");
    Check(!controller.SetPowerMode(5).Accepted && io.Writes.Count == 0, "Unsupported Nitro Turbo never dispatches");
}
using (var io = new FakeIo())
using (var controller = new WmiController(rgb, io, profiles))
{
    controller.ApplyLightingSynchronous([Color.Red, Color.Green, Color.Blue, Color.White]);
    Check(io.LightingWrites == 1 && io.Writes.Count == 4, "Verified synthetic Nitro RGB configuration uses its correct lighting and zone commands");
    controller.ApplyLightingSynchronous([Color.Red, Color.Green, Color.Blue, Color.White], 7);
    Check(io.LightingWrites == 1, "Unsupported RGB effects do not dispatch");
}
using (var io = new FakeIo())
using (var controller = new WmiController(Discovery("Unidentified model"), io))
{
    Check(controller.KeyboardControl.Restore(null, null) == null, "Unknown keyboard has no default startup command");
    controller.KeyboardControl.Restore(true, true); controller.SetFanBehavior(3); controller.SetCpuFanSpeed(50);
    controller.SetBatteryChargeLimit(true); await controller.SetGpuModeAsync(1); controller.SetBootSound(true); controller.SetUsbCharging(true);
    Check(io.TotalWrites == 0, "Saved preferences and background entry points cannot bypass unknown capabilities");
}
using (var io = new FakeIo { BatteryReadback = null })
using (var controller = new WmiController(battery, io))
{
    Check(!controller.SetBatteryChargeLimit(true) && controller.LastOperation?.Status == "unverified", "Battery acknowledgement without readback never claims enabled limit");
    io.BatteryReadback = true;
    Check(controller.SetBatteryChargeLimit(true) && controller.LastOperation?.Confirmed == true, "Matching battery readback confirms the requested state");
}
using (var io = new FakeIo())
using (var controller = new WmiController(mux, io))
{
    Check(!await controller.SetGpuModeAsync(2) && io.GpuWrites == 0, "Unsupported MUX Auto does not dispatch");
    Check(await controller.SetGpuModeAsync(1) && io.GpuWrites == 1 && controller.LastOperation?.Status == "unverified", "Supported MUX request remains pending reboot verification");
}

int active = 0, maximum = 0, writes = 0;
var gate = new HardwareWriteGate(() => predatorCaps);
await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => Task.Run(() => gate.Execute(HardwareFeature.Power, true, () =>
{
    int n = Interlocked.Increment(ref active); maximum = Math.Max(maximum, n);
    Thread.Sleep(2); Interlocked.Increment(ref writes); Interlocked.Decrement(ref active);
    return new("unverified", "Fake command accepted.");
}))));
Check(maximum == 1 && writes == 30, "Concurrent command sources cannot overlap hardware writes");
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    Check(!gate.Execute(HardwareFeature.Power, true, () => throw new Exception("Must not dispatch"), cancelled.Token).Accepted, "Cancellation before dispatch cannot write");
}
Check(gate.Execute(HardwareFeature.Power, true, () => throw new IOException("Fake transport failure")).Status == "failed", "Transport exceptions are failed operations");
var unavailableGate = new HardwareWriteGate(() => DeviceProfileResolver.Resolve(mux), _ => false);
Check(!unavailableGate.Execute(HardwareFeature.GpuMux, true, () => throw new Exception("Must not dispatch")).Accepted, "Service loss between detection and command prevents dispatch");

string diagnostic = CompatibilityDiagnostics.Export(predatorCaps, "test", new { available = true }, gate.LastOperation);
using (var json = JsonDocument.Parse(diagnostic))
    Check(json.RootElement.GetProperty("capabilities").GetProperty("profileId").GetString() == "legacy-ph16-71", "Diagnostic report is valid structured JSON");
Check(!diagnostic.Contains("SECRET-SERIAL") && !JsonSerializer.Serialize(predator.Identity).Contains("SECRET-SERIAL") && !predator.Identity.ToString().Contains("SECRET-SERIAL"), "Diagnostics, bridge serialization and record printing redact serials");
var escaped = predatorCaps with { Identity = predator.Identity with { Model = "quotes \" and \\ slash\nline" } };
using (JsonDocument.Parse(CompatibilityDiagnostics.Export(escaped, "test", null, null))) Check(true, "Diagnostic serializer escapes malformed SMBIOS strings");
Check(AcerAgentProtocol.GpuMask("{\"SUPPORT_GPU_MODE_CAPABILITY\":[3]}") == 3 && AcerAgentProtocol.GpuMask("{\"mode\":1}") == 0,
    "Current GPU mode cannot be expanded into an invented supported mask");
foreach (string malformed in new[] { "{}", "{", "{\"SUPPORT_GPU_MODE_CAPABILITY\":[\"7\"]}", "{\"SUPPORT_GPU_MODE_CAPABILITY\":[255]}", "{\"SUPPORT_GPU_MODE_CAPABILITY\":[3,7]}" })
    Check(AcerAgentProtocol.GpuMask(malformed) == 0, "Malformed Agent capability response is rejected: " + malformed);
byte[] frame = "ACER"u8.ToArray().Concat(BitConverter.GetBytes(20U)).Concat(Encoding.UTF8.GetBytes("{\"message\":\"é\",\"result\":\"0\"}")).ToArray();
using (var stream = new FragmentedStream(frame))
    Check(await AcerAgentProtocol.ReadReply(stream, 20, CancellationToken.None) == "{\"message\":\"é\",\"result\":\"0\"}", "Fragmented header, JSON and UTF-8 are read completely");
using (var stream = new FragmentedStream(frame)) Check(await AcerAgentProtocol.ReadReply(stream, 100, CancellationToken.None) == null, "Wrong reply opcode is rejected");
using (var stream = new FragmentedStream(Encoding.UTF8.GetBytes("{\"result\":\"0\""))) Check(await AcerAgentProtocol.ReadReply(stream, 20, CancellationToken.None) == null, "Truncated acknowledgement never reports success");
Check(FanCurveSafety.MustRecover(null, 50, 100) && FanCurveSafety.MustRecover(50, null, 100) && FanCurveSafety.MustRecover(50, 50, 5001), "Missing, partial or stale temperatures require fan Auto recovery");
Check(!FanCurveSafety.MustRecover(50, 50, 100), "Fresh valid thermal samples allow a supported curve");
Check((Id("PH16-71") with { SerialNumber = "SECOND" }).LocalPreferenceKey != Id("PH16-71").LocalPreferenceKey, "Local-only settings isolate individual devices without exporting serials");
Check((Id("PH16-71") with { ProductVendor = "Different OEM" }).Family == DeviceFamily.Unsupported, "Contradictory SMBIOS product vendor fails closed");
Check(Id("Predator Helios Neo").Family == DeviceFamily.PredatorHeliosNeo && DeviceProfileResolver.Resolve(Discovery("Predator Helios Neo")).ProfileId == "read-only", "Family names can brand a device without selecting firmware");
Check(HardwarePreferencePolicy.IsHardwareSetting("FanSpeedCpu") && !HardwarePreferencePolicy.IsHardwareSetting("JelliEnabled"), "Hardware migration leaves application preferences independent");
using (var io = new FakeIo())
using (var controller = new WmiController(rgb, io, profiles.Select(p => p.Id == rgbProfile.Id ? p with { Keyboard = p.Keyboard with { ZoneCount = 2 } } : p)))
{
    controller.ApplyLightingSynchronous([Color.Red, Color.Blue]);
    Check(io.LightingWrites == 1 && io.Writes.Count == 2 && io.Writes.All(w => (w.Item2 & 255) is 1 or 2), "Verified two-zone fixture receives only its two supported zone writes");
}
using (var stream = new MemoryStream(new byte[32769]))
    Check(await AcerAgentProtocol.ReadReply(stream, 20, CancellationToken.None) == null, "Oversized malformed service replies are bounded");
using (var io = new FakeIo())
using (var controller = new WmiController(predator, io))
{
    controller.ObserveTelemetry(new TelemetrySnapshot(null, 55, 0, null, System.Windows.Forms.PowerLineStatus.Online));
    Check(controller.Capabilities.TelemetryChannels["cpuTemp"].Status == CapabilityStatus.Unavailable && controller.Capabilities.TelemetryChannels["cpuFanRpm"].Status == CapabilityStatus.Supported, "Telemetry capability evidence distinguishes missing sensor from real zero");
    string bridge = JsonSerializer.Serialize(controller.Capabilities, CompatibilityDiagnostics.Json);
    using var json = JsonDocument.Parse(bridge);
    var modes = json.RootElement.GetProperty("powerModes").EnumerateArray().Select(m => m.GetProperty("value").GetByte()).ToArray();
    Check(modes.SequenceEqual(controller.Capabilities.PowerModeValues), "Jelli bridge modes and Classic capability projection share one snapshot");
}
var brokenDiscovery = new HardwareWriteGate(() => throw new IOException("Synthetic discovery failure"));
Check(brokenDiscovery.Execute(HardwareFeature.Power, true, () => throw new Exception("Must not dispatch")).Status == "failed", "Discovery failure prevents dispatch without crashing");
var brokenService = new HardwareWriteGate(() => DeviceProfileResolver.Resolve(mux), _ => throw new IOException("Synthetic SCM failure"));
Check(brokenService.Execute(HardwareFeature.GpuMux, true, () => throw new Exception("Must not dispatch")).Status == "failed", "Service-query failure prevents dispatch without crashing");
using (var io = new FakeIo())
using (var controller = new WmiController(predator, io))
{
    controller.SetFanBehavior(3);
    io.FailAtWrite = io.Writes.Count + 2;
    Check(!controller.SetFanSpeed(50, 60) && io.Writes.Last().Item1 == "SetGamingFanBehavior", "Partial CPU/GPU fan write failure requests firmware Auto");
}
var excludedMux = DeviceProfileResolver.Profiles[0] with { Id = "simulation-no-mux", UnsupportedFeatures = new HashSet<HardwareFeature> { HardwareFeature.GpuMux } };
Check(DeviceProfileResolver.Resolve(predator, [excludedMux]).Results[HardwareFeature.GpuMux].Status == CapabilityStatus.Unsupported, "Explicit negative profile evidence is represented as Unsupported");
Check(DeviceProfileResolver.Resolve(mux, [excludedMux]).Results[HardwareFeature.GpuMux].Status == CapabilityStatus.Unknown, "Conflicting positive and negative capability evidence fails closed");
Check(HardwareCapabilities.None.ChassisFamily == AcerChassisFamily.Unsupported && !HardwareCapabilities.None.CanWrite(HardwareFeature.Power), "Discovery-error snapshot never defaults to Predator or authorizes commands");
var olderNitro = Discovery("Nitro AN515-54") with
{
    Identity = Id("Nitro AN515-54", "V1.33") with { BoardProduct = "Octavia_CFS" },
    WindowsPowerOverlayReadable = true,
    WindowsPowerPlans = WindowsPowerPlans.Describe([WindowsPowerPlans.PowerSaver, WindowsPowerPlans.Balanced, WindowsPowerPlans.HighPerformance], null, WindowsPowerPlans.Balanced)
};
var olderCaps = DeviceProfileResolver.Resolve(olderNitro);
Check(olderCaps.ProfileId == "nitro-an515-54-octavia", "AN515-54 Octavia resolves its documented profile instead of disabling everything");
Check(olderCaps.CanWrite(HardwareFeature.FanAuto) && olderCaps.CanWrite(HardwareFeature.FanMax) && olderCaps.CanWrite(HardwareFeature.FanManual) && olderCaps.CanWrite(HardwareFeature.FanCurve), "AN515-54 exposes existing Auto/Max/Custom and temperature-monitored curves");
Check(olderCaps.PowerModeValues.SequenceEqual(new byte[] { 0, 1, 4 }) && olderCaps.Results[HardwareFeature.Power].Backend == "windows-power-plan", "AN515-54 uses installed Windows power plans without Predator Turbo or firmware profile values");
Check(!olderCaps.SupportsRgbLighting && !olderCaps.SupportsGpuMux && !olderCaps.SupportsLcdOverdrive, "AN515-54 does not inherit unsupported Predator controls");
Check(DeviceProfileResolver.Resolve(olderNitro with { Identity = olderNitro.Identity with { BoardProduct = "OTHER" } }).ProfileId == "read-only", "AN515-54 board restriction cannot match unrelated hardware");
var noPlans = DeviceProfileResolver.Resolve(olderNitro with { WindowsPowerPlans = new([],null,"Discovery failed") });
Check(!noPlans.CanWrite(HardwareFeature.Power) && noPlans.CanWrite(HardwareFeature.FanAuto), "Unavailable Windows plan API does not suppress independent fan support");
using (var io = new FakeIo { Sensors = new() { [1] = 72, [10] = 49, [2] = 4285, [6] = 4225 } })
using (var controller = new WmiController(olderNitro, io))
{
    var readings = controller.GetAllSensors(true);
    Check(readings.cpuTemp == 72 && readings.gpuTemp == 49 && readings.cpuFanRpm == 4285 && readings.gpuFanRpm == 4225 && readings.gpuPowerW == null, "AN515-54 telemetry preserves the validated sensor map without inventing a GPU power sensor");
    Check(io.TotalWrites == 0, "AN515-54 discovery and telemetry perform no writes");
    Check(controller.SetPowerMode(4).Confirmed && io.WindowsPlanWrites == 1 && io.WindowsPowerWrites == 0 && io.Writes.Count == 0, "AN515-54 power-plan change uses the isolated fake API, never an overlay or SetGamingProfile");
    Check(!controller.SetPowerMode(5).Accepted && io.WindowsPlanWrites == 1, "AN515-54 Turbo is rejected before any dispatch");
    io.Succeed = false;
    Check(!controller.SetPowerMode(0).Accepted, "Failed Windows power change is not reported as successful");
    io.Succeed = true;
    Check(controller.SetFanBehavior(2).Accepted && io.Writes.Last() == ("SetGamingFanBehavior", 0x820009UL), "AN515-54 Max uses the existing bounded two-fan encoding");
    Check(controller.SetFanBehavior(3).Accepted && io.Writes.TakeLast(2).Select(w => w.Item2 & 255).SequenceEqual(new ulong[] { 1, 4 }), "AN515-54 Custom targets only the documented CPU and GPU fan IDs");
}
Check(WmiController.TryDecodeBatteryStatus(new byte[2], (byte)1, new byte[5], out var limitOff) && limitOff == false, "Actual BatteryControl status-array contract decodes a disabled health limit");
Check(WmiController.TryDecodeBatteryStatus(new byte[2], (byte)3, new byte[] { 1, 0, 0, 0, 0 }, out var limitOn) && limitOn == true, "Actual BatteryControl function list decodes enabled health mode independently of calibration");
Check(!WmiController.TryDecodeBatteryStatus(new byte[2], (byte)2, new byte[5], out _) && !WmiController.TryDecodeBatteryStatus(new byte[] { 1, 0 }, (byte)1, new byte[5], out _), "Unsupported battery health mode and nonzero firmware return code fail closed");
Check(!WmiController.TryDecodeBatteryStatus((ushort)0, (byte)1, (byte)1, out _) && !WmiController.TryDecodeBatteryStatus(new byte[2], (byte)1, new byte[4], out _), "Scalar guesses and malformed battery status arrays are rejected");
var unknownWithSensors = Discovery("Nitro AN515-99") with { SupportedSensorMask = (1 << 0) | (1 << 1) | (1 << 5) | (1 << 9) };
var unknownSensors = DeviceProfileResolver.Resolve(unknownWithSensors);
Check(unknownSensors.ProfileId == "read-only" && unknownSensors.Sensors.Count == 4 && !unknownSensors.CanWrite(HardwareFeature.FanManual), "Firmware-advertised read-only telemetry works without authorizing any writes on an unprofiled Acer");
Check(DeviceProfileResolver.Resolve(unknownWithSensors with { SupportedSensorMask = 1 << 1 }).Sensors.Keys.SequenceEqual(new[] { "cpuFanRpm" }), "Sensor capability mask exposes only the advertised standard channel");
Check(DeviceProfileResolver.Resolve(unknownWithSensors with { Identity = unknownWithSensors.Identity with { Manufacturer = "Other OEM" } }).Sensors.Count == 0, "Non-Acer hardware cannot acquire Acer telemetry through an injected sensor bitmap");
var golden = JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"nitrosense3-golden-vectors.json")))!;
foreach (var (name,command,input,wide) in new (string,ushort,ulong,bool)[] {
    ("fanAuto",15,0x410009,true),("fanMax",15,0x820009,true),("fanCustom",15,0xC30009,true),
    ("cpu50",16,0x3201,true),("gpu50",16,0x3204,true),("coolOn",17,0x10007,true),
    ("coolOff",17,7,true),("coolRead",20,519,false),("cpuTemp",13,257,false),
    ("opModeQuiet",30,0,false),("opModeDefault",30,1,false),("opModePerformance",30,4,false),
    ("opModeWmiQuiet",63,0x000B,true),("opModeWmiDefault",63,0x010B,true),("opModeWmiPerformance",63,0x040B,true),
    ("opModeWmiRead",64,0x0B,false)})
    Check(Convert.ToHexString(NitroSense3Protocol.Encode(command,input,wide))==golden[name], "NitroSense packet matches isolated official client capture: " + name);
byte[] reply32 = [1,4,0,0,0,0,0,0,0];
byte[] reply64 = [1,8,0,0,0,0,1,0,0,0,0,0,0];
using (var stream = new FragmentedStream(reply64)) Check(await NitroSense3Protocol.ReadReply(stream,true,CancellationToken.None)==256, "NitroSense fragmented reply is fully read");
foreach (byte[] bad in new byte[][] { [], reply32[..4], reply32[..8], [2,4,0,0,0,0,0,0,0], [1,255,255,255,127] })
    using (var stream = new MemoryStream(bad)) Check(await NitroSense3Protocol.ReadReply(stream,false,CancellationToken.None)==null,"NitroSense rejects truncated/malformed reply without allocating its declared size");
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel(); using var stream = new MemoryStream(reply32);
    Check(await NitroSense3Protocol.ReadReply(stream,false,cancelled.Token)==null,"NitroSense deadline/cancellation never reports success");
}
Check(NitroSense3Protocol.CoolBoostState(1)==null && NitroSense3Protocol.CoolBoostState(512)==null && NitroSense3Protocol.CoolBoostState(256)==true,"CoolBoost status and state bytes are independently validated");
var nitroService = olderNitro with { Services = new Dictionary<string,ServiceEvidence> { ["PSSvc"] = new("PSSvc",true,true,true,true) }, NitroCoolBoostEnabled=false };
Check(DeviceProfileResolver.Resolve(nitroService).SupportsCoolBoost,"Exact Nitro 3 profile plus operational trusted PSSvc enables CoolBoost");
Check(!DeviceProfileResolver.Resolve(nitroService with { Identity=Id("AN515-58") }).SupportsCoolBoost,"NitroSense 3 service presence cannot enable another generation");
Check(!DeviceProfileResolver.Resolve(nitroService with { Services = new Dictionary<string,ServiceEvidence> { ["PSSvc"]=new("PSSvc",true,true,false,true) } }).SupportsCoolBoost,"Untrusted PSSvc cannot authorize commands");
using (var io = new FakeIo())
using (var controller = new WmiController(nitroService,io))
{
    Check(controller.SetCoolBoost(true) && controller.LastOperation?.Confirmed==true && io.NitroCalls.SequenceEqual(new (ushort,ulong,bool)[] { (17,0x10007,true),(20,519,false) }) && io.Writes.Count==0,"CoolBoost sends one exact setter then one readback, with no conflicting WMI fan writes");
    io.NitroReadUnavailable=true;
    Check(!controller.SetCoolBoost(false) && controller.LastOperation?.Status=="unverified","Acknowledgement without CoolBoost readback does not confirm or save state");
    io.Succeed=false; int count=io.NitroCalls.Count;
    Check(!controller.SetCoolBoost(true) && io.NitroCalls.Count==count+1 && controller.LastOperation?.Status=="failed","Rejected Nitro command is neither retried nor followed by another write");
}
Check(!OemServiceManager.OptimizationCandidates.Intersect(OemServiceManager.NitroEssentialServices).Any() && OemServiceManager.NitroEssentialServices.Contains("PSSvc"),"NitroSense PSSvc is an explicit dependency excluded from optimization");
var nitroDirect = olderNitro with { NitroDirectCoolBoostReadable=true, Methods=olderNitro.Methods.Append("APGeAction.SetFunction").ToHashSet() };
Check(DeviceProfileResolver.Resolve(nitroDirect).Results[HardwareFeature.CoolBoost] is { CanWrite:true, Backend:"nitrosense3-apge", RequiredService:null },"Validated APGe CoolBoost is selected without depending on the nonresponsive pipe");
Check(!DeviceProfileResolver.Resolve(nitroDirect with { Methods=olderNitro.Methods }).SupportsCoolBoost,"APGe getter alone cannot authorize a missing setter");
using (var io=new FakeIo())
using (var controller=new WmiController(nitroDirect,io))
    Check(controller.SetCoolBoost(true) && io.NitroCalls.Count==2 && io.Writes.Count==0,"Direct Nitro CoolBoost retains one setter/readback and no unrelated fan command");
var onlyBalanced = WindowsPowerPlans.Describe([WindowsPowerPlans.Balanced],null,WindowsPowerPlans.Balanced);
Check(onlyBalanced.Modes.Select(m => m.Value).SequenceEqual(new byte[] {1}) && onlyBalanced.Evidence.Contains("Only Balanced"),"Only installed Balanced is shown, with an explanation for missing plans");
var mergedSchemes = WindowsPowerPlans.MergeSchemeSources([WindowsPowerPlans.Balanced], ["a1841308-3541-4fab-bc81-f71556f20b4a", "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", "not-a-guid"]);
var mergedDiscovery = WindowsPowerPlans.Describe(mergedSchemes, null, WindowsPowerPlans.Balanced);
Check(mergedDiscovery.Modes.Select(m => m.Value).SequenceEqual(new byte[] {0, 1, 4}), "Windows PowerSchemes registry definitions expose Power saver and High performance even when PowerEnumerate only returns Balanced");
var balancedOnlyCaps = DeviceProfileResolver.Resolve(olderNitro with { WindowsPowerPlans=onlyBalanced });
Check(balancedOnlyCaps.PowerModeValues.SequenceEqual(new byte[] {1}) && balancedOnlyCaps.ActiveWindowsPowerPlan==WindowsPowerPlans.Balanced,"Capability snapshot and current selection use the actual Windows plan GUID");
Guid acerPlan = new("e5f40712-50bd-4144-98f9-94735ab0234d"); // Deliberately synthetic OEM registration.
var allPlans = WindowsPowerPlans.Describe([WindowsPowerPlans.PowerSaver,WindowsPowerPlans.Balanced,acerPlan,WindowsPowerPlans.HighPerformance],acerPlan,WindowsPowerPlans.Balanced);
Check(allPlans.Modes.Select(m => m.Value).SequenceEqual(new byte[] {0,1,2,4}) && allPlans.Modes.Single(m => m.Value==2).WindowsScheme==acerPlan,"Installed AcerGUID registration adds Acer Optimized alongside the three standard plans");
Check(!WindowsPowerPlans.Describe([WindowsPowerPlans.Balanced],acerPlan,WindowsPowerPlans.Balanced).Modes.Any(m => m.Value==2),"OEM registry presence cannot invent an uninstalled Acer plan");
Check(WindowsPowerPlans.Describe([WindowsPowerPlans.Balanced],WindowsPowerPlans.Balanced,WindowsPowerPlans.Balanced).Modes.Count==1,"An OEM GUID aliasing Balanced cannot create a duplicate mode");
Check(!DeviceProfileResolver.Resolve(olderNitro with { WindowsPowerPlans=onlyBalanced with {ActiveScheme=null} }).CanWrite(HardwareFeature.Power),"Unreadable active plan prevents power writes despite installed choices");
Check(!DeviceProfileResolver.Resolve(Discovery("ANV15-51") with {WindowsPowerPlans=allPlans}).CanWrite(HardwareFeature.Power),"Windows plans do not authorize a different Nitro generation");
Check(!DeviceProfileResolver.Resolve(olderNitro with {Identity=olderNitro.Identity with {Manufacturer="Other"},WindowsPowerPlans=allPlans}).CanWrite(HardwareFeature.Power),"Installed plans cannot bypass device-profile authorization");
using (var io=new FakeIo {PlanInventory=allPlans})
using (var controller=new WmiController(olderNitro with {WindowsPowerPlans=allPlans},io))
{
    Check(controller.SetPowerMode(2).Confirmed && io.ActivePlan==acerPlan && controller.Capabilities.ActiveWindowsPowerPlan==acerPlan,"Acer Optimized selection passes its exact GUID and updates cached state only after matching readback");
    Check(io.WindowsPlanWrites==1 && io.WindowsPowerWrites==0 && io.Writes.Count==0 && io.NitroCalls.Count==0,"Plan selection performs no overlay, WMI, NitroSense or CoolBoost command");
    string bridge=JsonSerializer.Serialize(controller.Capabilities,CompatibilityDiagnostics.Json);
    using var json=JsonDocument.Parse(bridge);
    Check(json.RootElement.GetProperty("powerModes").EnumerateArray().Select(m=>m.GetProperty("value").GetByte()).SequenceEqual(controller.Capabilities.PowerModeValues),"Windows plan IDs and labels use the same Classic/Jelli capability projection");
}
using (var io=new FakeIo {PlanInventory=onlyBalanced})
using (var controller=new WmiController(olderNitro,io))
    Check(!controller.SetPowerMode(4).Accepted && io.WindowsPlanWrites==0,"A plan removed after discovery is rejected before the setter");
foreach (bool missing in new[] {false,true})
using (var io=new FakeIo {PlanReadbackMismatch=!missing,PlanReadbackUnavailable=missing})
using (var controller=new WmiController(olderNitro,io))
    Check(controller.SetPowerMode(4).Status=="unverified" && controller.Capabilities.ActiveWindowsPowerPlan==WindowsPowerPlans.Balanced,"Mismatched/missing plan readback never confirms or replaces the known active plan");
int planCalls=0;
Check(WindowsPowerPlans.Apply(Guid.Empty,onlyBalanced,_=>{planCalls++;return 0;},()=>WindowsPowerPlans.Balanced).Status=="failed" && planCalls==0,"Empty or absent power-plan GUID cannot reach the setter");
Check(WindowsPowerPlans.Apply(WindowsPowerPlans.Balanced,onlyBalanced with {ActiveScheme=null},_=>{planCalls++;return 0;},()=>WindowsPowerPlans.Balanced).Status=="failed" && planCalls==0,"Failed discovery never dispatches a plan change");
Check(WindowsPowerPlans.Apply(WindowsPowerPlans.Balanced,onlyBalanced,_=>5,()=>throw new Exception("Do not read after failure")).Status=="failed","Windows access-denied result is not reported as successful");
Check(WindowsPowerPlans.Apply(WindowsPowerPlans.Balanced,onlyBalanced,_=>throw new IOException(),()=>WindowsPowerPlans.Balanced).Status=="failed","Power setter exceptions fail without claiming acknowledgement");
Check(WindowsPowerPlans.Apply(WindowsPowerPlans.Balanced,onlyBalanced,_=>0,()=>throw new IOException()).Status=="unverified","Readback exception after a successful setter remains unverified");
Check(WindowsPowerPlans.Set(WindowsPowerPlans.Balanced).Status=="failed","Direct Windows plan API rejects calls outside capability authorization without accessing Windows");
Check(NitroSense3Protocol.EncodeOperationModeMiscInput(0)==0x000BUL && NitroSense3Protocol.EncodeOperationModeMiscInput(1)==0x010BUL && NitroSense3Protocol.EncodeOperationModeMiscInput(4)==0x040BUL,"Nitro SetOperationMode WMI misc encoding matches PSSvc ((mode << 8) | 0x0B)");
Check(NitroSense3Protocol.MapGpuOverclockLevel(0)==0 && NitroSense3Protocol.MapGpuOverclockLevel(1)==1 && NitroSense3Protocol.MapGpuOverclockLevel(4)==2 && NitroSense3Protocol.MapGpuOverclockLevel(5)==null,"Nitro SetOperationMode GPU OC level mapping matches PSSvc (0->0, 1->1, 4->2)");
Check(NitroSense3Protocol.OperationModeState(0x0000UL)==0 && NitroSense3Protocol.OperationModeState(0x0100UL)==1 && NitroSense3Protocol.OperationModeState(0x0400UL)==4 && NitroSense3Protocol.OperationModeState(0x0500UL)==null && NitroSense3Protocol.OperationModeState(0x0101UL)==null,"GetGamingMiscSetting(0x0B) status and mode bytes are independently validated");
Check(NitroSense3Protocol.BatteryBoostReady(1UL << 40)==true && NitroSense3Protocol.BatteryBoostReady(0UL)==false && NitroSense3Protocol.BatteryBoostReady(1UL)==null,"GetGamingSysInfo(0x0002) BatteryBoost readiness decodes byte 5 and rejects nonzero status");
Check(NitroSense3Protocol.ParseRegistryOperationMode(0)==0 && NitroSense3Protocol.ParseRegistryOperationMode(1)==1 && NitroSense3Protocol.ParseRegistryOperationMode(4)==4 && NitroSense3Protocol.ParseRegistryOperationMode(5)==null && NitroSense3Protocol.ParseRegistryOperationMode("4")==4,"CurrentOperationMode registry parser accepts only verified Nitro modes {0, 1, 4}");
var nitroJimnyCaps = DeviceProfileResolver.Resolve(fixedRed);
Check(nitroJimnyCaps.PowerModeValues.SequenceEqual(new byte[] {0,1,4}) && nitroJimnyCaps.PowerModes.Select(m=>m.Label).SequenceEqual(new[] {"Quiet","Default","Performance"}) && nitroJimnyCaps.Results[HardwareFeature.Power].Backend=="nitrosense3-opmode" && nitroJimnyCaps.ActivePowerMode==1,"Nitro MachineType=2 enumerates Quiet(0), Default(1), Performance(4) on nitrosense3-opmode and reads active mode");
foreach (string model in new[] {"Nitro AN515-46","Nitro AN515-47","Nitro AN517-42","Nitro AN517-43","Nitro AN517-55"})
{
    var mCaps = DeviceProfileResolver.Resolve(fixedRed with { Identity = Id(model) });
    Check(mCaps.CanWrite(HardwareFeature.Power) && mCaps.PowerModeValues.SequenceEqual(new byte[] {0,1,4}) && mCaps.Results[HardwareFeature.Power].Backend=="nitrosense3-opmode","Verified Nitro MachineType=2 model resolves native Nitro operation modes: " + model);
}
var nitroWmiDirect = fixedRed with
{
    Services = new Dictionary<string,ServiceEvidence>(),
    NitroOperationModeReadable = false,
    NitroDirectOperationModeReadable = true,
    Methods = fixedRed.Methods.Append("GetGamingMiscSetting").ToHashSet()
};
var nitroWmiCaps = DeviceProfileResolver.Resolve(nitroWmiDirect);
Check(nitroWmiCaps.CanWrite(HardwareFeature.Power) && nitroWmiCaps.Results[HardwareFeature.Power] is { CanWrite:true, Backend:"nitrosense3-wmi-opmode", RequiredService:null },"Nitro MachineType=2 falls back to direct GetGamingMiscSetting/SetGamingMiscSetting(0x0B) when PSSvc is absent but WMI readback is verified");
var nitroUnverifiedPower = DeviceProfileResolver.Resolve(fixedRed with { Services = new Dictionary<string,ServiceEvidence>(), NitroOperationModeReadable = false, NitroDirectOperationModeReadable = false });
Check(!nitroUnverifiedPower.CanWrite(HardwareFeature.Power),"Nitro MachineType=2 disables power writes when neither PSSvc nor direct WMI readback is verified");
using (var io = new FakeIo())
using (var controller = new WmiController(fixedRed, io))
{
    Check(controller.ReadPowerMode()==1 && controller.Capabilities.ActivePowerMode==1,"Nitro ReadPowerMode queries PSSvc cmd 64 (0x0B) and updates ActivePowerMode");
    io.NitroCalls.Clear();
    var setPerf = controller.SetPowerMode(4);
    Check(setPerf.Confirmed && controller.Capabilities.ActivePowerMode==4 && io.NitroCalls.SequenceEqual(new (ushort,ulong,bool)[] {(30,4,false),(64,0x0B,false)}) && io.Writes.Count==0,"Nitro SetPowerMode(4) sends SetOperationMode(30, 4) then GetGamingMiscSetting(64, 0x0B) without touching Predator SetGamingProfile");
    io.NitroCalls.Clear();
    Check(controller.SetPowerMode(0).Confirmed && controller.Capabilities.ActivePowerMode==0 && io.NitroCalls.SequenceEqual(new (ushort,ulong,bool)[] {(30,0,false),(64,0x0B,false)}),"Nitro SetPowerMode(0) selects Quiet via cmd 30 and confirms via cmd 64");
    io.NitroCalls.Clear();
    io.IsOnAc = false;
    Check(!controller.SetPowerMode(4).Accepted && !controller.SetPowerMode(0).Accepted && io.NitroCalls.Count==0,"Nitro Quiet(0) and Performance(4) are rejected on battery before any command is sent");
    Check(controller.SetPowerMode(1).Confirmed && controller.Capabilities.ActivePowerMode==1 && io.NitroCalls.SequenceEqual(new (ushort,ulong,bool)[] {(30,1,false),(64,0x0B,false)}),"Nitro Default(1) is allowed on battery");
    io.IsOnAc = true;
    io.NitroOperationModeReadMismatch = true;
    Check(controller.SetPowerMode(4).Status=="unverified" && controller.Capabilities.ActivePowerMode==1,"Nitro SetOperationMode readback mismatch reports unverified and preserves last confirmed mode");
    io.NitroOperationModeReadMismatch = false;
    io.NitroReadUnavailable = true;
    Check(controller.SetPowerMode(4).Status=="unverified" && controller.Capabilities.ActivePowerMode==1,"Nitro SetOperationMode timeout/missing readback reports unverified");
    io.NitroReadUnavailable = false;
    io.Succeed = false;
    Check(controller.SetPowerMode(4).Status=="failed" && controller.Capabilities.ActivePowerMode==1,"Failed Nitro SetOperationMode command reports failed");
    io.Succeed = true;
    int fanWritesBefore = io.Writes.Count;
    io.NitroCalls.Clear();
    Check(controller.SetPowerMode(4).Confirmed && io.Writes.Count==fanWritesBefore && !io.NitroCalls.Any(c => c.Command is 15 or 16 or 17),"Switching Nitro performance profile never emits WMI or NitroSense fan/CoolBoost commands");
}
var nitroJimnyWithFans = DeviceProfileResolver.Profiles.First(p => p.Id == "conservative-an515-58") with
{
    Bindings =
    [
        new(HardwareFeature.Power, NitroPowerProfileCoordinator.OperationModePipeBackend, Service: "PSSvc", CanVerify: true),
        new(HardwareFeature.FanAuto, "acer-wmi-fans", "SetGamingFanBehavior"),
        new(HardwareFeature.FanMax, "acer-wmi-fans", "SetGamingFanBehavior"),
        new(HardwareFeature.FanManual, "acer-wmi-fans", "SetGamingFanSpeed")
    ]
};
using (var io = new FakeIo())
using (var controller = new WmiController(fixedRed, io, [nitroJimnyWithFans]))
{
    Check(controller.SetFanBehavior(3).Accepted && controller.SetFanSpeed(55, 65),"Manual fan session established on Nitro fixture");
    int fanWritesBefore = io.Writes.Count;
    io.NitroCalls.Clear();
    Check(controller.SetPowerMode(4).Confirmed && io.Writes.Count==fanWritesBefore && controller.SetCpuFanSpeed(70),"Switching Nitro performance profile does not clear manual fan session or emit conflicting fan commands");
}
using (var io = new FakeIo())
using (var controller = new WmiController(fixedRed with { NitroBatteryBoostReady = false }, io))
{
    Check(!controller.Capabilities.AllowsPower(0, true) && !controller.Capabilities.AllowsPower(4, true) && controller.Capabilities.AllowsPower(1, true),"BatteryBoost readiness false (<40% battery) restricts Quiet and Performance even on AC, matching NitroSense check_battery_boost");
    Check(!controller.SetPowerMode(4).Accepted && io.NitroCalls.Count==0,"Performance mode is blocked when BatteryBoost is not ready");
}
using (var io = new FakeIo())
using (var controller = new WmiController(nitroWmiDirect, io))
{
    var res = controller.SetPowerMode(4);
    Check(res.Confirmed && controller.Capabilities.ActivePowerMode==4 && io.Writes.SequenceEqual([("SetGamingMiscSetting", 0x040BUL)]) && io.NitroCalls.Count==0,"Direct WMI Nitro operation mode backend encodes SetGamingMiscSetting(0x040B) and confirms via GetGamingMiscSetting(0x0B)");
    Check(!controller.SetPowerMode(5).Accepted && io.Writes.Count==1,"Direct WMI Nitro backend rejects Predator Turbo (5)");
}
var projectedAc = NitroPowerProfileCoordinator.ProjectControls(nitroJimnyCaps with { ActivePowerMode = 4 }, true);
Check(projectedAc.Select(c => (c.Id, c.Label, c.Enabled, c.Active)).SequenceEqual([
    ("power.quiet", "Quiet", true, false),
    ("power.balanced", "Default", true, false),
    ("power.performance", "Performance", true, true)]),"UI/backend coordinator projects Nitro modes, labels, enabled state and active mode on AC");
var projectedBattery = NitroPowerProfileCoordinator.ProjectControls(nitroJimnyCaps with { ActivePowerMode = 1 }, false);
Check(projectedBattery.Select(c => (c.Id, c.Enabled, c.Active)).SequenceEqual([
    ("power.quiet", false, false),
    ("power.balanced", true, true),
    ("power.performance", false, false)]),"UI/backend coordinator disables Quiet and Performance on battery while keeping Default active");
using (var io = new FakeIo { NitroOperationModeState = 1 })
using (var controller = new WmiController(fixedRed, io))
{
    io.NitroCalls.Clear();
    Check(controller.RestoreSavedPowerMode(1).Confirmed && io.NitroCalls.SequenceEqual(new (ushort,ulong,bool)[] {(64,0x0B,false)}),"Startup profile restoration reads current hardware mode and skips redundant write when saved mode is already active");
    io.NitroCalls.Clear();
    Check(controller.RestoreSavedPowerMode(4).Confirmed && controller.Capabilities.ActivePowerMode==4 && io.NitroCalls.SequenceEqual(new (ushort,ulong,bool)[] {(64,0x0B,false),(30,4,false),(64,0x0B,false)}),"Startup profile restoration applies verified saved Nitro mode when hardware is in a different mode");
    io.NitroCalls.Clear();
    Check(!controller.RestoreSavedPowerMode(5).Accepted && io.NitroCalls.SequenceEqual(new (ushort,ulong,bool)[] {(64,0x0B,false)}),"Startup profile restoration rejects unsupported saved mode without sending a write");
    io.IsOnAc = false; io.NitroCalls.Clear();
    Check(!controller.RestoreSavedPowerMode(4).Accepted && io.NitroCalls.SequenceEqual(new (ushort,ulong,bool)[] {(64,0x0B,false)}),"Startup profile restoration does not apply AC-only saved mode when starting on battery");
}
using (var io = new FakeIo())
using (var controller = new WmiController(predator, io))
{
    controller.SetPowerMode(4);
    Check(io.Writes.SequenceEqual([("SetGamingProfile", 4UL)]) && io.NitroCalls.Count==0,"Predator backend isolation: Predator uses SetGamingProfile and never sends NitroSense SetOperationMode or SetGamingMiscSetting(0x0B)");
}
Console.WriteLine($"{passed} compatibility checks passed. All identities, services, sensors and command outcomes were synthetic. No hardware accessed.");

sealed class FakeKeyboard : IKeyboardFirmware
{
    public uint Configuration = 0x1E64;
    public int Writes;
    public bool TryRead(out uint configuration) { configuration = Configuration; return true; }
    public bool TryWrite(uint configuration) { Writes++; Configuration = configuration; return true; }
}
sealed class FakeIo : IHardwareTestIo, IDisposable
{
    public bool IsOnAc { get; set; } = true;
    public bool Succeed = true;
    public int FailAtWrite = -1;
    public List<(string, ulong)> Writes = [];
    public Dictionary<ulong, int?> Sensors = [];
    public int LightingWrites, BatteryWrites, GpuWrites, WindowsPowerWrites, WindowsPlanWrites;
    public bool? BatteryReadback;
    public FakeKeyboard KeyboardIo = new();
    public IKeyboardFirmware Keyboard => KeyboardIo;
    public int TotalWrites => Writes.Count + LightingWrites + BatteryWrites + GpuWrites + WindowsPowerWrites + WindowsPlanWrites + KeyboardIo.Writes + NitroCalls.Count(c => c.Command is 15 or 16 or 17 or 30 or 63);
    public HardwareOperationResult SetWindowsPowerMode(byte mode) { WindowsPowerWrites++; return new(Succeed ? "confirmed" : "failed", "Synthetic Windows power result; no OS API called."); }
    public WindowsPowerPlanDiscovery PlanInventory = WindowsPowerPlans.Describe([WindowsPowerPlans.PowerSaver,WindowsPowerPlans.Balanced,WindowsPowerPlans.HighPerformance],null,WindowsPowerPlans.Balanced);
    public Guid? ActivePlan = WindowsPowerPlans.Balanced;
    public bool PlanReadbackUnavailable, PlanReadbackMismatch;
    public HardwareOperationResult SetWindowsPowerPlan(Guid scheme) => WindowsPowerPlans.Apply(scheme, PlanInventory, id =>
    {
        WindowsPlanWrites++;
        if (!Succeed) return 5;
        if (!PlanReadbackMismatch) ActivePlan = id;
        return 0;
    }, () => PlanReadbackUnavailable ? null : ActivePlan);
    public Guid? ReadActiveWindowsPowerPlan() => PlanReadbackUnavailable ? null : ActivePlan;
    public bool NitroState, NitroReadUnavailable, NitroOperationModeReadMismatch;
    public byte NitroOperationModeState = NitroSense3Protocol.ModeDefault;
    public List<(ushort Command, ulong Input, bool Wide)> NitroCalls = [];
    public ulong? SendNitroSense(ushort command, ulong input, bool wide)
    {
        NitroCalls.Add((command, input, wide));
        if (!Succeed) return 1;
        if (command == 17) { NitroState = input == 0x10007; return 0; }
        if (command == 20) return NitroReadUnavailable ? null : NitroState ? 256UL : 0UL;
        if (command == 30)
        {
            if (!NitroOperationModeReadMismatch) NitroOperationModeState = (byte)input;
            return 0;
        }
        if (command == 63)
        {
            if (!NitroOperationModeReadMismatch && (input & 0xFF) == 0x0B)
                NitroOperationModeState = (byte)((input >> 8) & 0xFF);
            return 0;
        }
        if (command == 64 && input == 0x0B)
            return NitroReadUnavailable ? null : ((ulong)NitroOperationModeState << 8);
        return NitroReadUnavailable ? null : 0UL;
    }
    public (bool, ulong) SendWmi(string method, ulong input)
    {
        Writes.Add((method, input));
        bool ok = Succeed && Writes.Count != FailAtWrite;
        if (ok && method == "SetGamingMiscSetting" && (input & 0xFF) == 0x0B && !NitroOperationModeReadMismatch)
            NitroOperationModeState = (byte)((input >> 8) & 0xFF);
        return (ok, ok ? 0UL : 1UL);
    }
    public ulong? ReadGamingMiscSetting(uint input) =>
        NitroReadUnavailable || input != 0x0B ? null : ((ulong)NitroOperationModeState << 8);
    public bool SendLighting(byte[] payload) { LightingWrites++; return Succeed; }
    public int? ReadSensor(ulong sensor) => Sensors.GetValueOrDefault(sensor);
    public bool SetBatteryLimit(bool enabled) { BatteryWrites++; return Succeed; }
    public bool? ReadBatteryLimit() => BatteryReadback;
    public bool SetGpuMode(int mode) { GpuWrites++; return Succeed; }
    public void Dispose() { }
}
sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], token);
}
