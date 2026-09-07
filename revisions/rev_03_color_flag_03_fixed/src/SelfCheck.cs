using System.Diagnostics;

namespace PredatorControlApp
{
    internal static class SelfCheck
    {
        [Conditional("DEBUG")]
        public static void Run()
        {
            var messy = new List<Point> { new(500, 500), new(10, -20), new(60, 60), new(60, 61) };
            var n = FanCurveGraph.Normalize(messy);

            Debug.Assert(n.Count == messy.Count, "Normalize dropped points");
            Debug.Assert(n[0].X == 30 && n[^1].X == 100, "Normalize must pin first/last temp");
            for (int i = 0; i < n.Count; i++)
            {
                Debug.Assert(n[i].Y is >= 0 and <= 100, "speed out of range");
                Debug.Assert(n[i].X is >= 30 and <= 100, "temp out of range");
                Debug.Assert(i == 0 || n[i - 1].X <= n[i].X, "Normalize must sort by temp");
            }

            Debug.Assert(FanCurveGraph.Normalize(null).Count >= 2, "null must yield default curve");
            Debug.Assert(FanCurveGraph.Normalize(new List<Point> { new(50, 50) }).Count >= 2,
                "single point must yield default curve");

            Debug.Assert(Form1.BatteryProfileValues.Length == 4, "battery table size");
            Debug.Assert(Form1.BatteryProfileValues[3] == 0x06, "battery Eco must map to 0x06");
            Debug.Assert(Form1.AcProfileValues.Length == 5, "AC table size");
            Debug.Assert(Form1.AcProfileValues[4] == 0x05, "AC Turbo must map to 0x05");

            CheckPowerLineDebounce();
            CheckPredatorKeyHook();
            CheckDisplayCcdTechnology();
            CheckThemes();
            CheckDebounceHelper();
        }

        private static void CheckPredatorKeyHook()
        {
            Debug.Assert(PredatorKeyHook.IsPredatorKey(PredatorKeyHook.VK_LAUNCH_APP2, 0, 0), "VK_LAUNCH_APP2 must match Predator key");
            Debug.Assert(PredatorKeyHook.IsModeKey(PredatorKeyHook.VK_F24, 0, 0), "VK_F24 must match Mode key");
            Debug.Assert(PredatorKeyHook.IsModeKey(0, PredatorKeyHook.ACER_SCANCODE_MODE_KEY, 0), "Scan code 0x76 must match Mode key");
            Debug.Assert(PredatorKeyHook.IsPredatorKey(0, PredatorKeyHook.ACER_SCANCODE_PREDATOR, 0), "Scan code 0x71 must match Predator key");
            Debug.Assert(PredatorKeyHook.IsPredatorKey(0, PredatorKeyHook.ACER_SCANCODE_PREDATOR, PredatorKeyHook.LLKHF_EXTENDED), "Extended scan code 0x71 must match");
            Debug.Assert(!PredatorKeyHook.IsPredatorKey(0x5B, 0x5B, 0), "Left Windows key must not match Predator key");
            Debug.Assert(!PredatorKeyHook.IsModeKey(0x5B, 0x5B, 0), "Left Windows key must not match Mode key");
            Debug.Assert(!PredatorKeyHook.IsPredatorKey(0x41, 0x1E, 0), "Key A must not match");
            Debug.Assert(!PredatorKeyHook.IsPredatorKey(0x20, 0x39, 0), "Spacebar must not match");
        }

        private static void CheckDisplayCcdTechnology()
        {
            Debug.Assert(DisplayCcdController.IsInternalTechnology(DisplayCcdController.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL), "INTERNAL must be internal");
            Debug.Assert(DisplayCcdController.IsInternalTechnology(DisplayCcdController.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED), "eDP must be internal");
            Debug.Assert(DisplayCcdController.IsInternalTechnology(DisplayCcdController.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS), "LVDS must be internal");
            Debug.Assert(!DisplayCcdController.IsInternalTechnology(DisplayCcdController.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI), "HDMI must not be internal");
            Debug.Assert(!DisplayCcdController.IsInternalTechnology(DisplayCcdController.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL), "External DP must not be internal");
            Debug.Assert(!DisplayCcdController.IsInternalTechnology(DisplayCcdController.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DVI), "DVI must not be internal");
        }

        private static void CheckThemes()
        {
            Debug.Assert(ThemeManager.PredatorTeal.Accent == Color.FromArgb(0, 200, 150), "Teal accent mismatch");
            Debug.Assert(ThemeManager.NitroCrimson.Accent == Color.FromArgb(230, 57, 70), "Nitro Crimson accent mismatch");
            Debug.Assert(ThemeManager.OledBlack.FormBg == Color.FromArgb(0, 0, 0), "OLED Black background must be pure black");
        }

