using System.Security.AccessControl;
using System.Reflection;
using System.Diagnostics;
using System.Net;
using System.Text;
using PredatorControlApp;

int failed = 0, passed = 0;
async Task Check(string name, Func<Task<bool>> check)
{
    try { if (!await check()) throw new Exception("assertion failed"); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception ex) { Console.WriteLine("FAIL " + name + ": " + ex.GetBaseException()); failed++; }
}
var assembly = typeof(SingleInstanceIpc).Assembly;
await Check("IPC oversized command is rejected with bounded consumption", async () =>
{
    using var input = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 100000) + "\n"));
    var read = typeof(SingleInstanceIpc).GetMethod("ReadCommandAsync", BindingFlags.Static | BindingFlags.NonPublic);
    string? command;
    if (read == null) // Original implementation: reproduce the same unbounded read without opening a pipe.
        command = await new StreamReader(input).ReadLineAsync();
    else command = await (Task<string?>)read.Invoke(null, [input, CancellationToken.None])!;
    return command == null && input.Position < 2048;
});
await Check("IPC accepts normal command and cancels stalled input", async () =>
{
    var read = typeof(SingleInstanceIpc).GetMethod("ReadCommandAsync", BindingFlags.Static | BindingFlags.NonPublic);
    if (read == null) return false;
    using var input = new MemoryStream(Encoding.UTF8.GetBytes("TOGGLE\r\n"));
    if (await (Task<string?>)read.Invoke(null, [input, CancellationToken.None])! != "TOGGLE") return false;
    using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
    using var stalled = new StalledStream();
    try { await (Task<string?>)read.Invoke(null, [stalled, cancelled.Token])!; return false; }
    catch (OperationCanceledException) { return true; }
});
await Check("native resource remains locked against replacement", () =>
{
    string dir = Path.Combine(Path.GetTempPath(), "PredatorControl-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    string path = Path.Combine(dir, "resource.dat");
    try
    {
        var secure = assembly.GetType("PredatorControlApp.SecureLaunch")!;
        var method = secure.GetMethod("WriteLockedResource", BindingFlags.Static | BindingFlags.NonPublic);
        using var held = method == null ? null : (FileStream)method.Invoke(null, [path, new byte[] { 1, 2, 3 }])!;
        if (method == null) { File.WriteAllBytes(path, [1, 2, 3]); using var originalCheck = File.OpenRead(path); }
        try { File.WriteAllBytes(path, [4, 5, 6]); return Task.FromResult(false); }
        catch (IOException) { return Task.FromResult(File.ReadAllBytes(path).SequenceEqual(new byte[] { 1, 2, 3 })); }
    }
    finally { Directory.Delete(dir, true); }
});
await Check("update script preserves exclamation marks with inherited delayed expansion", async () =>
{
    var type = assembly.GetType("PredatorControlApp.Updater")!;
    string script = (string)type.GetMethod("BuildUpdateScript", BindingFlags.Static | BindingFlags.NonPublic)!
        .Invoke(null, [123, @"C:\test!value\staged.exe", @"C:\app.exe", @"C:\backup.exe", @"C:\stage", new string('a', 64)])!;
    string dir = Path.Combine(Path.GetTempPath(), "PredatorControl-shell-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
        // Execute only variable assignments, never the updater's wait/copy/move/launch commands.
        string file = Path.Combine(dir, "assignments.cmd");
        File.WriteAllText(file, script[..script.IndexOf("set /a WAITS=", StringComparison.Ordinal)] + "echo %STAGED%\r\n");
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dir };
        start.ArgumentList.Add("/d"); start.ArgumentList.Add("/v:on"); start.ArgumentList.Add("/c"); start.ArgumentList.Add(file);
        using var process = Process.Start(start)!;
        string output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return process.ExitCode == 0 && output.Trim() == @"C:\test!value\staged.exe";
    }
    finally { Directory.Delete(dir, true); }
});
await Check("updater validates redirects and caps redirect loops without networking", async () =>
{
    var get = assembly.GetType("PredatorControlApp.Updater")!.GetMethod("GetTrustedAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
    foreach (string target in new[] { "http://github.com/asset", "https://example.invalid/asset", "https://github.com/loop" })
    {
        using var handler = new RedirectHandler(target);
        using var client = new HttpClient(handler);
        try { using var result = await (Task<HttpResponseMessage>)get.Invoke(null, [client, "https://github.com/start", CancellationToken.None])!; return false; }
        catch (InvalidOperationException) { if (handler.Calls > 5) return false; }
    }
    return true;
});
await Check("malformed Game Sync entries cannot break polling", () =>
{
    const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
    var type = typeof(GameSyncController);
    var controller = (GameSyncController)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
    string path = Path.Combine(Path.GetTempPath(), "PredatorControl-profiles-test-" + Guid.NewGuid().ToString("N") + ".json");
    try
    {
        File.WriteAllText(path, "{\"Enabled\":true,\"Profiles\":[null,{\"ExecutableName\":null},{\"ExecutableName\":\"..\\\\game.exe\"}]}");
        type.GetField("_savePath", flags)!.SetValue(controller, path);
        type.GetField("_profiles", flags)!.SetValue(controller, new List<GameProfile>());
        type.GetMethod("Load", flags)!.Invoke(controller, null);
        type.GetMethod("PollProcesses", flags)!.Invoke(controller, [null, EventArgs.Empty]);
        return Task.FromResult(controller.Profiles.Count == 0);
    }
    finally { File.Delete(path); } // Never Dispose a constructor-free test fixture (no timer was created).
});
await Check("Game Sync edits reject invalid names and preserve a valid profile", () =>
{
    var type = typeof(GameSyncController);
    var controller = (GameSyncController)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
    type.GetField("_profiles", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(controller, new List<GameProfile>());
    foreach (var bad in new[] { null, "", "../game.exe", @"C:\game.exe", "game.txt" })
    {
        var profile = new GameProfile { ExecutableName = bad! };
        try { controller.AddProfile(profile); return Task.FromResult(false); }
        catch (ArgumentException) { }
        try { controller.UpdateProfile(profile); return Task.FromResult(false); }
        catch (ArgumentException) { }
    }
    // Save() fails harmlessly on the constructor-free fixture's absent path; it cannot write real preferences.
    controller.AddProfile(new GameProfile { ExecutableName = "fixture-game.exe", DisplayName = "Fixture" });
    controller.UpdateProfile(new GameProfile { ExecutableName = "fixture-game.exe", DisplayName = "Updated" });
    return Task.FromResult(controller.Profiles.Count == 1 && controller.Profiles[0].DisplayName == "Updated");
});
await Check("startup refuses the test image in a writable folder without changing its ACL", () =>
{
    var secure = assembly.GetType("PredatorControlApp.SecureLaunch")!;
    var prepare = secure.GetMethod("PrepareTaskBinary", BindingFlags.NonPublic | BindingFlags.Static)!;
    var path = Environment.ProcessPath!;
    var before = new FileInfo(path).GetAccessControl().GetSecurityDescriptorBinaryForm();
    object?[] args = [null];
    bool rejected = prepare.Invoke(null, args) == null && !string.IsNullOrWhiteSpace(args[0] as string);
    return Task.FromResult(rejected && before.SequenceEqual(new FileInfo(path).GetAccessControl().GetSecurityDescriptorBinaryForm()));
});
await AuditChecks.Run(assembly, Check);
await BundleChecks.Run(assembly, Check);
Console.WriteLine($"{passed} passed, {failed} failed. Temporary data only; no hardware or elevated operations.");
return failed == 0 ? 0 : 1;

sealed class RedirectHandler(string target) : HttpMessageHandler
{
    public int Calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        var response = new HttpResponseMessage(HttpStatusCode.Redirect);
        response.Headers.Location = new Uri(target);
        return Task.FromResult(response);
    }
}

sealed class StalledStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    { await Task.Delay(Timeout.Infinite, cancellationToken); return 0; }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
