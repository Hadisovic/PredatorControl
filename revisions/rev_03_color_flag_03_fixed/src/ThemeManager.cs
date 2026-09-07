using Microsoft.Win32;

namespace PredatorControlApp
{
    public enum ThemeMode
    {
        PredatorTeal = 0,
        NitroCrimson = 1,
        OledBlack = 2
    }

    public class ThemePalette
    {
        public ThemeMode Mode { get; init; }
        public string Name { get; init; } = "";
        public Color Accent { get; init; }
        public Color FormBg { get; init; }
        public Color TitleBarBg { get; init; }
        public Color CardBg { get; init; }
        public Color CardHover { get; init; }
        public Color CardActive { get; init; }
        public Color Border { get; init; }
        public Color BorderHover { get; init; }
        public Color Separator { get; init; }
        public Color TextPrimary { get; init; }
        public Color TextSecondary { get; init; }
        public Color TrackBg { get; init; }
    }

    public static class ThemeManager
    {
        public static readonly ThemePalette PredatorTeal = new()
        {
            Mode = ThemeMode.PredatorTeal,
            Name = "Predator Teal",
            Accent = Color.FromArgb(0, 200, 150),
            FormBg = Color.FromArgb(22, 22, 26),
            TitleBarBg = Color.FromArgb(18, 18, 21),
            CardBg = Color.FromArgb(37, 37, 40),
            CardHover = Color.FromArgb(48, 48, 52),
            CardActive = Color.FromArgb(20, 50, 45),
            Border = Color.FromArgb(60, 60, 66),
            BorderHover = Color.FromArgb(80, 80, 88),
            Separator = Color.FromArgb(40, 40, 44),
            TextPrimary = Color.FromArgb(255, 255, 255),
            TextSecondary = Color.FromArgb(120, 120, 135),
            TrackBg = Color.FromArgb(55, 55, 62)
        };

        public static readonly ThemePalette NitroCrimson = new()
        {
            Mode = ThemeMode.NitroCrimson,
            Name = "Nitro Crimson",
            Accent = Color.FromArgb(230, 57, 70),
            FormBg = Color.FromArgb(24, 19, 20),
            TitleBarBg = Color.FromArgb(19, 14, 15),
            CardBg = Color.FromArgb(40, 33, 35),
            CardHover = Color.FromArgb(56, 46, 49),
            CardActive = Color.FromArgb(72, 24, 29),
            Border = Color.FromArgb(74, 56, 60),
            BorderHover = Color.FromArgb(107, 78, 84),
            Separator = Color.FromArgb(54, 36, 39),
            TextPrimary = Color.FromArgb(255, 255, 255),
            TextSecondary = Color.FromArgb(163, 128, 134),
            TrackBg = Color.FromArgb(58, 43, 46)
        };

        public static readonly ThemePalette OledBlack = new()
        {
            Mode = ThemeMode.OledBlack,
            Name = "OLED Stealth Black",
            Accent = Color.FromArgb(0, 200, 150),
            FormBg = Color.FromArgb(0, 0, 0),
            TitleBarBg = Color.FromArgb(0, 0, 0),
            CardBg = Color.FromArgb(18, 18, 20),
            CardHover = Color.FromArgb(32, 32, 36),
            CardActive = Color.FromArgb(14, 41, 34),
            Border = Color.FromArgb(40, 40, 45),
            BorderHover = Color.FromArgb(62, 62, 70),
            Separator = Color.FromArgb(31, 31, 36),
            TextPrimary = Color.FromArgb(245, 245, 247),
            TextSecondary = Color.FromArgb(120, 120, 133),
            TrackBg = Color.FromArgb(35, 35, 40)
        };

        private static ThemePalette _current = PredatorTeal;
        public static ThemePalette Current => _current;

        public static event Action? ThemeChanged;

        static ThemeManager()
        {
            LoadSavedTheme();
        }

        public static void SetTheme(ThemeMode mode)
        {
            _current = mode switch
            {
                ThemeMode.NitroCrimson => NitroCrimson,
                ThemeMode.OledBlack => OledBlack,
                _ => PredatorTeal
            };

            SaveTheme(mode);
            ThemeChanged?.Invoke();
        }

        private static void LoadSavedTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PredatorControl");
                if (key?.GetValue("Theme") is int val)
                {
                    _current = (ThemeMode)val switch
                    {
                        ThemeMode.NitroCrimson => NitroCrimson,
                        ThemeMode.OledBlack => OledBlack,
                        _ => PredatorTeal
                    };
                }
            }
            catch { }
        }

        private static void SaveTheme(ThemeMode mode)
        {
            DebounceHelper.Debounce("Theme", () =>
            {
                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\PredatorControl");
                    key.SetValue("Theme", (int)mode);
                }
                catch { }
            }, 100);
        }
    }
}
