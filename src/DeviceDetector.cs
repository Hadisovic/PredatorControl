using System.Management;

namespace PredatorControlApp;

internal static class DeviceDetector
{
    // Each optional SMBIOS class fails independently. No identity defaults to Acer.
    internal static DeviceIdentity Read()
    {
        var cs = ReadClass("Win32_ComputerSystem");
        var product = ReadClass("Win32_ComputerSystemProduct");
        var bios = ReadClass("Win32_BIOS");
        var board = ReadClass("Win32_BaseBoard");
        var chassis = ReadClass("Win32_SystemEnclosure");
        string Get(Dictionary<string, object?> row, string field) => DeviceIdentity.Clean(row.GetValueOrDefault(field));
        return new()
        {
            Manufacturer = Get(cs, "Manufacturer"), Model = Get(cs, "Model"),
            ProductName = Get(product, "Name"), ProductVersion = Get(product, "Version"),
            ProductVendor = Get(product, "Vendor"), SystemFamily = Get(cs, "SystemFamily"),
            Sku = Get(cs, "SystemSKUNumber") is { Length: > 0 } sku ? sku : Get(product, "SKUNumber"),
            SerialNumber = Get(bios, "SerialNumber") is { Length: > 0 } s ? s : Get(product, "IdentifyingNumber"),
            BiosVersion = Get(bios, "SMBIOSBIOSVersion"), BiosReleaseDate = Get(bios, "ReleaseDate"),
            BoardManufacturer = Get(board, "Manufacturer"), BoardProduct = Get(board, "Product"), BoardVersion = Get(board, "Version"),
            ChassisTypes = chassis.GetValueOrDefault("ChassisTypes") is ushort[] types ? Array.AsReadOnly(types) : Array.Empty<ushort>()
        };
    }

    private static Dictionary<string, object?> ReadClass(string name)
    {
        var row = new Dictionary<string, object?>();
        try
        {
            using var searcher = new ManagementObjectSearcher(new ManagementScope(@"root\CIMV2"), new ObjectQuery($"SELECT * FROM {name}"),
                new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(2) });
            using var results = searcher.Get();
            using var iterator = results.GetEnumerator();
            if (iterator.MoveNext())
            using (var item = iterator.Current)
                foreach (PropertyData property in item.Properties) row[property.Name] = property.Value;
        }
        catch { /* Partial identity is expected on malformed/missing SMBIOS. */ }
        return row;
    }
}
