using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using PredatorControlApp;

// Manual elevated UI integration test. Never construct Form1/WmiController, register a task or invoke hardware.
internal static class SmokeTest
{
    [STAThread]
    static void Main()
    {
        string output = Path.Combine(AppContext.BaseDirectory, "jelli-smoke-result.txt");
        ApplicationConfiguration.Initialize();
        // This guest integration harness represents an installer user who accepted its runtime terms.
        BundledBrowser.AcceptTerms(installerAlreadyAccepted: true);
        using var pump = new Form { ShowInTaskbar = false, WindowState = FormWindowState.Minimized };
        pump.Shown += async (_, _) =>
        {
            pump.Hide();
            try
            {
                var backend = new NoHardwareBackend();
                using var jelli = new JelliHostForm(backend, persist: false);
                await jelli.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(60));
                if (!jelli.Ready) throw new InvalidOperationException("The real frontend did not handshake.");
                jelli.OpenDashboard();
                await Task.Delay(1500);
                string rendered = await jelli.Browser.ExecuteScriptAsync("JSON.stringify({canvas:!!document.querySelector('canvas'),text:document.body.innerText})");
                string json = JsonSerializer.Deserialize<string>(rendered)!;
                using var document = JsonDocument.Parse(json);
                if (!document.RootElement.GetProperty("canvas").GetBoolean() ||
                    !document.RootElement.GetProperty("text").GetString()!.Contains("Jelli smoke test"))
                    throw new InvalidOperationException("The real Jelli dashboard did not render: " + json);
                foreach (int width in new[] { 430, 610 })
                foreach (double scale in new[] { 1d, 1.25d, 1.5d, 2d })
                {
                    jelli.ClientSize = new Size((int)(width * scale), (int)(800 * scale));
                    await jelli.Browser.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride", JsonSerializer.Serialize(new { width, height = 800, deviceScaleFactor = scale, mobile = false }));
                    foreach (string tab in new[] { "Overview", "Hardware", "Keyboard", "Games", "Settings" })
                    {
                        await jelli.Browser.ExecuteScriptAsync($"Array.from(document.querySelectorAll('nav button')).find(b=>b.textContent.includes('{tab}')).click()");
                        await Task.Delay(150);
                        string check = await jelli.Browser.ExecuteScriptAsync("""
                        (() => {
                          const content=document.querySelector('.dashboard-content');
                          const r=content.getBoundingClientRect();
                          const overflow=[...content.querySelectorAll('button,select,input')].some(e=>{const b=e.getBoundingClientRect();return b.width>0&&(b.left<r.left-1||b.right>r.right+1)});
                          if(overflow) return 'horizontal overflow';
                          const zones=[...document.querySelectorAll('.rgb-zones button')];
                          if(zones.length){
                            if(zones.map(b=>b.textContent.trim()).join('|')!=='All zones|Zone 1|Zone 2|Zone 3|Zone 4')return 'zone order';
                            if(zones.some(b=>Math.abs(b.getBoundingClientRect().top-zones[0].getBoundingClientRect().top)>1))return 'wrapped zones';
                            const colors=document.querySelector('.rgb-presets');
                            if(colors.getBoundingClientRect().top<zones[0].getBoundingClientRect().bottom)return 'colors overlap zones';
                            if(getComputedStyle(colors).gridTemplateColumns.split(' ').length!==3)return 'color columns';
                          }
                          return 'PASS';
                        })()
                        """);
                        if (JsonSerializer.Deserialize<string>(check) != "PASS") throw new Exception($"{tab} {width}px {scale}: {check}");
                    }
                }
                await jelli.Browser.ExecuteScriptAsync("Array.from(document.querySelectorAll('nav button')).find(b=>b.textContent.includes('Keyboard')).click()");
                await Task.Delay(200);
                foreach (string selector in new[] { ".rgb-zones button:nth-child(1)", ".rgb-presets button", ".rgb-controls .controls:last-child button" })
                {
                    // Zone 1 wrapper is the second grid child; presets and save retain their IDs.
                    string actual = selector == ".rgb-zones button:nth-child(1)" ? ".rgb-zones > div:nth-child(2) button" : selector;
                    await jelli.Browser.ExecuteScriptAsync($"document.querySelector('{actual}').click()");
                    await Task.Delay(250);
                }
                if (!new[] { "rgb.zone0", "rgb.preset0", "rgb.profile.save" }.All(backend.Actions.Contains))
                    throw new Exception("Fake backend did not receive original RGB action IDs.");
                using var screenshot = File.Create(Path.Combine(AppContext.BaseDirectory, "jelli-smoke.png"));
                await jelli.Browser.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, screenshot);
                File.WriteAllText(output, "PASS: real WebView2 rendered five tabs at 430/610px and 100/125/150/200% device scale; RGB row/grid geometry and original fake-backend action IDs passed.\n" +
                    "Protected root: " + SecureLaunch.ProtectedDataDir + "\nNo hardware, services, tasks or user preferences were written.");
                Environment.ExitCode = 0;
            }
            catch (Exception ex) { File.WriteAllText(output, "FAIL\n" + ex); Environment.ExitCode = 1; }
            finally { pump.Close(); }
        };
        Application.Run(pump);
    }
}

