using System.Drawing;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("PredatorControl.Overlay.Tests")]

namespace PredatorControlApp;

// Pure display rules shared by the native overlay and its hardware-free regression checks.
internal static class OverlayPolicy
{
    internal static bool IsFresh(long captured, long now, long frequency, double seconds = 3) =>
        captured > 0 && frequency > 0 && now >= captured && (now - captured) / (double)frequency <= seconds;

    internal static Point ClampPosition(Point position, Size size, Rectangle workArea) => new(
        Math.Clamp(position.X, workArea.Left, Math.Max(workArea.Left, workArea.Right - size.Width)),
        Math.Clamp(position.Y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - size.Height)));

    internal static float Scale(int dpi, int percent) => Math.Max(48, dpi) / 96f * Math.Clamp(percent, 50, 300) / 100f;

    internal static int? MemoryPercent(float? used, float? total) =>
        used is >= 0 && total is > 0 && float.IsFinite(used.Value) && float.IsFinite(total.Value) && used <= total
            ? (int)Math.Clamp(used.Value / total.Value * 100f, 0f, 100f) : null;

    internal static int? ClockMhz(int? value) => value is >= 100 and <= 10000 ? value : null;

    // Frames are ordered oldest to newest. A stopped game's last rate must expire.
    internal static double SampleFps(ReadOnlySpan<long> frames, long now, long frequency)
    {
        if (frames.Length < 2 || !IsFresh(frames[^1], now, frequency, 1.5)) return 0;
        long cutoff = now - frequency;
        long first = 0, last = 0;
        int count = 0;
        foreach (long frame in frames)
        {
            if (frame < cutoff || frame > now || frame <= last) continue;
            if (count == 0) first = frame;
            last = frame;
            count++;
        }
        return count >= 2 && last > first ? (count - 1) * (double)frequency / (last - first) : 0;
    }
}
