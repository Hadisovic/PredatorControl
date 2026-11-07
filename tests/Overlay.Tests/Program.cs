using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using PredatorControlApp;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        int passed = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            Console.WriteLine("PASS " + name);
            passed++;
        }
        T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
        void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

        Check(OverlayPolicy.IsFresh(1000, 4000, 1000), "Telemetry expires after three seconds");
        Check(!OverlayPolicy.IsFresh(1000, 4001, 1000), "Expired telemetry is unavailable");
        Check(!OverlayPolicy.IsFresh(5000, 4000, 1000), "Future timestamps cannot authorize fresh readings");
        Check(!OverlayPolicy.IsFresh(0, 1000, 1000), "Missing monotonic timestamps are not measurements");
        Check(OverlayPolicy.MemoryPercent(null, 8) == null && OverlayPolicy.MemoryPercent(0, null) == null, "Missing memory readings do not invent usage or capacity");
        Check(OverlayPolicy.MemoryPercent(0, 8) == 0 && OverlayPolicy.MemoryPercent(4, 8) == 50, "Real zero and measured memory percentages are retained");
        Check(OverlayPolicy.MemoryPercent(9, 8) == null && OverlayPolicy.MemoryPercent(float.NaN, 8) == null && OverlayPolicy.MemoryPercent(1, float.PositiveInfinity) == null, "Malformed provider memory is unavailable");
        Check(OverlayPolicy.ClockMhz(3200) == 3200 && OverlayPolicy.ClockMhz(null) == null, "Real clock readings are retained and absent clocks stay unavailable");
        Check(OverlayPolicy.ClockMhz(0) == null && OverlayPolicy.ClockMhz(99) == null && OverlayPolicy.ClockMhz(10001) == null, "Malformed clock readings are rejected");
        foreach (int dpi in new[] { 96, 120, 144, 192 })
            foreach (int percent in new[] { 50, 100, 200, 300 })
                Check(Math.Abs(OverlayPolicy.Scale(dpi, percent) - dpi / 96f * percent / 100f) < .001, $"DPI {dpi} and scale {percent}% multiply once");
        var secondary = new Rectangle(-1920, -1080, 1920, 1080);
        Check(OverlayPolicy.ClampPosition(new(5000, 5000), new(400, 60), secondary) == new Point(-400, -60), "Monitor removal and negative desktop coordinates clamp correctly");
        Check(OverlayPolicy.ClampPosition(new(-5000, -5000), new(4000, 2000), secondary) == secondary.Location, "Oversized overlay clamp never throws");

        long[] frames = Enumerable.Range(0, 61).Select(i => 60000L + i * 1000L).ToArray();
        Check(Math.Abs(OverlayPolicy.SampleFps(frames, 120000, 60000) - 60) < .01, "Measured 60Hz presents produce 60 FPS");
        Check(OverlayPolicy.SampleFps(frames, 240000, 60000) == 0, "Game exit cannot retain the last FPS");
        Check(OverlayPolicy.SampleFps(new long[] { 1000, 1000, 2000 }, 2000, 1000) == 1, "Duplicate timestamps cannot inflate FPS");
        Check(OverlayPolicy.SampleFps(new long[] { 1000, 2000, 4000 }, 2500, 1000) == 0, "Future frames are rejected");
        Check(NvmlGpuMonitor.NeedsReconnect(15) && NvmlGpuMonitor.NeedsReconnect(1) && !NvmlGpuMonitor.NeedsReconnect(3), "GPU reset reconnects while unsupported channels stay missing");
        using (var fps = new EtwFpsMonitor())
        {
            long now = Stopwatch.GetTimestamp(), freq = Stopwatch.Frequency;
            var ring = Field<long[]>(fps, "_frameTimes");
            for (int i = 0; i < 61; i++) ring[i] = now - freq + i * freq / 60;
            Set(fps, "_framesFilled", 61);
            Set(fps, "_frameHead", 61);
            Check(fps.SampleFps() is > 59 and < 61, "Production FPS sampler reads a synthetic ring without starting ETW");
            fps.TargetPid = 42;
            Check(fps.SampleFps() == 0, "Foreground process changes clear the previous game's ring");
        }

        // Exercise actual native rendering without ShowOverlay(), ETW startup, application startup,
        // persisted-setting writes, services, or hardware commands.
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        using var overlay = new GameOverlayForm();
        using (var settings = new OverlaySettingsForm(overlay, _ => { }))
        {
            var cpuClockOption = Field<System.Windows.Forms.Control>(settings, "_chkCpuClock");
            var gpuClockOption = Field<System.Windows.Forms.Control>(settings, "_chkGpuClock");
            var overlayOption = Field<System.Windows.Forms.Control>(settings, "_chkOverlayMaster");
            Check(cpuClockOption.Text == "CPU clock" && gpuClockOption.Text == "GPU clock", "Clock options are present in Overlay Settings");
            Check(overlayOption.Bottom <= settings.ClientSize.Height, "Expanded clock options keep the existing settings layout in bounds");
        }
        overlay.Show();
        foreach (var mode in new[] { "light", "default", "full", "complete" })
        {
            overlay.Configure(new JelliSettings { OverlayMode = mode, OverlayScale = 100 });
            overlay.UpdateSnapshot(new TelemetrySnapshot(65, 70, 0, 4800, PowerLineStatus.Online,
                CpuUsage: 37, GpuUsage: 92, GpuPowerW: 120, VramUsedGb: 4, VramTotalGb: 8,
                RamUsedGb: 12, RamTotalGb: 32, SampledAtTimestamp: Stopwatch.GetTimestamp()));
            using var bitmap = new Bitmap(overlay.Width, overlay.Height);
            overlay.DrawToBitmap(bitmap, overlay.ClientRectangle);
            string? renderDir = Environment.GetEnvironmentVariable("PREDATORCONTROL_OVERLAY_PREVIEW_DIR");
            if (!string.IsNullOrWhiteSpace(renderDir))
            {
                Directory.CreateDirectory(renderDir);
                bitmap.Save(Path.Combine(renderDir, mode + ".png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            Check(overlay.Width > 100 && overlay.Height > 20, "Native " + mode + " layout renders");
        }
        Check(overlay.Mode == OverlayMode.Complete && overlay.ShowRam, "Complete configuration applies its memory preset");
        overlay.ShowCpuClock = false;
        overlay.ShowGpuClock = false;
        overlay.RefreshLayout();
        int widthWithoutClocks = overlay.Width;
        overlay.ShowCpuClock = true;
        overlay.ShowGpuClock = true;
        overlay.RefreshLayout();
        Check(overlay.Width > widthWithoutClocks, "Optional CPU/GPU clocks reserve layout space only when enabled");
        overlay.UpdateSnapshot(new TelemetrySnapshot(65, 70, null, null, PowerLineStatus.Online,
            SampledAtTimestamp: Stopwatch.GetTimestamp(), CpuClockMhz: 4100, GpuClockMhz: 1800));
        Check(Field<int?>(overlay, "_cpuClockMhz") == 4100 && Field<int?>(overlay, "_gpuClockMhz") == 1800, "Fresh provider clocks reach the existing two sensor rows");
        using (var clockBitmap = new Bitmap(overlay.Width, overlay.Height))
            overlay.DrawToBitmap(clockBitmap, overlay.ClientRectangle);
        overlay.UpdateSnapshot(new TelemetrySnapshot(65, 70, null, null, PowerLineStatus.Online,
            SampledAtTimestamp: Stopwatch.GetTimestamp(), CpuClockMhz: 0, GpuClockMhz: 12000));
        Check(Field<int?>(overlay, "_cpuClockMhz") == null && Field<int?>(overlay, "_gpuClockMhz") == null, "Invalid clocks are cleared instead of rendered");
        overlay.UpdateSnapshot(new TelemetrySnapshot(null, null, null, null, PowerLineStatus.Unknown, SampledAtTimestamp: Stopwatch.GetTimestamp()));
        Check(Field<int?>(overlay, "_cpuTemp") == null && Field<float?>(overlay, "_vramUsedGb") == null && Field<int?>(overlay, "_gpuClockMhz") == null, "Provider failure clears displayed measurements");
        overlay.UpdateSnapshot(new TelemetrySnapshot(65, 70, 1000, 1000, PowerLineStatus.Online,
            SampledAtTimestamp: Stopwatch.GetTimestamp() - 4 * Stopwatch.Frequency));
        Check(Field<int?>(overlay, "_cpuTemp") == null, "Delayed telemetry is rejected on arrival");
        Set(overlay, "_hasCustomPosition", true);
        overlay.Location = new Point(100, 100);
        overlay.Configure(new JelliSettings { OverlayMode = "complete", OverlayScale = 100 });
        Check(overlay.Location == new Point(100, 100), "Unrelated overlay configuration preserves a dragged position");
        overlay.Configure(new JelliSettings { OverlayMode = "complete", OverlayScale = 300 });
        Check(overlay.ScalePercent == 300 && overlay.Width <= Screen.FromControl(overlay).WorkingArea.Width, "300% overlay fits the current monitor");
        Check(Field<Task?>(overlay, "_fpsWorker") == null, "Layout and rendering never start an FPS trace");
        overlay.Hide();
        Console.WriteLine($"{passed} overlay checks passed.");
    }
}
