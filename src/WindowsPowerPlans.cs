using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PredatorControlApp;

internal static class WindowsPowerPlans
{
    internal const string Backend = "windows-power-plan";
    internal static readonly Guid PowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");
    internal static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    internal static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    private static readonly string[] PowerSchemeRegistryPaths =
    [
        @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes",
        @"SYSTEM\CurrentControlSet\Control\Power\User\Default\PowerSchemes"
    ];

    [DllImport("powrprof.dll")]
    private static extern uint PowerEnumerate(IntPtr root, IntPtr scheme, IntPtr subgroup, uint access, uint index, [Out] byte[] buffer, ref uint size);
    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr root, in Guid scheme);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    internal static IReadOnlyCollection<Guid> MergeSchemeSources(IEnumerable<Guid> enumerated, IEnumerable<string>? registrySubkeys)
    {
        var ids = enumerated.Where(g => g != Guid.Empty).ToHashSet();
        if (registrySubkeys != null)
        {
            foreach (string subkey in registrySubkeys)
                if (Guid.TryParse(subkey, out var parsed) && parsed != Guid.Empty)
                    ids.Add(parsed);
        }
        return ids;
    }

    internal static WindowsPowerPlanDiscovery Describe(IEnumerable<Guid> installed, Guid? acer, Guid? active)
    {
        var ids = installed.ToHashSet();
        var modes = new List<PowerMode>();
        void Add(byte value, string label, Guid id) { if (ids.Contains(id)) modes.Add(new(value, label, WindowsScheme: id)); }
        Add(0, "Power saver", PowerSaver);
        Add(1, "Balanced", Balanced);
        if (acer.HasValue && acer != Guid.Empty && acer != PowerSaver && acer != Balanced && acer != HighPerformance)
            Add(2, "Acer Optimized", acer.Value);
        Add(4, "High performance", HighPerformance);
        string status = modes.Count == 0 ? "No supported Windows power plans are installed."
            : modes.Count == 1 ? $"Only {modes[0].Label} is installed. Other plans appear when installed; the app does not create them."
            : "Windows power plans (Power saver, Balanced, High performance) via PowerSetActiveScheme.";
        if (!active.HasValue) status = "Windows could not read the active power plan. Selection is unavailable.";
        return new(Array.AsReadOnly(modes.ToArray()), active, status);
    }

    // Read-only discovery. Includes Windows PowerSchemes registry definitions because Windows 10/11 hides inactive Power saver / High performance plans from PowerEnumerate until activated with PowerSetActiveScheme.
    internal static WindowsPowerPlanDiscovery Read()
    {
        try
        {
            var installed = new List<Guid>();
            for (uint i = 0; i < 256; i++)
            {
                byte[] bytes = new byte[16]; uint size = 16;
                uint error = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16, i, bytes, ref size);
                if (error == 259) // ERROR_NO_MORE_ITEMS
                {
                    var registryKeyNames = new List<string>();
                    foreach (string regPath in PowerSchemeRegistryPaths)
                    {
                        try
                        {
                            using var schemeRoot = Registry.LocalMachine.OpenSubKey(regPath);
                            if (schemeRoot != null)
                                registryKeyNames.AddRange(schemeRoot.GetSubKeyNames());
                        }
                        catch { }
                    }
                    Guid? acer = null;
                    try
                    {
                        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\OEM\NitroSense")
                            ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\OEM\NitroSense");
                        if (Guid.TryParse(key?.GetValue("AcerGUID") as string, out var id)) acer = id;
                    }
                    catch { /* Optional OEM plan registration cannot hide standard Windows plans. */ }
                    return Describe(MergeSchemeSources(installed, registryKeyNames), acer, ReadActive());
                }
                if (error != 0 || size != 16) return new([], null, "Windows power-plan enumeration failed. No changes were made.");
                installed.Add(new Guid(bytes));
            }
        }
        catch { }
        return new([], null, "Windows power-plan discovery is unavailable. No changes were made.");
    }

    private static Guid? ReadActive()
    {
        IntPtr pointer = IntPtr.Zero;
        try { return PowerGetActiveScheme(IntPtr.Zero, out pointer) == 0 && pointer != IntPtr.Zero ? Marshal.PtrToStructure<Guid>(pointer) : null; }
        finally { if (pointer != IntPtr.Zero) LocalFree(pointer); }
    }

    // Fake delegates exercise the same validation/readback policy without calling Windows.
    internal static HardwareOperationResult Apply(Guid wanted, WindowsPowerPlanDiscovery fresh, Func<Guid, uint> set, Func<Guid?> read)
    {
        if (!fresh.ActiveScheme.HasValue || wanted == Guid.Empty || !fresh.Modes.Any(m => m.WindowsScheme == wanted))
            return new("failed", "The requested Windows power plan is unavailable. Restart the app to refresh available plans.");
        try
        {
            if (set(wanted) != 0) return new("failed", "Windows rejected the power-plan change. The preference was not saved.");
        }
        catch { return new("failed", "The Windows power-plan call failed. The preference was not saved."); }
        try { if (read() == wanted) return new("confirmed", "Windows active power plan confirmed."); }
        catch { /* A completed setter with failed readback is unverified. */ }
        return new("unverified", "Windows acknowledged the request, but active-plan readback did not confirm it. The preference was not saved.");
    }

    internal static HardwareOperationResult Set(Guid wanted)
    {
        if (!HardwareWriteGate.InScope(HardwareFeature.Power, Backend)) return new("failed", "Power-plan change is not authorized.");
        return Apply(wanted, Read(), id => PowerSetActiveScheme(IntPtr.Zero, in id), ReadActive);
    }
}
