using PredatorControlApp;

internal static class ProtocolCapabilityChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const string modesReply = "{\"request\":\"SUPPORT_OPERATING_MODE_CAPABILITY\",\"data\":[0,1,4]}";
        var modes = VerifiedAcerProtocol.ParseModes(modesReply);
        check(modes.Select(m => m.Value).SequenceEqual(new byte[] { 0, 1, 4 }) && !modes.Any(m => m.Value == 5), "Agent enumerates only advertised modes without inventing Turbo");
        check(modes.First(m => m.Value == 0).OnBattery == false && modes.First(m => m.Value == 1).OnBattery, "Agent power-source policy follows production OEM filtering");
        foreach (string invalid in new[] { "{}", "{", "[]", "{\"request\":\"wrong\",\"data\":[1]}",
            "{\"request\":\"SUPPORT_OPERATING_MODE_CAPABILITY\",\"data\":[1,1]}",
            "{\"request\":\"SUPPORT_OPERATING_MODE_CAPABILITY\",\"data\":[1,2]}",
            "{\"request\":\"SUPPORT_OPERATING_MODE_CAPABILITY\",\"data\":[\"1\"]}",
            "{\"request\":\"SUPPORT_OPERATING_MODE_CAPABILITY\",\"data\":[1],\"result\":1}",
            "{\"request\":\"SUPPORT_OPERATING_MODE_CAPABILITY\",\"data\":[1],\"data\":[4]}" })
            check(VerifiedAcerProtocol.ParseModes(invalid).Count == 0, "Malformed, ambiguous or foreign capability response fails closed: " + invalid);
        check(VerifiedAcerProtocol.ParseCurrentMode("{\"request\":\"OPERATING_MODE\",\"data\":{\"mode\":4}}") == 4 &&
            VerifiedAcerProtocol.ParseCurrentMode("{\"request\":\"OPERATING_MODE\",\"data\":{\"mode\":2}}") == null, "Current mode requires matching response and reviewed enum");
        check(VerifiedAcerProtocol.ParseStatus("{\"request\":\"ADAPTOR_STATUS\",\"data\":{\"status\":2}}", "ADAPTOR_STATUS") == true &&
            VerifiedAcerProtocol.ParseStatus("{\"request\":\"ADAPTOR_STATUS\",\"data\":{\"status\":4}}", "ADAPTOR_STATUS") == false &&
            VerifiedAcerProtocol.ParseStatus("{\"request\":\"ADAPTOR_STATUS\",\"data\":{\"status\":255}}", "ADAPTOR_STATUS") == null, "Adaptor readiness distinguishes full power, USB-PD and unknown bits");
        var protocol = new ProtocolEvidence(VerifiedAcerProtocol.Family, "AASSvc", VerifiedAcerProtocol.AgentSha256, modes, 1, BatteryBoostReady: true, AdaptorReady: true);
        var service = new ServiceEvidence("AASSvc", true, true, true, true, "1.2.2111.0", VerifiedAcerProtocol.CentralSha256, 100, VerifiedAcerProtocol.AgentSha256);
        HardwareDiscovery Fixture(string model) => new()
        {
            Identity = new() { Manufacturer = "Acer", Model = model },
            Services = new Dictionary<string, ServiceEvidence> { [service.Name] = service }, Protocols = [protocol]
        };
        foreach (string model in new[] { "PHN16-71", "PHN16-72", "Unlisted Acer notebook" })
        {
            var caps = DeviceProfileResolver.Resolve(Fixture(model));
            check(caps.ProtocolId == VerifiedAcerProtocol.Family && caps.Results[HardwareFeature.Power].Backend == VerifiedAcerProtocol.PowerBackend && caps.AllowsPower(4, true),
                "Reviewed live protocol selects the same backend without a new model entry: " + model);
            check(!caps.AllowsPower(0, false) && !caps.AllowsPower(4, false) && caps.AllowsPower(1, false), "DC policy remains narrow: " + model);
        }
        var unknown = Fixture("Unlisted Acer notebook");
        var known = DeviceProfileResolver.Resolve(unknown);
        using (var readOnlyIo = new FakeIo { ManualFanActiveFlag = true })
        {
            using (var controller = new WmiController(Fixture("PHN16-71"), readOnlyIo, readOnly: true))
            {
                check(!controller.SetPowerMode(1).Accepted && controller.LastOperation?.Code == "read-only-diagnostics" && readOnlyIo.TotalWrites == 0,
                    "Read-only diagnostic controller cannot dispatch a supported hardware operation");
            }
            check(readOnlyIo.TotalWrites == 0 && readOnlyIo.ManualFanActiveFlag, "Diagnostic disposal cannot replay persisted automatic-fan recovery");
        }
        check(NitroSense3Protocol.CmdWmiSetGamingMiscSetting == 33 && NitroSense3Protocol.CmdWmiGetGamingMiscSetting == 34,
            "Actual NitroSense3 WMIFunction and ServiceCommand metadata establish misc opcodes 33/34");
        using (var io = new FakeIo())
        using (var controller = new WmiController(unknown, io))
        {
            check(controller.SetPowerMode(4).Confirmed && io.AgentWrites == 1 && io.Writes.Count == 0 && io.NitroCalls.Count == 0,
                "Unlisted model sends one command through the reviewed OEM backend with no alternate WMI writes");
            check(!controller.SetPowerMode(5).Accepted && io.AgentWrites == 1, "Unadvertised Turbo never reaches the service");
            controller.InvalidateCompatibility();
            check(!controller.Capabilities.IsCurrent && !controller.SetPowerMode(1).Accepted && io.AgentWrites == 1, "Suspend/interface invalidation revokes an existing controller immediately");
            var restored = controller.RefreshCompatibility();
            check(restored.IsCurrent && restored.Revision > 0 && io.AgentWrites == 1, "Compatibility refresh restores grants from fresh discovery without replaying writes");
            io.LiveService = service with { ProcessId = 101 };
            check(!controller.SetPowerMode(1).Accepted && controller.LastOperation?.Code == "service-unavailable" && io.AgentWrites == 1,
                "Service restart between discovery and dispatch revokes the old grant");
        }
        using (var io = new FakeIo())
        using (var controller = new WmiController(unknown with { Protocols = [protocol with { Fans = [new(0, "CPU", 0, 100)] }] }, io))
        {
            check(controller.SetFanBehavior(1).Confirmed && controller.SetFanBehavior(2).Confirmed && io.AgentFanWrites == 2 && io.Writes.Count == 0,
                "Auto and Max use only the selected service fan protocol");
            check(!controller.Capabilities.CanWrite(HardwareFeature.FanManual) && !controller.SetFanBehavior(3).Accepted && io.AgentFanWrites == 2,
                "A single fan channel cannot be mapped to the existing CPU/GPU custom-speed controls");
        }
        check(!known.CanWrite(HardwareFeature.Rgb) && !known.CanWrite(HardwareFeature.FanManual), "Operating-mode evidence never grants unrelated lighting or cooling commands");
        foreach (var badService in new[] { service with { Trusted = false }, service with { Running = false }, service with { InterfaceWorking = false },
            service with { BinarySha256 = new string('A', 64) }, service with { ProtocolBinarySha256 = new string('B', 64) } })
            check(!DeviceProfileResolver.Resolve(unknown with { Services = new Dictionary<string, ServiceEvidence> { [service.Name] = badService } }).CanWrite(HardwareFeature.Power),
                "Missing, foreign or untrusted service cannot identify a protocol");
        check(!DeviceProfileResolver.Resolve(unknown with { Protocols = [protocol with { BinarySha256 = new string('C', 64) }] }).CanWrite(HardwareFeature.Power), "An unreviewed worker hash cannot grant writes");
        check(!DeviceProfileResolver.Resolve(unknown with { Protocols = [protocol, protocol] }).CanWrite(HardwareFeature.Power), "Ambiguous protocol observations fail closed");
        check(!DeviceProfileResolver.Resolve(unknown with { Protocols = [protocol with { CurrentPowerMode = 5 }] }).CanWrite(HardwareFeature.Power), "Current and supported modes must agree");
        check(!DeviceProfileResolver.Resolve(unknown with { Identity = unknown.Identity with { Manufacturer = "Other" } }).CanWrite(HardwareFeature.Power), "Service fingerprint does not authorize a non-Acer device");
        var namesOnly = unknown with { Protocols = [], Methods = new HashSet<string> { "SetGamingProfile", "SetGamingFanBehavior", "SetGamingMiscSetting" } };
        check(!DeviceProfileResolver.Resolve(namesOnly).Results.Values.Any(v => v.CanWrite), "Similarly named WMI methods never identify the reviewed protocol");
        var lowPower = DeviceProfileResolver.Resolve(unknown with { Protocols = [protocol with { BatteryBoostReady = false }] });
        check(!lowPower.AllowsPower(4, true) && lowPower.AllowsPower(0, true) && lowPower.AllowsPower(1, true) && lowPower.PowerPlanNotice != null, "Insufficient firmware power disables high modes while preserving Quiet and Balanced");
        var missingBoost = DeviceProfileResolver.Resolve(unknown with { Protocols = [protocol with { BatteryBoostReady = null, AdaptorReady = true }] });
        check(missingBoost.AllowsPower(4, true) && missingBoost.AllowsPower(0, true), "Omitted BATTERY_BOOST telemetry on AC does not block performance or quiet modes");
        var missingPower = DeviceProfileResolver.Resolve(unknown with { Protocols = [protocol with { AdaptorReady = null }] });
        check(!missingPower.AllowsPower(4, true) && missingPower.AllowsPower(0, true), "Missing adaptor status never enables high modes");

        const string fansReply = "{\"request\":\"SUPPORT_FAN_CONTROL\",\"data\":[{\"fan_index\":0,\"fan_name\":\"CPU\",\"fan_custom_min_speed\":0,\"fan_custom_max_speed\":100},{\"fan_index\":1,\"fan_name\":\"GPU\",\"fan_custom_min_speed\":10,\"fan_custom_max_speed\":100}]}";
        var fans = VerifiedAcerProtocol.ParseFans(fansReply);
        var profiledLiveFans = DeviceProfileResolver.Resolve(Fixture("PHN16-71") with { Protocols = [protocol with { Fans = fans }] });
        check(profiledLiveFans.Results[HardwareFeature.FanManual].Backend == VerifiedAcerProtocol.FanBackend &&
            profiledLiveFans.Cooling.ManualSupported && profiledLiveFans.Results[HardwareFeature.Power].Backend == VerifiedAcerProtocol.PowerBackend,
            "Live protocol capabilities override model-level fan gating on an exact-model profile");
        const string fanState = "{\"request\":\"FAN_CONTROL\",\"data\":{\"mode\":0,\"custom_fan_data\":[{\"fan_index\":0,\"fan_name\":\"CPU\",\"fan_custom_speed\":50,\"fan_custom_auto\":1},{\"fan_index\":1,\"fan_name\":\"GPU\",\"fan_custom_speed\":60,\"fan_custom_auto\":1}]}}";
        check(fans.Count == 2 && VerifiedAcerProtocol.ParseFanState(fanState, fans)?.Mode == 0, "Fan support and current state agree before selection");
        check(VerifiedAcerProtocol.TryGetCpuGpuFans(fans, out var rangedCpu, out var rangedGpu) &&
            VerifiedAcerProtocol.TryGetCustomSliderRange(rangedCpu, out int cpuSliderMin, out int cpuSliderMax) && cpuSliderMin == 10 && cpuSliderMax == 100 &&
            VerifiedAcerProtocol.TryGetCustomSliderRange(rangedGpu, out int gpuSliderMin, out int gpuSliderMax) && gpuSliderMin == 10 && gpuSliderMax == 100,
            "Custom speed sliders retain the safety floor and use live per-fan maximums");
        var narrowFans = VerifiedAcerProtocol.ParseFans(fansReply.Replace("\"fan_custom_min_speed\":0", "\"fan_custom_min_speed\":25")
            .Replace("\"fan_custom_min_speed\":10", "\"fan_custom_min_speed\":25")
            .Replace("\"fan_custom_max_speed\":100", "\"fan_custom_max_speed\":60"));
        check(VerifiedAcerProtocol.TryGetCpuGpuFans(narrowFans, out var narrowCpu, out var narrowGpu) &&
            VerifiedAcerProtocol.TryGetCustomSliderRange(narrowCpu, out int narrowCpuMin, out int narrowCpuMax) && narrowCpuMin == 25 && narrowCpuMax == 60 &&
            VerifiedAcerProtocol.TryGetCustomSliderRange(narrowGpu, out int narrowGpuMin, out int narrowGpuMax) && narrowGpuMin == 25 && narrowGpuMax == 60,
            "Narrow live custom-speed ranges adapt each slider to OEM limits");
        var tooNarrowFans = VerifiedAcerProtocol.ParseFans(fansReply.Replace("\"fan_custom_min_speed\":10", "\"fan_custom_min_speed\":0")
            .Replace("\"fan_custom_max_speed\":100", "\"fan_custom_max_speed\":5"));
        var tooNarrowCaps = DeviceProfileResolver.Resolve(unknown with { Protocols = [protocol with { Fans = tooNarrowFans }] });
        check(tooNarrowFans.Count == 2 && !tooNarrowCaps.Cooling.ManualSupported,
            "An OEM range below the existing slider's safe minimum cannot enable custom mode");
        check(VerifiedAcerProtocol.ParseFanState(fanState.Replace("\"fan_name\":\"GPU\"", "\"fan_name\":\"OTHER\""), fans) == null &&
            VerifiedAcerProtocol.ParseFanState(fanState.Replace("\"fan_custom_speed\":60", "\"fan_custom_speed\":255"), fans) == null, "Duplicate fan channels and out-of-range firmware state are rejected");
        var compositeFans = VerifiedAcerProtocol.ParseFans(fansReply.Replace("\"fan_index\":1", "\"fan_index\":0"));
        check(compositeFans.Count == 2 && VerifiedAcerProtocol.ParseFanState(fanState.Replace("\"fan_index\":1", "\"fan_index\":0"), compositeFans).HasValue,
            "OEM CPU0 and GPU0 are distinct fan channels with the same numeric index");
        check(VerifiedAcerProtocol.TryGetCpuGpuFans(compositeFans, out var compositeCpu, out var compositeGpu) && compositeCpu.Index == compositeGpu.Index,
            "Dynamic custom-speed mapping retains composite channel identity when CPU and GPU share a numeric index");
        check(VerifiedAcerProtocol.ParseFanState(fanState.Replace("\"fan_index\":1,\"fan_name\":\"GPU\"", "\"fan_index\":0,\"fan_name\":\"CPU\""), fans) == null,
            "Duplicate composite fan channel cannot authorize a mode change");
        var cooling = DeviceProfileResolver.Resolve(unknown with { Protocols = [protocol with { Fans = fans }] });
        check(cooling.Cooling.FanCount == 2 && cooling.Cooling.AutoSupported && cooling.Cooling.MaxSupported &&
            cooling.Cooling.ManualSupported && !cooling.Cooling.FanCurveSupported && !cooling.Cooling.CoolBoostSupported,
            "Reviewed CPU/GPU channels enable Auto/Max/Custom RPM while hiding unsupported curves and CoolBoost");
        using (var io = new FakeIo { LiveService = service })
        using (var controller = new WmiController(unknown with { Protocols = [protocol with { Fans = fans }] }, io))
        {
            controller.ObserveTelemetry(new TelemetrySnapshot(90, 88, null, null, PowerLineStatus.Online));
            check(controller.SetFanBehavior(3).Confirmed && io.AgentFanCalls.Last() == (2, (byte?)85, (byte?)85),
                "Dynamic custom fan mode dispatches only for the reported CPU/GPU channels and applies the thermal floor");
            check(controller.SetFanSpeed(20, 30) && io.AgentFanCalls.Last() == (2, (byte?)85, (byte?)85),
                "Custom RPM updates remain above the live thermal safety floor");
            controller.ObserveTelemetry(new TelemetrySnapshot(null, null, null, null, PowerLineStatus.Online));
            check(!controller.SetFanSpeed(40, 40) && io.AgentFanCalls.Last().Mode == 0,
                "Missing thermal readings reject manual fan writes and recover to firmware Auto");
        }
        check(VerifiedAcerProtocol.ParseKeyboard("{\"request\":\"SUPPORT_KEYBOARD_INFO\",\"data\":{\"keyboard_backlight_color_control\":2,\"keyboard_led_type\":4}}") is { LightingType: "rgb", ZoneCount: 4, PerKey: false }, "OEM keyboard metadata identifies four RGB zones");
        var metadata = DeviceProfileResolver.Resolve(unknown with { DetectedKeyboard = new("rgb", 4, false) });
        check(metadata.Keyboard.Kind == KeyboardKind.Unknown && !metadata.CanWrite(HardwareFeature.Rgb) && metadata.Keyboard.Evidence.Contains("not reviewed"), "Keyboard metadata cannot authorize an unavailable lighting backend");
        var conflictingKeyboard = Fixture("PHN16-71") with
        {
            Methods = new HashSet<string> { "SetGamingKBBacklight", "SetGamingRgbKb" },
            KeyboardConfigurationReadable = true, DetectedKeyboard = new("fixed-color", 1, false)
        };
        var conflictCaps = DeviceProfileResolver.Resolve(conflictingKeyboard);
        check(!conflictCaps.CanWrite(HardwareFeature.Rgb) && !conflictCaps.CanWrite(HardwareFeature.KeyboardPower), "Live controller contradiction revokes an inherited model lighting profile");

        int writes = 0;
        var staleGate = new HardwareWriteGate(() => known with { IsCurrent = false });
        var stale = staleGate.Execute(HardwareFeature.Power, true, () => { writes++; return new("confirmed", "fixture"); });
        check(stale.Code == "stale-capabilities" && stale.Feature == HardwareFeature.Power && writes == 0, "Stale snapshots revoke writes with a specific feature diagnostic");
        var invalidGate = new HardwareWriteGate(() => known, _ => true);
        var releaseDetached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool>? detached = null;
        invalidGate.Execute(HardwareFeature.Power, true, () =>
        {
            detached = Task.Run(async () => { await releaseDetached.Task; return HardwareWriteGate.InScope(HardwareFeature.Power, VerifiedAcerProtocol.PowerBackend); });
            return new("unverified", "fixture");
        });
        releaseDetached.SetResult();
        check(!detached!.GetAwaiter().GetResult(), "Detached async work cannot retain authorization after the command scope ends");
        var revocable = known;
        var revocableGate = new HardwareWriteGate(() => revocable, _ => true);
        revocableGate.Execute(HardwareFeature.Power, true, () =>
        {
            check(HardwareWriteGate.InScope(HardwareFeature.Power, VerifiedAcerProtocol.PowerBackend), "Current command scope starts authorized");
            revocable = revocable with { IsCurrent = false };
            check(!HardwareWriteGate.InScope(HardwareFeature.Power, VerifiedAcerProtocol.PowerBackend), "Invalidation during a read preflight revokes the pending transport scope");
            revocable = known with { Revision = known.Revision + 1 };
            check(!HardwareWriteGate.InScope(HardwareFeature.Power, VerifiedAcerProtocol.PowerBackend), "A changed capability revision cannot inherit an in-flight command scope");
            return new("unverified", "fixture");
        });
        var payload = invalidGate.Execute(HardwareFeature.Power, true, () =>
        {
            check(VerifiedAcerProtocol.AuthorizedWritePayload("{\"Function\":\"OPERATING_MODE\",\"Parameter\":{\"mode\":4}}"), "Reviewed power scope permits the exact structured payload");
            check(!VerifiedAcerProtocol.AuthorizedWritePayload("{\"Function\":\"GPU_MODE\",\"Parameter\":{\"mode\":4}}") &&
                !VerifiedAcerProtocol.AuthorizedWritePayload("{\"Function\":\"OPERATING_MODE\",\"Parameter\":{\"mode\":4,\"voltage\":1000}}"), "Reviewed power scope rejects unrelated functions and speculative parameters");
            return new("confirmed", "fixture");
        });
        var manualFans = new HardwareWriteGate(() => cooling, _ => true).Execute(HardwareFeature.FanManual, true, () =>
        {
            const string valid = "{\"Function\":\"FAN_CONTROL\",\"Parameter\":{\"mode\":2,\"custom_fan_data\":[{\"fan_index\":0,\"fan_name\":\"CPU\",\"fan_custom_speed\":70,\"fan_custom_auto\":0},{\"fan_index\":1,\"fan_name\":\"GPU\",\"fan_custom_speed\":75,\"fan_custom_auto\":0}]}}";
            const string injected = "{\"Function\":\"FAN_CONTROL\",\"Parameter\":{\"mode\":2,\"custom_fan_data\":[{\"fan_index\":9,\"fan_name\":\"CPU\",\"fan_custom_speed\":250,\"fan_custom_auto\":0}]}}";
            check(VerifiedAcerProtocol.AuthorizedWritePayload(valid), "Reviewed fan scope permits the bounded custom-speed payload");
            check(!VerifiedAcerProtocol.AuthorizedWritePayload(injected), "Reviewed fan scope rejects out-of-range or unrecognized channel payloads");
            return new("confirmed", "fixture");
        });
        check(invalidGate.Execute(HardwareFeature.Power, false, () => throw new Exception()).Code == "invalid-parameter", "Invalid parameters have a distinct diagnostic");
        var unknownGate = new HardwareWriteGate(() => DeviceProfileResolver.Resolve(namesOnly));
        check(unknownGate.Execute(HardwareFeature.Power, true, () => throw new Exception()).Code == "unknown-protocol", "Unknown protocol has a distinct diagnostic");
        check(unknownGate.Execute(HardwareFeature.KeyboardPower, true, () => throw new Exception()).Code == "unsupported-keyboard-controller", "Unknown keyboard has a distinct diagnostic");
        var serviceGate = new HardwareWriteGate(() => known, _ => false);
        check(serviceGate.Execute(HardwareFeature.Power, true, () => throw new Exception()).Code == "service-unavailable", "Service loss cannot dispatch and has a specific diagnostic");
        check(!VerifiedAcerProtocol.ReviewedAgent("CC69F8E6DD7FC8C59F68B6900D1CA25BE31444966A376E4E5D6879A09BE77F35"), "Historical vulnerable PredatorSense 5.0 worker is not allowlisted");
    }
}
