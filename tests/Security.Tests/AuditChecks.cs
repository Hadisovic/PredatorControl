using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.AccessControl;
using Microsoft.Win32.SafeHandles;
using PredatorControlApp;

internal static class AuditChecks
{
    private const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string link, string existing, IntPtr security);

    internal static async Task Run(Assembly assembly, Func<string, Func<Task<bool>>, Task> check)
    {
        bool Rejects(MethodInfo method, object?[] args)
        {
            try { method.Invoke(null, args); return false; }
            catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException or InvalidOperationException or IOException) { return true; }
        }
        var ipc = typeof(SingleInstanceIpc);
        await check("GUI and headless startup share an exclusive mutex without using the live app name", () =>
        {
            var acquire = ipc.GetMethod("TryAcquireHardwareLock", Static)!;
            string name = "PredatorControl-mutex-test-" + Guid.NewGuid().ToString("N");
            using (var first = (Mutex?)acquire.Invoke(null, [name]))
            {
                if (first == null) return Task.FromResult(false);
                try
                {
                    using var second = (Mutex?)acquire.Invoke(null, [name]);
                    if (second != null) return Task.FromResult(false);
                }
                finally { first.ReleaseMutex(); }
            }
            using var next = (Mutex?)acquire.Invoke(null, [name]);
            if (next == null) return Task.FromResult(false);
            next.ReleaseMutex();
            return Task.FromResult(true);
        });
        await check("IPC allowlist separates activation from elevated commands and sessions", () =>
        {
            var method = ipc.GetMethod("IsCommandAllowed", Static);
            if (method == null) return Task.FromResult(false);
            bool Allowed(string command, bool user, bool session, bool elevated) =>
                (bool)method.Invoke(null, [command, user, session, elevated])!;
            return Task.FromResult(Allowed("TOGGLE", true, true, false) && Allowed("SHOW", true, true, false) &&
                new[] { "CYCLE_MODE", "MODE", "EXIT", "QUIT" }.All(c => !Allowed(c, true, true, false) && Allowed(c, true, true, true)) &&
                !Allowed("arbitrary", true, true, true) && !Allowed("TOGGLE", false, true, true) && !Allowed("TOGGLE", true, false, true));
        });
        await check("real Windows pipe token authorizes activation and checks administrator membership", async () =>
        {
            var authorize = ipc.GetMethod("AuthorizeClient", Static);
            if (authorize == null) return false;
            string name = "PredatorControl-audit-" + Guid.NewGuid().ToString("N");
            using var identity = WindowsIdentity.GetCurrent();
            var security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new PipeAccessRule(identity.User!,
                PipeAccessRights.ReadData | PipeAccessRights.WriteData | PipeAccessRights.ReadAttributes | PipeAccessRights.Synchronize, AccessControlType.Allow));
            using var server = NamedPipeServerStreamAcl.Create(name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 0, 0, security);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var connected = server.WaitForConnectionAsync(timeout.Token);
            using var client = new NamedPipeClientStream(".", name, PipeAccessRights.WriteData | PipeAccessRights.ReadAttributes | PipeAccessRights.Synchronize,
                PipeOptions.Asynchronous, TokenImpersonationLevel.Identification, HandleInheritability.None);
            await client.ConnectAsync(timeout.Token);
            await connected;
            var received = new StreamReader(server, leaveOpen: true).ReadLineAsync(timeout.Token);
            await client.WriteAsync(Encoding.UTF8.GetBytes("TOGGLE\n"), timeout.Token);
            await client.FlushAsync(timeout.Token);
            await received;
            bool elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            return (bool)authorize.Invoke(null, [server, "TOGGLE"])! &&
                (bool)authorize.Invoke(null, [server, "CYCLE_MODE"])! == elevated &&
                !(bool)authorize.Invoke(null, [server, "unknown"])!;
        });
        await check("atomic Game Sync save replaces a hardlink without changing its target", () =>
        {
            string dir = Path.Combine(Path.GetTempPath(), "PredatorControl-link-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string sentinel = Path.Combine(dir, "sentinel.json"), destination = Path.Combine(dir, "game_sync.json");
                File.WriteAllText(sentinel, "original fixture");
                if (!CreateHardLink(destination, sentinel, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                var type = typeof(GameSyncController);
                var controller = (GameSyncController)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
                type.GetField("_savePath", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(controller, destination);
                type.GetField("_profiles", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(controller, new List<GameProfile>());
                controller.AddProfile(new GameProfile { ExecutableName = "fixture.exe" });
                return Task.FromResult(File.ReadAllText(sentinel) == "original fixture" && File.ReadAllText(destination).Contains("fixture.exe"));
            }
            finally { Directory.Delete(dir, true); }
        });
        await check("safe file writes reject junction ancestors and preserve ordinary Unicode paths", () =>
        {
            var write = assembly.GetType("PredatorControlApp.SafeFile")?.GetMethod("WriteText", Static);
            if (write == null) return Task.FromResult(false);
            string dir = Path.Combine(Path.GetTempPath(), "PredatorControl-junction-test-" + Guid.NewGuid().ToString("N"));
            string target = Path.Combine(dir, "target"), link = Path.Combine(dir, "link");
            Directory.CreateDirectory(target);
            try
            {
                using var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                    $"/d /c mklink /J \"{link}\" \"{target}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
                if (process == null || !process.WaitForExit(5000) || process.ExitCode != 0) return Task.FromResult(false);
                try { write.Invoke(null, [Path.Combine(link, "fixture.json"), "blocked"]); return Task.FromResult(false); }
                catch (TargetInvocationException ex) when (ex.InnerException is IOException) { }
                string normal = Path.Combine(target, "عربي ! % fixture.json");
                write.Invoke(null, [normal, "first"]);
                write.Invoke(null, [normal, "second"]);
                try { write.Invoke(null, [target, "cannot replace a directory"]); return Task.FromResult(false); }
                catch (TargetInvocationException ex) when (ex.InnerException is System.ComponentModel.Win32Exception) { }
                return Task.FromResult(!File.Exists(Path.Combine(target, "fixture.json")) && File.ReadAllText(normal) == "second" &&
                    Directory.GetFiles(dir, "*.tmp").Length == 0);
            }
            finally
            {
                if (Directory.Exists(link)) Directory.Delete(link); // Remove only our junction, never traverse it.
                Directory.Delete(dir, true);
            }
        });
        await check("profile reader rejects linked and oversized input", () =>
        {
            var read = assembly.GetType("PredatorControlApp.SafeFile")?.GetMethod("ReadText", Static);
            if (read == null) return Task.FromResult(false);
            string dir = Path.Combine(Path.GetTempPath(), "PredatorControl-read-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "data.json"), link = Path.Combine(dir, "link.json");
                File.WriteAllText(path, "fixture");
                if (!CreateHardLink(link, path, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                try { read.Invoke(null, [link, 1024]); return Task.FromResult(false); }
                catch (TargetInvocationException ex) when (ex.InnerException is IOException) { }
                File.Delete(link);
                try { read.Invoke(null, [path, 2]); return Task.FromResult(false); }
                catch (TargetInvocationException ex) when (ex.InnerException is IOException) { }
                return Task.FromResult((string)read.Invoke(null, [path, 1024])! == "fixture");
            }
            finally { Directory.Delete(dir, true); }
        });
        await check("handle-relative save stays in its verified directory after pathname substitution", () =>
        {
            var safe = assembly.GetType("PredatorControlApp.SafeFile")!;
            string dir = Path.Combine(Path.GetTempPath(), "PredatorControl-pin-test-" + Guid.NewGuid().ToString("N"));
            string original = Path.Combine(dir, "original"), moved = Path.Combine(dir, "moved"), target = Path.Combine(dir, "target");
            Directory.CreateDirectory(original);
            Directory.CreateDirectory(target);
            try
            {
                using var held = (SafeFileHandle)safe.GetMethod("OpenDirectory", Static)!.Invoke(null, [Path.Combine(original, "fixture.json"), false])!;
                Directory.Move(original, moved);
                using var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                    $"/d /c mklink /J \"{original}\" \"{target}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
                if (process == null || !process.WaitForExit(5000) || process.ExitCode != 0) return Task.FromResult(false);
                safe.GetMethod("WriteTextCore", Static)!.Invoke(null, [held, "fixture.json", "anchored fixture"]);
                return Task.FromResult(!File.Exists(Path.Combine(target, "fixture.json")) &&
                    File.ReadAllText(Path.Combine(moved, "fixture.json")) == "anchored fixture");
            }
            finally
            {
                if (Directory.Exists(original)) Directory.Delete(original);
                Directory.Delete(dir, true);
            }
        });
        var updater = assembly.GetType("PredatorControlApp.Updater")!;
        await check("update bodies enforce size limits and cancel stalled downloads", async () =>
        {
            var copy = updater.GetMethod("CopyLimitedAsync", Static)!;
            using var source = new MemoryStream([1, 2, 3, 4]);
            using var destination = new MemoryStream();
            try { await (Task)copy.Invoke(null, [source, destination, 3L, CancellationToken.None])!; return false; }
            catch (IOException) { if (destination.Length > 3) return false; }
            using var stalled = new StalledStream();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            try { await (Task)copy.Invoke(null, [stalled, destination, 10L, timeout.Token])!; return false; }
            catch (OperationCanceledException) { return true; }
        });
        await check("update metadata rejects downgrades, tag mismatches and unsafe asset names", () =>
        {
            var infoType = assembly.GetType("PredatorControlApp.UpdateInfo")!;
            var validate = updater.GetMethod("ValidateUpdateInfo", Static)!;
            object Info(Version version, string tag, string name) => Activator.CreateInstance(infoType,
                version, tag, "fixture", "https://github.com/fixture.exe", "https://github.com/checksums.txt", name)!;
            foreach (var info in new[] { Info(new(0, 0, 0), "v0.0.0", "fixture.exe"),
                Info(new(999, 0, 0), "v998.0.0", "fixture.exe"), Info(new(999, 0, 0), "v999.0.0", "../fixture.exe") })
                if (!Rejects(validate, [info])) return Task.FromResult(false);
            validate.Invoke(null, [Info(new(999, 0, 0), "v999.0.0", "fixture.exe")]);
            var pick = updater.GetMethod("PickAssetInfo", Static)!;
            foreach (string json in new[] { "null", "[]", "{\"assets\":[null,3,{\"name\":\"../unsafe.exe\"}]}" })
            {
                using var document = JsonDocument.Parse(json);
                if (pick.Invoke(null, [document.RootElement, false]) != null) return Task.FromResult(false);
            }
            return Task.FromResult(true);
        });
        await check("automatic updates reject unsigned executables and different publisher keys", () =>
        {
            var verify = updater.GetMethod("VerifyUpdateAuthenticity", Static)!;
            if (!Rejects(verify, [Environment.ProcessPath!, Environment.ProcessPath!])) return Task.FromResult(false);
            using var first = RSA.Create(2048);
            using var other = RSA.Create(2048);
            X509Certificate2 Cert(RSA key, string name) => new CertificateRequest("CN=" + name, key, HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1).CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            using var installed = Cert(first, "Fixture");
            using var renewed = Cert(first, "Fixture renewed");
            using var attacker = Cert(other, "Fixture");
            var same = updater.GetMethod("SameSigningKey", Static)!;
            return Task.FromResult((bool)same.Invoke(null, [installed, renewed])! && !(bool)same.Invoke(null, [installed, attacker])!);
        });
        await check("update handoff rejects shell line breaks, relative paths and malformed hashes", () =>
        {
            var build = updater.GetMethod("BuildUpdateScript", Static)!;
            object?[] Args(string path, string hash) => [123, path, @"C:\fixture\app.exe", @"C:\fixture\backup.exe", @"C:\fixture\stage", hash];
            return Task.FromResult(Rejects(build, Args("relative.exe", new string('a', 64))) &&
                Rejects(build, Args("C:\\bad\r\npath.exe", new string('a', 64))) &&
                Rejects(build, Args(@"C:\fixture\staged.exe", "untrusted\nvalue")));
        });
        await check("Jelli bridge rejects untrusted origins and malformed command payloads", () =>
        {
            var protocol = assembly.GetType("PredatorControlApp.JelliProtocol")!;
            var parse = protocol.GetMethod("ReadRequest", Static)!;
            string Message(string action, object? data) => JsonSerializer.Serialize(new { version = 1, id = "fixture", action, data });
            const string origin = "https://jelli.predator.local/";
            foreach (string source in new[] { "https://example.invalid/", "https://jelli.predator.local.evil/", origin + "untrusted.html", origin + "?page=evil", "file:///index.html" })
                if (!Rejects(parse, [source, Message("exit", null)])) return Task.FromResult(false);
            foreach (string message in new[] { Message("unknown", null), Message("move", new { x = 1e100, y = 0 }),
                Message("color", new { r = 256, g = 0, b = 0 }), Message("color", new { r = 0.5, g = 0, b = 0 }),
                Message("control", new { id = "power" }), Message("overlay", "true"), new string('x', 16385) })
                if (!Rejects(parse, [origin, message])) return Task.FromResult(false);
            foreach (string message in new[] { Message("connect", null), Message("surface", "dashboard"),
                Message("move", new { x = -20, y = 40 }), Message("color", new { r = 0, g = 255, b = 40 }),
                Message("control", new { id = "battery.limit", value = true }), Message("overlay", true) })
                if (parse.Invoke(null, [origin, message]) == null) return Task.FromResult(false);
            return Task.FromResult(true);
        });
        await check("ACL validator refuses a null DACL even with an administrator owner", () =>
        {
            var security = new FileSecurity();
            security.SetSecurityDescriptorSddlForm("O:BAG:BAD:NO_ACCESS_CONTROL");
            var validate = assembly.GetType("PredatorControlApp.SecureLaunch")!.GetMethod("HasUntrustedWriter", Static)!;
            return Task.FromResult((bool)validate.Invoke(null, [security, FileSystemRights.WriteData])!);
        });
        await check("Jelli persisted preferences clamp coordinates, scale and enum values", () =>
        {
            var type = assembly.GetType("PredatorControlApp.JelliSettings")!;
            object settings = JsonSerializer.Deserialize("{\"OverlayScale\":2147483647,\"X\":-2147483648,\"Y\":2147483647,\"OverlayMode\":\"invalid\"}", type)!;
            object validated = type.GetMethod("Validate", Static)!.Invoke(null, [settings])!;
            return Task.FromResult((int)type.GetProperty("OverlayScale")!.GetValue(validated)! == 300 &&
                (int)type.GetProperty("X")!.GetValue(validated)! == -100000 && (int)type.GetProperty("Y")!.GetValue(validated)! == 100000 &&
                (string)type.GetProperty("OverlayMode")!.GetValue(validated)! == "default");
        });
        await check("failed battery probes are cached without invoking device APIs", () =>
        {
            var type = typeof(BatteryHealthMonitor);
            var stamp = type.GetField("_lastQueryTick", Static)!;
            var cached = type.GetField("_cachedInfo", Static)!;
            object? previousStamp = stamp.GetValue(null), previousCached = cached.GetValue(null);
            try
            {
                stamp.SetValue(null, Environment.TickCount64);
                cached.SetValue(null, null);
                return Task.FromResult(BatteryHealthMonitor.GetBatteryHealth() == null);
            }
            finally { stamp.SetValue(null, previousStamp); cached.SetValue(null, previousCached); }
        });
        await check("OEM reply parsers complete without a blocked UI synchronization context", () =>
        {
            foreach (var (name, bytes, args) in new (string, byte[], object?[])[]
            {
                ("AcerAgentProtocol", Encoding.UTF8.GetBytes("{\"result\":0}"), [null, 1U, CancellationToken.None]),
                ("NitroSense3Protocol", [1, 4, 0, 0, 0, 0, 0, 0, 0], [null, false, CancellationToken.None])
            })
            {
                using var stream = new DelayedStream(bytes);
                args[0] = stream;
                var prior = SynchronizationContext.Current;
                var context = new QueuedContext();
                SynchronizationContext.SetSynchronizationContext(context);
                try
                {
                    var task = (Task)assembly.GetType("PredatorControlApp." + name)!.GetMethod("ReadReply", Static)!.Invoke(null, args)!;
                    bool completed = task.Wait(1000);
                    context.Drain(); // Release a failing implementation's queued continuation before disposing its stream.
                    if (!completed || !task.IsCompletedSuccessfully || context.Posts != 0) return Task.FromResult(false);
                }
                finally { SynchronizationContext.SetSynchronizationContext(prior); }
            }
            return Task.FromResult(true);
        });
    }
}

internal sealed class DelayedStream(byte[] bytes) : MemoryStream(bytes)
{
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        await Task.Delay(10, token).ConfigureAwait(false);
        return Read(buffer.Span);
    }
}

internal sealed class QueuedContext : SynchronizationContext
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _pending = new();
    internal int Posts;
    public override void Post(SendOrPostCallback callback, object? state) { Interlocked.Increment(ref Posts); _pending.Enqueue((callback, state)); }
    internal void Drain() { while (_pending.TryDequeue(out var item)) item.Callback(item.State); }
}
