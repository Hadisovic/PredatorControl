using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace PredatorControlApp;

// The WebView renderer runs in its own Chromium process. Hardware remains in Form1.
// No server, open port, second startup entry, or public named-pipe extension is needed.
internal sealed class JelliHostForm : Form
{
    private const string Origin = "https://jelli.predator.local/";
    private readonly IJelliBackend _backend;
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Transparent };
    private readonly System.Windows.Forms.Timer _cursor = new() { Interval = 32 };
    private readonly System.Windows.Forms.Timer _state = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _rgb = new() { Interval = 750 };
    private JelliSettings _settings = JelliSettings.Load();
    private readonly bool _persist;
    private Point _anchor;
    private string _surface = "compact";
    private bool _ready, _gaming, _disposed, _recovering;
    private Color? _pendingColor, _lastColor;
    private bool _colorWritten;
    private bool _isFlyout;
    private double _creatureX, _creatureY;
    private int _recoveries;
    private bool _actionInProgress;
    private readonly TaskCompletionSource _connected = new();

    internal CoreWebView2 Browser => _web.CoreWebView2;
    internal bool Ready => _ready;
    internal bool DashboardOpen => _surface == "dashboard";
    internal JelliHostForm(IJelliBackend backend, bool persist = true)
    {
        _backend = backend;
        _persist = persist;
        if (!persist) _settings = new();
        Text = "Predator Control · Jelli";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        // A color-keyed layered parent makes the HWND WebView click-through:
        // Windows hit-tests the parent's keyed pixels, not Chromium's visuals.
        // DWM glass preserves transparent rendering without layered hit testing.
        BackColor = Color.Black;
        Controls.Add(_web);
        _anchor = new(_settings.X, _settings.Y);
        SetSurface("compact");
        _cursor.Tick += (_, _) => SendCursor();
        _state.Tick += (_, _) => PublishState();
        _rgb.Tick += (_, _) => FlushColor();
        Deactivate += (_, _) => { if (_surface is "menu" or "summary") SetSurface("compact"); };
        DpiChanged += (_, _) => SetSurface(_surface);
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmExtendFrameIntoClientArea(nint window, ref Margins margins);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        Marshal.ThrowExceptionForHR(DwmExtendFrameIntoClientArea(Handle, ref margins));
    }

    internal async Task InitializeAsync(bool startSuspended = false)
    {
        _gaming = startSuspended;
        string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PredatorControl", _persist ? "WebView2" : "WebView2-Test");
        var options = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--disable-gpu --js-flags=\"--max-old-space-size=16 --max-semi-space-size=1 --optimize-for-size\"" };
        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: profile, options: options);
        await _web.EnsureCoreWebView2Async(env);
        var core = _web.CoreWebView2;
        core.AddWebResourceRequestedFilter(Origin + "*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) => {
            var uri = new Uri(e.Request.Uri);
            string path = uri.AbsolutePath.TrimStart('/');
            if (path.Length == 0) path = "index.html";
            var assembly = typeof(JelliHostForm).Assembly;
            string resource = "JelliUI/" + path;
            string? name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.Replace('\\', '/') == resource);
            var stream = name == null ? null : assembly.GetManifestResourceStream(name);
            string mime = Path.GetExtension(path) switch { ".html" => "text/html", ".js" => "text/javascript", ".css" => "text/css", ".svg" => "image/svg+xml", _ => "application/octet-stream" };
            e.Response = env.CreateWebResourceResponse(stream, stream == null ? 404 : 200, stream == null ? "Not found" : "OK", "Content-Type: " + mime + "\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff");
        };
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = Environment.GetCommandLineArgs().Contains("--jelli-devtools");
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.AreHostObjectsAllowed = false;
        core.NavigationStarting += (_, e) => { if (e.Uri != Origin && e.Uri != Origin + "index.html") e.Cancel = true; };
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.DownloadStarting += (_, e) => e.Cancel = true;
        core.WebMessageReceived += Receive;
        core.ProcessFailed += (_, _) => Recover();
        core.Navigate(Origin);
        _state.Start();
        _rgb.Start();
        await _connected.Task.WaitAsync(TimeSpan.FromSeconds(15));
        if (Environment.GetCommandLineArgs().Contains("--jelli-devtools")) core.OpenDevToolsWindow();
    }

    private async void Recover()
    {
        if (_disposed || _recovering) return;
        _recovering = true;
        _ready = false;
        _cursor.Stop();
        try {
            if (++_recoveries > 3) throw new InvalidOperationException("The Jelli renderer repeatedly stopped. Native controls are available from the tray.");
            await Task.Delay(1000);
            if (!_disposed) _web.CoreWebView2.Reload();
        }
        catch (Exception ex) { Hide(); _backend.Fallback(); Program.Report(ex, false); }
        finally { _recovering = false; }
    }

    private void Send(object message)
    {
        if (!_ready || _disposed) return;
        try { _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, JelliSettings.Json)); }
        catch (InvalidOperationException) { Recover(); }
    }
    internal void PublishState()
    {
        if (_ready && !_gaming) Send(new { version = 1, type = "state", data = _backend.State(_settings) });
    }

    private void Receive(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!e.Source.StartsWith(Origin, StringComparison.Ordinal) || e.WebMessageAsJson.Length > 16384) return;
        JelliRequest? request = null;
        try
        {
            request = JsonSerializer.Deserialize<JelliRequest>(e.WebMessageAsJson, JelliSettings.Json);
            if (request == null || request.Version != 1 || string.IsNullOrEmpty(request.Id)) throw new ArgumentException("Unsupported protocol message.");
            object? result = null;
            switch (request.Action)
            {
                case "connect":
                    _ready = true;
                    _connected.TrySetResult();
                    result = _backend.State(_settings);
                    SendLayout();
                    if (!_gaming) { Show(); _cursor.Start(); }
                    break;
                case "surface": SetSurface(request.Data.GetString() ?? "compact"); break;
                case "move":
                    var x = request.Data.GetProperty("x").GetDouble();
                    var y = request.Data.GetProperty("y").GetDouble();
                    if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > 100000 || Math.Abs(y) > 100000) throw new ArgumentException("Invalid position.");
                    // JS uses desktop logical coordinates consistently with the cursor snapshot.
                    double scale = DeviceDpi / 96d;
                    _anchor = new((int)(x * scale), (int)(y * scale));
                    SetSurface(_surface);
                    break;
                case "savePosition": SavePosition(); break;
                case "control":
                    string id = request.Data.GetProperty("id").GetString() ?? "";
                    if (id.StartsWith("rgb.", StringComparison.Ordinal) && _settings.SyncRgb) DisableSync();
                    if (_actionInProgress) throw new InvalidOperationException("Complete the current hardware dialog first.");
                    _actionInProgress = true;
                    try { _backend.Action(id, request.Data.GetProperty("value")); }
                    finally { _actionInProgress = false; }
                    PublishState();
                    break;
                case "settings":
                    var updated = JelliSettings.Validate(request.Data.Deserialize<JelliSettings>(JelliSettings.Json) ?? throw new ArgumentException("Missing preferences."));
                    if (_settings.SyncRgb && !updated.SyncRgb) DisableSync();
                    _settings = updated with { X = _anchor.X, Y = _anchor.Y };
                    if (_persist) _settings.Save();
                    _backend.ConfigureOverlay(_settings);
                    SetSurface(_surface);
                    PublishState();
                    break;
                case "color":
                    _pendingColor = Color.FromArgb(request.Data.GetProperty("r").GetInt32(), request.Data.GetProperty("g").GetInt32(), request.Data.GetProperty("b").GetInt32());
                    break;
                case "overlay": _backend.SetOverlayVisible(request.Data.GetBoolean()); break;
                case "legacy": _backend.Fallback(); break;
                case "exit": _backend.Exit(); break;
                default: throw new ArgumentException("Unknown Jelli action.");
            }
            Send(new { version = 1, type = "response", id = request.Id, data = result });
        }
        catch (Exception ex) { Send(new { version = 1, type = "response", id = request?.Id, error = ex.Message }); }
    }

    internal void SetSurface(string surface)
    {
        if (surface is not ("compact" or "summary" or "dashboard" or "menu")) throw new ArgumentException("Invalid surface.");
        _surface = surface;
        TopMost = surface != "dashboard";
        double scale = DeviceDpi / 96d;
        var work = Screen.FromPoint(_anchor).WorkingArea;
        bool flyout = surface == "dashboard" && _settings.Presentation == "flyout" && work.Width / scale >= 610;
        _isFlyout = flyout;
        int width = surface switch { "dashboard" => flyout ? 610 : 430, "summary" => 330, "menu" => 430, _ => 180 };
        int height = surface switch { "dashboard" => 600, "summary" => 400, "menu" => 570, _ => 180 };
        width = Math.Min(width, (int)(work.Width / scale));
        height = Math.Min(height, (int)(work.Height / scale));
        var size = new Size((int)(width * scale), (int)(height * scale));
        _anchor = Clamp(_anchor, new Size((int)(180 * scale), (int)(180 * scale)), work);
        bool left = flyout && _anchor.X + size.Width > work.Right;
        // Grow around the existing creature center; only move it when edge clamping requires it.
        int offset = flyout ? (left ? 430 : 0) : (width - 180) / 2;
        var desired = new Point(_anchor.X - (int)(offset * scale), _anchor.Y);
        Location = Clamp(desired, size, work);
        ClientSize = size;
        _creatureX = flyout ? (_anchor.X - Left) / scale + 20 : surface == "compact" ? 20 : (width - 140) / 2d;
        _creatureY = flyout ? Math.Clamp((_anchor.Y - Top) / scale, 0, height - 180) : 0;
        SendLayout();
    }
    internal static Point Clamp(Point point, Size size, Rectangle work) => new(
        Math.Clamp(point.X, work.Left, Math.Max(work.Left, work.Right - size.Width)),
        Math.Clamp(point.Y, work.Top, Math.Max(work.Top, work.Bottom - size.Height)));
    private void SendLayout() => Send(new { version = 1, type = "layout", data = new {
        surface = _surface, creatureX = _creatureX, creatureY = _creatureY,
        flyout = _isFlyout,
        panelLeft = _creatureX > 200 ? 0 : 180,
    }});
    private void SendCursor()
    {
        if (_gaming || !Visible) return;
        var p = Cursor.Position;
        double s = DeviceDpi / 96d;
        Send(new { version = 1, type = "cursor", data = new {
            x = p.X / s, y = p.Y / s, windowX = Left / s + _creatureX,
            windowY = Top / s + _creatureY, anchorX = _anchor.X / s, anchorY = _anchor.Y / s,
        }});
    }
    internal async void SetGaming(bool active)
    {
        _gaming = active;
        _backend.ConfigureOverlay(_settings);
        if (active) {
            _cursor.Stop();
            Send(new { version = 1, type = "suspend", data = true });
            Hide();
            try { if (_web.CoreWebView2 != null) await _web.CoreWebView2.TrySuspendAsync(); } catch (InvalidOperationException) { }
        } else {
            if (_web.CoreWebView2 != null) _web.CoreWebView2.Resume();
            SetSurface(_surface);
            if (_ready) { Show(); _cursor.Start(); }
            Send(new { version = 1, type = "suspend", data = false });
            PublishState();
        }
    }
    internal void OpenDashboard() { if (_gaming) _backend.SetOverlayVisible(false); SetSurface("dashboard"); Show(); Activate(); }
    internal void Collapse() => SetSurface("compact");
    private void OnDisplayChanged(object? sender, EventArgs e) { if (!_disposed && IsHandleCreated) BeginInvoke(() => SetSurface(_surface)); }
    private void SavePosition() { _settings = _settings with { X = _anchor.X, Y = _anchor.Y }; if (_persist) _settings.Save(); }
    internal void DisableSync()
    {
        _settings = _settings with { SyncRgb = false };
        _pendingColor = null;
        _lastColor = null;
        if (_colorWritten) { _backend.RestoreColor(); _colorWritten = false; }
        if (_persist) _settings.Save();
    }
    internal void PauseColorSync()
    {
        _lastColor = null;
        if (_colorWritten) { _backend.RestoreColor(); _colorWritten = false; }
    }
    private void FlushColor()
    {
        if (!_settings.SyncRgb || _gaming || _pendingColor is not Color c) return;
        if (_lastColor is Color last && Math.Abs(c.R-last.R) + Math.Abs(c.G-last.G) + Math.Abs(c.B-last.B) < 35) return;
        try { if (_backend.TryWriteColor(c)) { _lastColor = c; _colorWritten = true; } }
        catch (Exception ex) { DisableSync(); Send(new { version = 1, type = "error", error = ex.Message }); }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed) {
            _disposed = true;
            _connected.TrySetCanceled();
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            _cursor.Dispose(); _state.Dispose(); _rgb.Dispose();
            try { SavePosition(); if (_colorWritten) _backend.RestoreColor(); } catch { }
            _web.Dispose();
        }
        base.Dispose(disposing);
    }
}
