using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32;

namespace PredatorControlApp
{
    internal sealed record UpdateInfo(Version Version, string Tag, string Notes, string DownloadUrl, string? ChecksumUrl = null, string? TargetFileName = null);

    [SupportedOSPlatform("windows")]
    internal static class Updater
    {
        private static readonly string[] ReleasesEndpoints =
        {
            "https://api.github.com/repos/Hadisovic/PredatorControl/releases"
        };
        private const string RegPath = @"SOFTWARE\PredatorControl";
        private const StringComparison OIC = StringComparison.OrdinalIgnoreCase;

        #region Version

        internal static Version Current => Norm(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

        internal static string CurrentText => Current.ToString(3);

        internal static int BuildRevision => Version.TryParse(
            Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version,
            out var version) ? Math.Max(version.Revision, 0) : 0;


        private static Version Norm(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

        internal static bool TryParseTag(string tag, out Version v)
        {
            v = new Version(0, 0, 0);
            if (string.IsNullOrEmpty(tag)) return false;
            if (!Version.TryParse(tag.TrimStart('v', 'V').Trim(), out var parsed)) return false;
            v = Norm(parsed);
            return true;
        }

        #endregion

        #region Check

        internal static async Task<UpdateInfo?> CheckAsync()
        {
            using var http = NewTrustedClient(TimeSpan.FromSeconds(20));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            string json = string.Empty;
            foreach (var endpoint in ReleasesEndpoints)
            {
                try
                {
                    using var resp = await GetTrustedAsync(http, endpoint, deadline.Token);
                    if (resp.IsSuccessStatusCode)
                    {
                        var content = await ReadLimitedTextAsync(resp.Content, 4 * 1024 * 1024, deadline.Token);
                        if (!string.IsNullOrWhiteSpace(content) && content.TrimStart().StartsWith("["))
                        {
                            json = content;
                            break;
                        }
                    }
                }
                catch { }
            }

            if (string.IsNullOrEmpty(json))
                return null;

            using var doc = JsonDocument.Parse(json);

            var current = Current;
            UpdateInfo? newest = null;
            var notes = new StringBuilder();

            foreach (var rel in doc.RootElement.EnumerateArray())
            {
                if (rel.ValueKind != JsonValueKind.Object) continue;
                if (Flag(rel, "draft") || Flag(rel, "prerelease")) continue;
                if (!TryParseTag(Str(rel, "tag_name"), out var v) || v <= current) continue;

                notes.AppendLine($"--- {Str(rel, "name", $"v{v.ToString(3)}")} ---")
                     .AppendLine(Str(rel, "body").Trim().Replace("\r\n", "\n").Replace("\n", Environment.NewLine))
                     .AppendLine();

                if (newest == null || v > newest.Version)
                {
                    if (PickAssetInfo(rel, IsSelfContained) is var assetInfo && assetInfo != null)
                        newest = new UpdateInfo(v, Str(rel, "tag_name"), "", assetInfo.Value.url, assetInfo.Value.checksumUrl, assetInfo.Value.name);
                }
            }

            return newest == null ? null : newest with { Notes = notes.ToString().TrimEnd() };
        }

        private static bool Flag(JsonElement e, string name) =>
            e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

        private static string Str(JsonElement e, string name, string fallback = "") =>
            e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? fallback : fallback;

        private static bool IsSelfContained =>
            !RuntimeEnvironment.GetRuntimeDirectory().Replace('/', '\\').Contains(@"\dotnet\shared\", OIC);

        internal static (string url, string name, string? checksumUrl)? PickAssetInfo(JsonElement rel, bool wantSelfContained)
        {
            if (rel.ValueKind != JsonValueKind.Object || !rel.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;

            string? checksumUrl = null;
            foreach (var a in assets.EnumerateArray())
            {
                if (a.ValueKind != JsonValueKind.Object) continue;
                string name = Str(a, "name");
                if (name.Equals("checksums.txt", OIC) || name.EndsWith("checksums.txt", OIC))
                {
                    checksumUrl = Str(a, "browser_download_url");
                    break;
                }
            }

            (string url, string name)? fallback = null;
            // Only an application payload can use EXE replacement, never an installer.
            foreach (var a in assets.EnumerateArray())
            {
                if (a.ValueKind != JsonValueKind.Object) continue;
                string name = Str(a, "name"), url = Str(a, "browser_download_url");
                if (IsSafeAssetName(name) && name.StartsWith("PredatorControl-Portable-", OIC) && name.EndsWith(".exe", OIC) && url.Length > 0)
                    return (url, name, checksumUrl);
            }
            foreach (var a in assets.EnumerateArray())
            {
                if (a.ValueKind != JsonValueKind.Object) continue;
                string name = Str(a, "name");
                if (!IsSafeAssetName(name) || !name.EndsWith(".exe", OIC)) continue;
                if (name.Contains("setup", OIC) || name.Contains("installer", OIC)) continue;

                string url = Str(a, "browser_download_url");
                if (url.Length == 0) continue;

                bool selfContained = name.Contains("standalone", OIC) || name.Contains("self", OIC);
                if (selfContained == wantSelfContained) return (url, name, checksumUrl);
                fallback ??= (url, name);
            }
            if (fallback != null) return (fallback.Value.url, fallback.Value.name, checksumUrl);
            return null;
        }

        internal static string? PickAsset(JsonElement rel, bool wantSelfContained) =>
            PickAssetInfo(rel, wantSelfContained)?.url;

        #endregion

        #region Apply

        // GitHub serves release assets from github.com and from *.githubusercontent.com (objects/release-assets/releases).
        internal static bool IsTrustedUpdateUri(Uri uri) =>
            uri.IsAbsoluteUri && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
            string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) &&
            (uri.Host.Equals("github.com", OIC) || uri.Host.EndsWith(".githubusercontent.com", OIC) ||
                uri.Host.Equals("api.github.com", OIC) && uri.AbsolutePath == "/repos/Hadisovic/PredatorControl/releases");

        private static bool IsSafeAssetName(string name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 128 &&
            name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name is not ("." or "..");

        internal static void ValidateUpdateInfo(UpdateInfo info)
        {
            if (!TryParseTag(info.Tag, out var version) || info.Version != version || version <= Current)
                throw new InvalidOperationException("Update metadata must identify a newer release.");
            if (info.TargetFileName != null && !IsSafeAssetName(info.TargetFileName))
                throw new InvalidOperationException("Update metadata contains an unsafe asset name.");
            if (info.TargetFileName?.Contains("setup", OIC) == true || info.TargetFileName?.Contains("installer", OIC) == true)
                throw new InvalidOperationException("Installer packages must be installed manually; they cannot replace the application executable.");
        }

        internal static async Task CopyLimitedAsync(Stream source, Stream destination, long limit, CancellationToken token)
        {
            byte[] buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, token)) != 0)
            {
                total += read;
                if (total > limit) throw new IOException("The update response exceeds its size limit.");
                await destination.WriteAsync(buffer.AsMemory(0, read), token);
            }
        }

        private static async Task<string> ReadLimitedTextAsync(HttpContent content, long limit, CancellationToken token)
        {
            using var source = await content.ReadAsStreamAsync(token);
            using var destination = new MemoryStream();
            await CopyLimitedAsync(source, destination, limit, token);
            return Encoding.UTF8.GetString(destination.ToArray());
        }

        internal static bool SameSigningKey(X509Certificate2 installed, X509Certificate2 update) =>
            installed.PublicKey.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(update.PublicKey.ExportSubjectPublicKeyInfo());

        internal static void VerifyUpdateAuthenticity(string installed, string staged)
        {
            if (OemServiceManager.VerifyWinTrust(installed) != 0 || OemServiceManager.VerifyWinTrust(staged) != 0)
                throw new InvalidOperationException("Automatic installation requires trusted Authenticode signatures on both versions. Install unsigned releases manually.");
#pragma warning disable SYSLIB0057
            using var installedRaw = X509Certificate.CreateFromSignedFile(installed);
            using var updateRaw = X509Certificate.CreateFromSignedFile(staged);
#pragma warning restore SYSLIB0057
            using var installedCert = new X509Certificate2(installedRaw);
            using var updateCert = new X509Certificate2(updateRaw);
            if (!SameSigningKey(installedCert, updateCert))
                throw new InvalidOperationException("The update's signing key differs from the installed publisher. Install a verified publisher-key change manually.");
            // WinVerifyTrust here uses cached revocation data only; explicitly reject a revoked signer.
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
            chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
            chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(10);
            chain.Build(updateCert);
            if (chain.ChainStatus.Any(s => s.Status.HasFlag(X509ChainStatusFlags.Revoked)))
                throw new InvalidOperationException("The update's signing certificate has been revoked.");
        }

        private static HttpClient NewTrustedClient(TimeSpan timeout)
        {
            // Redirects are followed manually so every hop is checked against the allowlist.
            var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, CheckCertificateRevocationList = true }) { Timeout = timeout };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PredatorControl");
            return http;
        }

