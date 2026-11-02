using System.Text.Json;

namespace PredatorControlApp;

internal interface IJelliBackend
{
    object State(JelliSettings settings);
    object Telemetry();
    Task<object?> Action(string id, JsonElement data);
    void SetOverlayVisible(bool visible);
    void OpenOverlaySettings();
    void ConfigureOverlay(JelliSettings settings);
    bool TryWriteColor(Color color);
    void RestoreColor();
    void Fallback();
    void SetJelliEnabled(bool enabled);
    void Exit();
}