internal sealed class NoHardwareBackend : IJelliBackend
{
    public object State(JelliSettings settings) => new
    {
        device = "Jelli smoke test — no hardware", theme = "PredatorTeal", version = "1.4.3", revision = 0,
        telemetry = new { }, powerMode = "N/A", fanMode = "N/A", gpuMode = "N/A", doubleClickMs = 500,
        sections = Sections(), menu = Array.Empty<object>(), monitors = Array.Empty<object>(), overlay = false,
        settings,
        keyboard = new { kind = "rgb", supportsRgb = true, zoneCount = 4, effects = Array.Empty<object>(), supportsBrightness = false }
    };
    public object Telemetry() => new { };
    internal readonly List<string> Actions = new();
    public Task<object?> Action(string id, JsonElement data)
    {
        Actions.Add(id);
        return Task.FromResult<object?>(new {status = "confirmed", message = "Fake backend only"});
    }
    private static object Control(string id, string label, string kind = "button", object? value = null) => new
    { id, label, kind, value, enabled = id != "rgb.zone2", choices = kind == "select" ? new[] { new {value = 0, label = "Static", enabled = true} } : null, min = 0, max = 100 };
    private static object[] Sections()
    {
        var rgb = new List<object> { Control("rgb.mode", "Lighting effect", "select", 0), Control("rgb.all", "All zones", value: true) };
        for(int i=0;i<4;i++) rgb.Add(Control($"rgb.zone{i}", $"Zone {i+1}"));
        string[] colors = ["Teal", "Red", "Cyan", "Amber", "Purple", "White"];
        for(int i=0;i<colors.Length;i++)rgb.Add(Control($"rgb.preset{i}", colors[i]));
        rgb.Add(Control("rgb.color", "Zone color", "color", "#00ffaa"));
        rgb.Add(Control("rgb.profile", "Saved profile", "select", 0));
        rgb.Add(Control("rgb.profile.save", "Save Zones to Profile"));
        return [new {id="rgb", label="Keyboard light", controls=rgb.ToArray()},
            new {id="power",label="Hardware",controls=new[]{Control("fixture.long","A long hardware label that must wrap without covering its adjacent switch", "toggle", true)}},
            new {id="app",label="Application",controls=new[]{Control("app.startup","Start with Windows","toggle",false)}},
            new {id="battery",label="Battery care",controls=new[]{Control("fixture.battery","Charge limit","toggle",true)}}];
    }
    public bool TryWriteColor(Color color) => false;
    public void RestoreColor() { }
    public void SetOverlayVisible(bool visible) { }
    public void OpenOverlaySettings() { }
    public void ConfigureOverlay(JelliSettings settings) { }
    public void Fallback() => throw new InvalidOperationException("Jelli requested fallback.");
    public void SetJelliEnabled(bool enabled) { }
    public void Exit() { }
}