        private static async Task<HttpResponseMessage> GetTrustedAsync(HttpClient http, string url, CancellationToken token)
        {
            var uri = new Uri(url);
            for (int hop = 0; hop < 5; hop++)
            {
                if (!IsTrustedUpdateUri(uri))
                    throw new InvalidOperationException("Update download refused: untrusted URL or redirect target '" + uri.Host + "'.");
                var resp = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
                if ((int)resp.StatusCode is >= 300 and < 400 && resp.Headers.Location is Uri next)
                {
                    resp.Dispose();
                    uri = next.IsAbsoluteUri ? next : new Uri(uri, next);
                    continue;
                }
                try { resp.EnsureSuccessStatusCode(); return resp; }
                catch { resp.Dispose(); throw; }
            }
            throw new InvalidOperationException("Update download refused: too many redirects.");
        }

        internal static async Task ApplyAsync(UpdateInfo info)
        {
            ValidateUpdateInfo(info);
            if (!Uri.TryCreate(info.DownloadUrl, UriKind.Absolute, out var downloadUri))
                throw new InvalidOperationException("Update URL is not a valid absolute URI: " + info.DownloadUrl);

            if (!string.Equals(downloadUri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Update download refused: URL must use HTTPS (got '" + downloadUri.Scheme + "').");

            if (!IsTrustedUpdateUri(downloadUri))
                throw new InvalidOperationException("Update download refused: untrusted host '" + downloadUri.Host + "'.");
            if (!Uri.TryCreate(info.ChecksumUrl, UriKind.Absolute, out var checksumUri) || !IsTrustedUpdateUri(checksumUri))
                throw new InvalidOperationException("Mandatory checksum verification failed: checksum URL is missing or untrusted.");

            string target = Environment.ProcessPath ?? Application.ExecutablePath;
            if (!SecureLaunch.IsAdminOnly(target))
                throw new InvalidOperationException("Automatic updates require the installed copy in a protected folder. Download the update manually or relaunch the installed copy.");
            if (OemServiceManager.VerifyWinTrust(target) != 0)
                throw new InvalidOperationException("This build is unsigned. Download and install updates manually until signed releases are available.");
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(10));

            // Unique staging directory, restricted to this user / SYSTEM / Administrators,
            // so other local users or processes can't pre-create or swap the staged files.
            string stageDir = CreateStagingDirectory();
            string staged = Path.Combine(stageDir, "PredatorControl-update.exe");
            string script = Path.Combine(stageDir, "apply-update.cmd");

            try
            {
                // Validate the checksum metadata before spending bandwidth on the download.
                if (string.IsNullOrEmpty(info.ChecksumUrl))
                    throw new InvalidOperationException("Mandatory checksum verification failed: no checksum URL provided with update metadata. Staged file aborted.");

                string stagedHash;
                // Download and hash through ONE exclusive handle so nothing can modify the file between download and verification.
                using (var http = NewTrustedClient(TimeSpan.FromMinutes(10)))
                {
                    using var response = await GetTrustedAsync(http, info.DownloadUrl, deadline.Token);
                    using var src = await response.Content.ReadAsStreamAsync(deadline.Token);
                    using var dst = new FileStream(staged, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                    await CopyLimitedAsync(src, dst, 512L * 1024 * 1024, deadline.Token);
                    await dst.FlushAsync(deadline.Token);

                    if (dst.Length < 100_000)
                        throw new IOException("Downloaded file looks truncated.");

                    dst.Position = 0;
                    using var sha = System.Security.Cryptography.SHA256.Create();
                    stagedHash = Convert.ToHexString(await sha.ComputeHashAsync(dst, deadline.Token)).ToLowerInvariant();
                }

                string checksumData;
                using (var http = NewTrustedClient(TimeSpan.FromMinutes(2)))
                {
                    using var response = await GetTrustedAsync(http, info.ChecksumUrl, deadline.Token);
                    checksumData = await ReadLimitedTextAsync(response.Content, 256 * 1024, deadline.Token);
                }

                string? expectedHash = null;
                string targetName = info.TargetFileName ?? Path.GetFileName(new Uri(info.DownloadUrl).LocalPath);
                foreach (var line in checksumData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        string lineHash = parts[0].Trim().ToLowerInvariant();
                        // sha256sum marks binary-mode entries with a leading '*'
                        string lineFile = parts[parts.Length - 1].Trim().TrimStart('*');
                        if (lineFile.Equals(targetName, OIC))
                        {
                            expectedHash = lineHash;
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(expectedHash))
                    throw new InvalidOperationException($"Mandatory checksum verification failed: no checksum found for '{targetName}' in release checksums. Staged file aborted.");

                if (!stagedHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Checksum verification failed: expected {expectedHash}, got {stagedHash}. Staged file aborted.");

                // The value is embedded in a batch script, so it must be strictly 64 hex characters.
                if (!Regex.IsMatch(stagedHash, "^[0-9a-f]{64}$"))
                    throw new InvalidOperationException("Checksum verification failed: unexpected hash format.");

                VerifyUpdateAuthenticity(target, staged);
                if (!Version.TryParse(FileVersionInfo.GetVersionInfo(staged).FileVersion, out var binaryVersion) || Norm(binaryVersion) != info.Version)
                    throw new InvalidOperationException("The signed executable version does not match the release metadata.");

                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(RegPath);
                    key.SetValue("UpdateNotes", info.Notes);
                    key.SetValue("UpdateNotesVersion", info.Version.ToString(3));
                }
                catch { }

                // Persistent backup of the version being replaced (kept after a successful update for manual rollback).
                string backupDir = SecureSubdirectory("backup");
                string backup = Path.Combine(backupDir, "PredatorControl.previous.exe");

                File.WriteAllText(script, BuildUpdateScript(Environment.ProcessId, staged, target, backup, stageDir, stagedHash),
                    new UTF8Encoding(false));

                using var helper = Process.Start(new ProcessStartInfo(SecureLaunch.System32("cmd.exe"), $"/d /v:off /c \"{script}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WorkingDirectory = Environment.SystemDirectory
                }) ?? throw new InvalidOperationException("The update helper could not be started.");
            }
            catch
            {
                // Anything that fails before the script takes over: don't leave staged binaries behind.
                try { Directory.Delete(stageDir, true); } catch { }
                throw;
            }
        }

        // Elevated handoff must fail closed if a protected location cannot be established.
        private static string SecureSubdirectory(string name)
        {
            SecureLaunch.CreateAdminOnlyDirectory(SecureLaunch.ProtectedDataDir);
            return SecureLaunch.CreateAdminOnlyDirectory(Path.Combine(SecureLaunch.ProtectedDataDir, name));
        }

        private static string CreateStagingDirectory()
        {
            string root = SecureSubdirectory("update");
            // Do not delete other staging directories: an earlier updater may still be using them.
            return SecureLaunch.CreateAdminOnlyDirectory(Path.Combine(root, Guid.NewGuid().ToString("N")));
        }

        private static string BuildUpdateScript(int pid, string staged, string target, string backup, string stageDir, string sha256)
        {
            if (pid <= 0 || !Regex.IsMatch(sha256, "\\A[0-9a-fA-F]{64}\\z")) throw new ArgumentException("Invalid update handoff.");
            foreach (string path in new[] { staged, target, backup, stageDir })
                if (!Path.IsPathFullyQualified(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                    throw new ArgumentException("Invalid update handoff path.");
            // '%' is special in batch files; none of these paths normally contain it, but escape to be safe.
            static string E(string s) => s.Replace("%", "%%");

            return """
                @echo off
                setlocal EnableExtensions DisableDelayedExpansion
                set "SYS=@@SYSTEM@@"
                set "PATH=%SYS%"
                "%SYS%\chcp.com" 65001 >nul
                set "PID=@@PID@@"
                set "STAGED=@@STAGED@@"
                set "TARGET=@@TARGET@@"
                set "BACKUP=@@BACKUP@@"
                set "STAGEDIR=@@STAGEDIR@@"
                set "EXPECTED=@@HASH@@"

                set /a WAITS=0
                :wait
                "%SYS%\tasklist.exe" /fi "PID eq %PID%" /nh | "%SYS%\find.exe" "%PID%" >nul
                if not errorlevel 1 (
                    set /a WAITS+=1
                    if %WAITS% GEQ 60 goto fail
                    "%SYS%\timeout.exe" /t 1 /nobreak >nul
                    goto wait
                )

                rem Re-verify the staged file immediately before it is used.
                set "ACTUAL="
                for /f "skip=1 tokens=* delims=" %%H in ('"%SYS%\certutil.exe" -hashfile "%STAGED%" SHA256') do if not defined ACTUAL set "ACTUAL=%%H"
                if not defined ACTUAL goto fail
                set "ACTUAL=%ACTUAL: =%"
                if /i not "%ACTUAL%"=="%EXPECTED%" goto fail

                rem Keep a recoverable copy of the current version.
                copy /y "%TARGET%" "%BACKUP%" >nul 2>&1
                if errorlevel 1 goto fail

                set /a TRIES=0
                :swap
                move /y "%STAGED%" "%TARGET%" >nul 2>&1
                if not errorlevel 1 goto done
                set /a TRIES+=1
                if %TRIES% GEQ 15 goto restore
                "%SYS%\timeout.exe" /t 1 /nobreak >nul
                goto swap

                :restore
                rem Replacement failed: make sure the original is intact, then relaunch it.
                if not exist "%TARGET%" copy /y "%BACKUP%" "%TARGET%" >nul 2>&1
                goto launch

                :fail
                rem Nothing was replaced; relaunch the existing version.
                goto launch

                :done
                :launch
                if exist "%TARGET%" start "" "%TARGET%"
                cd /d "%SYS%"
                (goto) 2>nul & rd /s /q "%STAGEDIR%"
                """
                .Replace("@@PID@@", pid.ToString())
                .Replace("@@SYSTEM@@", E(Environment.SystemDirectory))
                .Replace("@@STAGED@@", E(staged))
                .Replace("@@TARGET@@", E(target))
                .Replace("@@BACKUP@@", E(backup))
                .Replace("@@STAGEDIR@@", E(stageDir))
                .Replace("@@HASH@@", sha256);
        }
        #endregion

        #region Post-update notes

        internal static void ShowPendingNotes(IWin32Window owner)
        {
            string? notes, version;
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegPath);
                notes = key.GetValue("UpdateNotes") as string;
                version = key.GetValue("UpdateNotesVersion") as string;
                if (string.IsNullOrWhiteSpace(notes)) return;
                key.DeleteValue("UpdateNotes", false);
                key.DeleteValue("UpdateNotesVersion", false);
            }
            catch { return; }

            if (Version.TryParse(version, out var v) && Norm(v) > Current)
            {
                MessageBox.Show(owner, $"The update to v{version} could not be installed - you're still on v{CurrentText}.\n\n" +
                    "This usually means the app file was locked or write-protected. Try again, or download it manually from GitHub.",
                    "Predator Control", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ShowNotes(owner, $"Updated to v{version}", "What's new", notes!, confirm: false);
        }

        #endregion

        #region Dialog

        private static readonly Color FormBg = Color.FromArgb(22, 22, 26);
        private static readonly Color TitleBarBg = Color.FromArgb(18, 18, 21);
        private static readonly Color PanelBg = Color.FromArgb(30, 30, 34);
        private static readonly Color SeparatorColor = Color.FromArgb(40, 40, 44);
        private static readonly Color TitleTextColor = Color.FromArgb(200, 200, 205);
        private static readonly Color CloseHoverColor = Color.FromArgb(220, 50, 50);
        private static readonly Color BodyColor = Color.FromArgb(165, 165, 175);
        private static readonly Color TextColor = Color.FromArgb(210, 210, 215);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        private static void Drag(Form dlg, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            ReleaseCapture();
            SendMessage(dlg.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
        }

        internal static string Plain(string markdown)
        {
            var sb = new StringBuilder();
            bool blank = false;

            foreach (string raw in markdown.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.TrimEnd();

                if (line.Length == 0)
                {
                    blank = sb.Length > 0;
                    continue;
                }

                if (blank) sb.AppendLine();
                blank = false;

                line = Regex.Replace(line, @"^\s{0,3}#{1,6}\s*", "");
                line = Regex.Replace(line, @"^\s*[-*+]\s+", "\u2022 ");
                line = Regex.Replace(line, @"\[([^\]]+)\]\([^)]*\)", "$1");
                line = Regex.Replace(line, @"`([^`]*)`", "$1");
                line = line.Replace("**", "").Replace("__", "");

                sb.AppendLine(line);
            }

            return sb.ToString().Trim();
        }

        internal static bool ShowNotes(IWin32Window owner, string title, string subtitle, string notes, bool confirm)
        {
            const int W = 520, Bar = 36, Pad = 20, BtnW = 120, BtnH = 34, BodyH = 300;
            int bodyY = Bar + 72;
            int btnY = bodyY + BodyH + Pad;

            using var dlg = new Form
            {
                Text = title,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.None,
                ShowInTaskbar = false,
                KeyPreview = true,
                BackColor = FormBg,
                ClientSize = new Size(W, btnY + BtnH + Pad),
                DialogResult = DialogResult.Cancel
            };

            var bar = new Panel { Location = Point.Empty, Size = new Size(W, Bar), BackColor = TitleBarBg };
            bar.MouseDown += (s, e) => Drag(dlg, e);
            dlg.Controls.Add(bar);

            var lblTitle = new Label
            {
                Text = title,
                Location = new Point(14, 9),
                AutoSize = true,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = TitleTextColor,
                BackColor = Color.Transparent
            };
            lblTitle.MouseDown += (s, e) => Drag(dlg, e);
            bar.Controls.Add(lblTitle);

            var lblClose = new Label
            {
                Text = "\u2715",
                Location = new Point(W - Bar, 0),
                Size = new Size(Bar, Bar),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 10f),
                ForeColor = TitleTextColor,
                Cursor = Cursors.Hand
            };
            lblClose.MouseEnter += (s, e) => lblClose.ForeColor = CloseHoverColor;
            lblClose.MouseLeave += (s, e) => lblClose.ForeColor = TitleTextColor;
            lblClose.Click += (s, e) => dlg.Close();
            bar.Controls.Add(lblClose);

            dlg.Controls.Add(new Panel
            {
                Location = new Point(0, Bar),
                Size = new Size(W, 1),
                BackColor = SeparatorColor
            });

            dlg.Controls.Add(new Label
            {
                Text = subtitle,
                Location = new Point(Pad, Bar + 16),
                Size = new Size(W - Pad * 2, 48),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = TextColor,
                BackColor = Color.Transparent
            });

            var txt = new TextBox
            {
                Text = Plain(notes),
                Location = new Point(Pad, bodyY),
                Size = new Size(W - Pad * 2, BodyH),
                Multiline = true,
                ReadOnly = true,
                TabStop = false,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = PanelBg,
                ForeColor = BodyColor,
                Font = new Font("Segoe UI", 9.5f)
            };
            dlg.Controls.Add(txt);

            var btnPrimary = new PredatorButton
            {
                Text = confirm ? "Update Now" : "Close",
                Location = new Point(W - Pad - BtnW, btnY),
                Size = new Size(BtnW, BtnH)
            };
            btnPrimary.Click += (s, e) => { dlg.DialogResult = DialogResult.OK; dlg.Close(); };
            dlg.Controls.Add(btnPrimary);

            if (confirm)
            {
                var btnLater = new PredatorButton
                {
                    Text = "Later",
                    Location = new Point(W - Pad - BtnW * 2 - 12, btnY),
                    Size = new Size(BtnW, BtnH)
                };
                btnLater.Click += (s, e) => dlg.Close();
                dlg.Controls.Add(btnLater);
            }

            dlg.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) dlg.Close(); };
            dlg.Shown += (s, e) =>
            {
                txt.Select(0, 0);
                dlg.ActiveControl = btnPrimary;
            };

            return dlg.ShowDialog(owner) == DialogResult.OK;
        }

        #endregion
    }
}
