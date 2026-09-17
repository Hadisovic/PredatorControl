using System.Text.Json;

namespace PredatorControlApp;

internal sealed record JelliRequest(int Version, string Id, string Action, JsonElement Data);
internal sealed record JelliChoice(string Label, int Value, bool Enabled = true);
internal sealed record JelliControl(string Id, string Label, string Kind, object? Value,
    bool Enabled = true, JelliChoice[]? Choices = null, int Min = 0, int Max = 100, string? Accent = null);
internal sealed record JelliSection(string Id, string Label, JelliControl[] Controls);
internal sealed record JelliMenu(string Id, string Label, bool Enabled, bool Checked, JelliMenu[] Children);
