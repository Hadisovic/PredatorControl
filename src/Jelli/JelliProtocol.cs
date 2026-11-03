using System.Text.Json;

namespace PredatorControlApp;

internal sealed record JelliRequest(int Version, string Id, string Action, JsonElement Data);
internal static class JelliProtocol
{
    internal const string Origin = "https://jelli.predator.local/";
    internal static bool IsTrustedDocument(string source) => source == Origin || source == Origin + "index.html";

    internal static JelliRequest ReadRequest(string source, string json)
    {
        if (!IsTrustedDocument(source) || json.Length > 16384) throw new ArgumentException("Untrusted or oversized UI message.");
        var request = JsonSerializer.Deserialize<JelliRequest>(json, JelliSettings.Json);
        if (request == null || request.Version != 1 || string.IsNullOrWhiteSpace(request.Id) || request.Id.Length > 64 || request.Id.Any(char.IsControl))
            throw new ArgumentException("Unsupported protocol message.");
        var data = request.Data;
        bool valid = request.Action switch
        {
            "connect" or "savePosition" or "overlay.settings" or "legacy" or "exit" => data.ValueKind == JsonValueKind.Null,
            "surface" => data.ValueKind == JsonValueKind.String && data.GetString() is "compact" or "summary" or "dashboard" or "menu",
            "overlay" => data.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "settings" => data.ValueKind == JsonValueKind.Object,
            "control" => data.ValueKind == JsonValueKind.Object && data.TryGetProperty("id", out var id) &&
                id.ValueKind == JsonValueKind.String && id.GetString() is { Length: > 0 and <= 128 } && data.TryGetProperty("value", out _),
            "move" => Number(data, "x", -100000, 100000) && Number(data, "y", -100000, 100000),
            "color" => Channel(data, "r") && Channel(data, "g") && Channel(data, "b"),
            _ => false
        };
        if (!valid) throw new ArgumentException("Unknown action or invalid UI payload.");
        return request;
    }

    private static bool Number(JsonElement data, string name, double min, double max) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number) && double.IsFinite(number) && number >= min && number <= max;
    private static bool Channel(JsonElement data, string name) => Number(data, name, 0, 255) && data.GetProperty(name).TryGetInt32(out _);
}
internal sealed record JelliChoice(string Label, int Value, bool Enabled = true);
internal sealed record JelliControl(string Id, string Label, string Kind, object? Value,
    bool Enabled = true, JelliChoice[]? Choices = null, int Min = 0, int Max = 100, string? Accent = null);
internal sealed record JelliSection(string Id, string Label, JelliControl[] Controls);
internal sealed record JelliMenu(string Id, string Label, bool Enabled, bool Checked, JelliMenu[] Children);
