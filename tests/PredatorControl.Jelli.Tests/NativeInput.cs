using System.Runtime.InteropServices;
using System.Text.Json;
using PredatorControlApp;

internal static class NativeInput
{
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, nuint extra);

    internal static async Task Run(JelliHostForm host, Action<bool, string> check, Func<bool> actionReceived)
    {
        var saved = Cursor.Position;
        try {
            host.Activate();
            await Task.Delay(300);
            var point = await Target(host, "document.querySelector('canvas')");
            check(GetAncestor(WindowFromPoint(point), 2) == host.Handle, "Windows hit testing reaches the creature");
            Cursor.Position = point;
            await Task.Delay(100);
            Click();
            await Task.Delay(650);
            check(await host.Browser.ExecuteScriptAsync("document.querySelector('main').classList.contains('summary')") == "true", "native mouse click opens summary");
            host.Collapse();
            await Task.Delay(200);
            point = await Target(host, "document.querySelector('canvas')");
            check(GetAncestor(WindowFromPoint(point), 2) == host.Handle, "collapsed creature retains native input");
            Cursor.Position = point;
            await Task.Delay(100);
            var start = host.Location;
            mouse_event(2, 0, 0, 0, 0);
            for (int i = 1; i <= 5; i++) { Cursor.Position = new(point.X + i * 5, point.Y + i * 5); await Task.Delay(60); }
            mouse_event(4, 0, 0, 0, 0);
            await Task.Delay(200);
            check(host.Location != start, "native mouse drag moves creature");
            host.OpenDashboard();
            await Task.Delay(300);
            point = await Target(host, "[...document.querySelectorAll('button')].find(b=>b.textContent==='Quiet')");
            check(GetAncestor(WindowFromPoint(point), 2) == host.Handle, "Windows hit testing reaches dashboard controls");
            Cursor.Position = point;
            Click();
            await Task.Delay(200);
            check(actionReceived(), "native dashboard click reaches backend");
            // Exercise browser wheel default handling with a bounded scroll fixture.
            await host.Browser.ExecuteScriptAsync("window.nativeWheel=false;document.addEventListener('wheel',e=>window.nativeWheel=e.isTrusted,{once:true});let s=document.createElement('div');s.id='native-scroll';s.style='position:fixed;left:20px;top:250px;width:200px;height:100px;overflow:auto;z-index:999;background:#222';s.innerHTML='<div style=height:1000px>Scroll test</div>';document.body.append(s)");
            Cursor.Position = await Target(host, "document.querySelector('#native-scroll')");
            await Task.Delay(300);
            mouse_event(0x800, 0, 0, unchecked((uint)-120), 0);
            await Task.Delay(250);
            check(await host.Browser.ExecuteScriptAsync("window.nativeWheel && document.querySelector('#native-scroll').scrollTop > 0") == "true", "native mouse wheel reaches WebView and scrolls");
            await host.Browser.ExecuteScriptAsync("document.querySelector('#native-scroll').remove()");
            host.SetGaming(true);
            await Task.Delay(300);
            host.OpenDashboard();
            await Task.Delay(300);
            point = await Target(host, "[...document.querySelectorAll('button')].find(b=>b.textContent==='Quiet')");
            check(GetAncestor(WindowFromPoint(point), 2) == host.Handle, "tray dashboard restoration retains native input");
            host.Collapse();
        } finally { mouse_event(4, 0, 0, 0, 0); Cursor.Position = saved; }
    }
    private static void Click() { mouse_event(2, 0, 0, 0, 0); mouse_event(4, 0, 0, 0, 0); }
    private static async Task<Point> Target(JelliHostForm host, string element)
    {
        using var json = JsonDocument.Parse(await host.Browser.ExecuteScriptAsync($"(()=>{{let r=({element}).getBoundingClientRect();return [r.x+r.width/2,r.y+r.height/2]}})()"));
        double scale = host.DeviceDpi / 96d;
        return host.PointToScreen(new((int)(json.RootElement[0].GetDouble()*scale), (int)(json.RootElement[1].GetDouble()*scale)));
    }
}