        private static void CheckDebounceHelper()
        {
            int counter = 0;
            DebounceHelper.Debounce("test_counter", () => counter = 42, 5000);
            Debug.Assert(counter == 0, "Counter should not execute immediately");
            DebounceHelper.FlushAll();
            Debug.Assert(counter == 42, "FlushAll must immediately execute pending debounced work");
        }

        private static void CheckPowerLineDebounce()
        {
            bool? pending = null;
            int ticks = 0;
            bool? state = false;

            state = Form1.DebouncePowerLine(PowerLineStatus.Online, state, ref pending, ref ticks);
            Debug.Assert(state == false, "one Online sample must not flip state");

            state = Form1.DebouncePowerLine(PowerLineStatus.Offline, state, ref pending, ref ticks);
            Debug.Assert(state == false, "a contradicting sample must restart the count");

            state = Form1.DebouncePowerLine(PowerLineStatus.Online, state, ref pending, ref ticks);
            state = Form1.DebouncePowerLine(PowerLineStatus.Online, state, ref pending, ref ticks);
            Debug.Assert(state == true, "two agreeing samples must flip state");

            state = Form1.DebouncePowerLine(PowerLineStatus.Unknown, state, ref pending, ref ticks);
            Debug.Assert(state == true, "Unknown must not change state");
            Debug.Assert(ticks == 0 && pending == null, "Unknown must reset the count");

            bool? unknownStart = null;
            pending = null;
            ticks = 0;
            unknownStart = Form1.DebouncePowerLine(PowerLineStatus.Offline, unknownStart, ref pending, ref ticks);
            Debug.Assert(unknownStart == null, "unresolved state must stay unresolved after one sample");
            unknownStart = Form1.DebouncePowerLine(PowerLineStatus.Offline, unknownStart, ref pending, ref ticks);
            Debug.Assert(unknownStart == false, "unresolved state must resolve after two agreeing samples");

            CheckUpdater();
        }

        [Conditional("DEBUG")]
        private static void CheckUpdater()
        {
            Debug.Assert(Updater.TryParseTag("v1.3.1", out var tag) && tag == new Version(1, 3, 1), "tag must parse without the v");
            Debug.Assert(!Updater.TryParseTag("nightly", out _), "junk tags must be rejected");
            Debug.Assert(Updater.Current.Revision == -1, "Current must be Major.Minor.Build so tags compare cleanly");

            using var doc = System.Text.Json.JsonDocument.Parse("""
                {"assets":[
                    {"name":"PredatorControl-standalone-win-x64.exe","browser_download_url":"http://x/big.exe"},
                    {"name":"PredatorControl-win-x64.exe","browser_download_url":"http://x/light.exe"}]}
                """);
            var rel = doc.RootElement;
            Debug.Assert(Updater.PickAsset(rel, true) == "http://x/big.exe", "self-contained build must take the standalone asset");
            Debug.Assert(Updater.PickAsset(rel, false) == "http://x/light.exe", "framework-dependent build must take the light asset");

            using var noMatch = System.Text.Json.JsonDocument.Parse("""
                {"assets":[{"name":"only-light.exe","browser_download_url":"http://x/only.exe"}]}
                """);
            Debug.Assert(Updater.PickAsset(noMatch.RootElement, true) == "http://x/only.exe", "must fall back to any .exe");

            using var none = System.Text.Json.JsonDocument.Parse("""{"assets":[{"name":"notes.txt","browser_download_url":"http://x/n"}]}""");
            Debug.Assert(Updater.PickAsset(none.RootElement, true) == null, "non-exe assets must be ignored");

            string plain = Updater.Plain("## What's new\r\n\r\n**Bold.** text\r\n\r\n\r\n- one\r\n* two\r\n\r\n`code` and [link](http://x)");
            Debug.Assert(!plain.Contains('#') && !plain.Contains('*') && !plain.Contains('`'), "markdown syntax must be stripped");
            Debug.Assert(plain.StartsWith("What's new"), "headings must lose their hashes");
            Debug.Assert(plain.Contains("\u2022 one") && plain.Contains("\u2022 two"), "list markers must become bullets");
            Debug.Assert(plain.Contains("link") && !plain.Contains("http://x"), "link text must survive, url must not");
            string nl = Environment.NewLine;
            Debug.Assert(!plain.Contains(nl + nl + nl) && !plain.EndsWith(nl), "blank runs must collapse and not trail");
        }
    }
}
