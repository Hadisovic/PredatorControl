using PredatorControlApp;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

// Test fixtures never create Form1, WmiController, AcerAgentClient or ETW sessions.
// This executable uses the production WebView host and real imported renderer.
internal static class Program
{
    private static int _checks;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); _checks++; Console.WriteLine("PASS " + message); }
    [STAThread]
    static int Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        SelfCheck.Run(); // Existing hardware-free assertions are included by Debug builds.
        Check(JelliHostForm.Clamp(new(-3000, 2000), new(430, 600), new(-1920, 0, 1920, 1040)) == new Point(-1920, 440), "clamp negative monitor coordinates");
        Check(JelliHostForm.Clamp(new(100, 100), new(430, 600), new(0, 0, 320, 240)) == Point.Empty, "clamp oversized panel");
        Check(!JelliSettings.Load().SyncRgb || JelliSettings.Metrics.Length == 10, "settings readable");
        Check(new JelliSettings().Presentation == "expand" && !new JelliSettings().SyncRgb, "safe defaults");
        try { JelliSettings.Validate(new() { Metric1 = "invalid" }); throw new Exception("invalid setting accepted"); } catch (ArgumentException) { _checks++; }
        var backend = new FixtureBackend();
        using var host = new JelliHostForm(backend, persist: false);
        backend.Host = host;
        int result = 0;
        host.Shown += async (_, _) => {
            try {
                await Until(() => host.Ready, 15000);
                await Task.Delay(1000);
                await host.Browser.ExecuteScriptAsync("window.surfaceLog=[];const originalPost=window.chrome.webview.postMessage.bind(window.chrome.webview);window.chrome.webview.postMessage=m=>{if(m.action==='surface')window.surfaceLog.push(m.data);originalPost(m)};window.clickCreature=()=>{document.querySelector('canvas').dispatchEvent(new MouseEvent('mousedown',{button:0,bubbles:true}));document.dispatchEvent(new MouseEvent('mouseup',{button:0,bubbles:true}))};");
                await host.Browser.ExecuteScriptAsync("window.clickCreature()");
                await Task.Delay(100);
                Check(await EvalBool(host, "window.surfaceLog.length === 0"), "single click does not flash summary before arbitration");
                await host.Browser.ExecuteScriptAsync("window.clickCreature()");
                await Task.Delay(600);
                Check(await EvalBool(host, "JSON.stringify(window.surfaceLog) === '[\"dashboard\"]'"), "double click opens only dashboard");
                host.Collapse();
                await Task.Delay(100);
                await host.Browser.ExecuteScriptAsync("window.surfaceLog=[];window.clickCreature()");
                await Task.Delay(600);
                Check(await EvalBool(host, "window.surfaceLog.includes('summary')"), "single click opens quick summary");
                host.Collapse();
                await Task.Delay(100);
                Check(await EvalBool(host, "document.querySelectorAll('.floating-metrics .metric').length === 2"), "exactly two floating metric slots");
                Check(await EvalBool(host, "document.querySelector('canvas').width === 140"), "real canvas mounted");
                await Capture(host, "compact");
                host.OpenDashboard();
                await Task.Delay(300);
                Check(await EvalBool(host, "document.querySelectorAll('nav button').length === 4"), "full dashboard navigation");
                Check(await EvalBool(host, "document.body.textContent.includes('Quiet')"), "backend control descriptors rendered");
                await Capture(host, "dashboard");
                await host.Browser.ExecuteScriptAsync("[...document.querySelectorAll('button')].find(b=>b.textContent==='Quiet').click()");
                await Task.Delay(200);
                Check(backend.LastAction == "power.quiet", "semantic action reaches backend");
                backend.FailNextAction = true;
                await host.Browser.ExecuteScriptAsync("[...document.querySelectorAll('button')].find(b=>b.textContent==='Quiet').click()");
                await Task.Delay(200);
                Check(await EvalBool(host, "document.querySelector('[role=alert]').textContent.includes('Fixture hardware failure')"), "backend errors are visible rather than reported as success");
                await host.Browser.ExecuteScriptAsync("document.querySelector('[aria-label=\"Dismiss error\"]').click()");
                await host.Browser.ExecuteScriptAsync("window.protocolError='';window.chrome.webview.addEventListener('message',e=>{if(e.data.id==='invalid-version')window.protocolError=e.data.error});window.chrome.webview.postMessage({version:99,id:'invalid-version',action:'exit',data:null})");
                await Task.Delay(100);
                Check(await EvalBool(host, "window.protocolError.includes('Unsupported protocol')"), "unsupported protocol versions rejected");
                host.Browser.Navigate("https://example.invalid/");
                await Task.Delay(100);
                Check(host.Browser.Source.StartsWith("https://jelli.predator.local/", StringComparison.Ordinal), "remote navigation blocked");
                await host.Browser.ExecuteScriptAsync("[...document.querySelectorAll('nav button')].find(b=>b.textContent==='Settings').click()");
                await Task.Delay(200);
                Check(await EvalBool(host, "document.body.textContent.includes('Sync keyboard RGB')"), "settings controls available");
                Check(backend.Colors.Count == 0, "RGB sync is off by default");
                await host.Browser.ExecuteScriptAsync("[...document.querySelectorAll('button')].find(b=>b.textContent==='Sync keyboard RGB with Jelli').click()");
                await Task.Delay(850);
                int beforeColors = backend.Colors.Count;
                await host.Browser.ExecuteScriptAsync("for(let i=0;i<20;i++)window.chrome.webview.postMessage({version:1,id:'color-test-'+i,action:'color',data:{r:200+i,g:20,b:30}})");
                await Task.Delay(800);
                Check(backend.Colors.Count - beforeColors <= 2, "RGB color bursts are coalesced and rate limited");
                Check(backend.Colors.Count > 0, "RGB sync reaches only the fixture lighting adapter");
                await host.Browser.ExecuteScriptAsync("[...document.querySelectorAll('button')].find(b=>b.textContent==='Sync keyboard RGB with Jelli').click()");
                await Task.Delay(100);
                int afterStop = backend.Colors.Count;
                await host.Browser.ExecuteScriptAsync("window.chrome.webview.postMessage({version:1,id:'color-stopped',action:'color',data:{r:0,g:255,b:0}})");
                await Task.Delay(800);
                Check(backend.Colors.Count == afterStop && backend.Restores > 0, "disabling RGB sync stops writes and restores manual lighting");
                await host.Browser.ExecuteScriptAsync("[...document.querySelectorAll('button')].find(b=>b.textContent==='Attached flyout').click()");
                await Task.Delay(300);
                Check(await EvalBool(host, "document.querySelector('main').classList.contains('flyout')"), "attached flyout presentation");
                await Capture(host, "flyout");
                host.SetSurface("menu");
                await Task.Delay(200);
                Check(await EvalBool(host, "document.querySelector('[role=menu]') !== null"), "themed menu rendered");
                await Capture(host, "menu");
                host.SetGaming(true);
                await Task.Delay(500);
                Check(!host.Visible, "gaming removes desktop window");
                host.SetGaming(false);
                await Task.Delay(300);
                Check(host.Visible, "desktop returns after gaming");
                host.Collapse();
                await host.Browser.ExecuteScriptAsync("document.dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true}))");
                await Task.Delay(100);
                Check(await EvalBool(host, "document.querySelector('main').classList.contains('compact')"), "Escape collapses surface");
                int rendererId = host.Browser.Environment.GetProcessInfos().First(p => p.Kind == CoreWebView2ProcessKind.Renderer).ProcessId;
                using (var renderer = Process.GetProcessById(rendererId)) renderer.Kill();
                await Task.Delay(200);
                await Until(() => host.Ready, 15000);
                await Task.Delay(300);
                Check(await EvalBool(host, "document.querySelector('canvas') !== null"), "renderer crash recovers and reconnects");
                await Task.Delay(3000);
                var sampleProcesses = host.Browser.Environment.GetProcessInfos().Select(i => Process.GetProcessById(i.ProcessId)).Append(Process.GetCurrentProcess()).ToArray();
                double cpuStart = sampleProcesses.Sum(p => p.TotalProcessorTime.TotalMilliseconds);
                await host.Browser.ExecuteScriptAsync("window.draws=0;const ctx=document.querySelector('canvas').getContext('2d');const clear=ctx.clearRect.bind(ctx);ctx.clearRect=(...a)=>{window.draws++;clear(...a)};window.framesSeen=0; window.frameGaps=[]; window.lastFrame=0; window.measureFrame=t=>{if(window.lastFrame)window.frameGaps.push(t-window.lastFrame);window.lastFrame=t;window.framesSeen++;if(window.framesSeen<600)requestAnimationFrame(window.measureFrame)};requestAnimationFrame(window.measureFrame)");
                await Task.Delay(10000);
                double cpuEnd = sampleProcesses.Sum(p => {p.Refresh(); return p.TotalProcessorTime.TotalMilliseconds;});
                Console.WriteLine($"CPU: {(cpuEnd-cpuStart)/10000*100/Environment.ProcessorCount:F2}% machine; {(cpuEnd-cpuStart)/10000*100:F1}% one core");
                Console.WriteLine("CANVAS DRAWS/10s " + await host.Browser.ExecuteScriptAsync("window.draws"));
                Console.WriteLine("FRAME GAPS " + await host.Browser.ExecuteScriptAsync("JSON.stringify({count:window.framesSeen,mean:window.frameGaps.reduce((a,b)=>a+b,0)/window.frameGaps.length,max:Math.max(...window.frameGaps)})"));
                foreach (var sampleProcess in sampleProcesses) sampleProcess.Dispose();
                var processes = host.Browser.Environment.GetProcessInfos();
                foreach (var procInfo in processes) Console.WriteLine($"PROCESS KIND {procInfo.ProcessId}: {procInfo.Kind}");
                var ids = processes.Select(p => p.ProcessId).Append(Environment.ProcessId).ToHashSet();
                using var query = new System.Management.ManagementObjectSearcher("SELECT IDProcess,Name,WorkingSetPrivate FROM Win32_PerfFormattedData_PerfProc_Process");
                using var counters = query.Get();
                long privateWorking = 0;
                foreach (System.Management.ManagementObject row in counters) {
                    if (!ids.Contains(Convert.ToInt32(row["IDProcess"]))) continue;
                    long bytes = Convert.ToInt64(row["WorkingSetPrivate"]); privateWorking += bytes;
                    Console.WriteLine($"PROCESS {row["Name"]}: private working set {bytes/1048576d:F1} MiB");
                }
                Console.WriteLine($"PRIVATE WORKING SET: {privateWorking/1048576d:F1} MiB");
                long working = Process.GetCurrentProcess().WorkingSet64, privateBytes = Process.GetCurrentProcess().PrivateMemorySize64;
                foreach (var info in processes) { using var p = Process.GetProcessById(info.ProcessId); working += p.WorkingSet64; privateBytes += p.PrivateMemorySize64; }
                Console.WriteLine($"MEMORY host + {processes.Count} WebView processes: working set {working/1048576d:F1} MiB; private bytes {privateBytes/1048576d:F1} MiB. Fixture host excludes real hardware backend.");
                Console.WriteLine($"PASS {_checks} checks");
            } catch (Exception ex) { Console.Error.WriteLine(ex); result = 1; }
            finally { host.Dispose(); Application.ExitThread(); }
        };
        _ = Init();
        async Task Init() { try { await host.InitializeAsync(); host.Show(); } catch (Exception ex) { Console.Error.WriteLine(ex); result = 1; Application.ExitThread(); } }
        Application.Run();
        return result;
    }
    private static async Task Until(Func<bool> condition, int timeout) { var sw = Stopwatch.StartNew(); while (!condition()) { if (sw.ElapsedMilliseconds > timeout) throw new TimeoutException("Host connection timeout"); await Task.Delay(100); } }
    private static async Task<bool> EvalBool(JelliHostForm h, string script) => await h.Browser.ExecuteScriptAsync(script) == "true";
    private static async Task Capture(JelliHostForm h, string name)
    {
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "screenshots");
        Directory.CreateDirectory(dir);
        using var stream = File.Create(Path.Combine(dir, name + ".png"));
        await h.Browser.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
        if (name == "compact") {
            using var bitmap = new Bitmap(h.Width, h.Height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(h.Location, Point.Empty, h.Size);
            bitmap.Save(Path.Combine(dir, "native-compact.png"));
        }
    }
}

internal sealed class FixtureBackend : IJelliBackend
{
    public JelliHostForm? Host;
    public string? LastAction;
    public bool FailNextAction;
    public readonly List<Color> Colors = new();
    public int Restores;
    public object State(JelliSettings settings) => new {
        telemetry = (TelemetrySnapshot?)null, powerMode = "Unavailable", fanMode = "Unavailable", gpuMode = "Unavailable", gpuNotice = "Requires system restart", fps = (int?)null, overlay = false,
        settings, version = "test", doubleClickMs = 500, gameSyncStatus = "Unavailable",
        sections = new JelliSection[] { new("power", "Performance", [new("power.quiet", "Quiet", "button", false), new("power.balanced", "Balanced", "button", false)]), new("fans", "Cooling", [new("fans.auto", "Auto", "button", false)]), new("battery", "Battery care", [new("battery.limit", "Stop charging at 80%", "toggle", false)]) },
        menu = new JelliMenu[] { new("menu.power", "Power Mode", true, false, [new("power.quiet", "Quiet", true, false, [])]), new("menu.exit", "Exit", true, false, []) },
        monitors = new[] {new {id = "", label = "Primary display"}}
    };
    public void Action(string id, JsonElement data) { if (FailNextAction) { FailNextAction = false; throw new InvalidOperationException("Fixture hardware failure"); } LastAction = id; }
    public void SetOverlayVisible(bool visible) => Host?.SetGaming(visible);
    public void ConfigureOverlay(JelliSettings settings) { }
    public bool TryWriteColor(Color color) { Colors.Add(color); return true; } // In-memory assertion only; no hardware controller exists.
    public void RestoreColor() => Restores++;
    public void Fallback() => throw new Exception("Unexpected renderer failure");
    public void Exit() { }
}
