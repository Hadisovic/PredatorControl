using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PredatorControlApp;

// PredatorSense 5.2.109 main.js: PSSdkAdapter.authenticateWithChallenge,
// generateChallengeKey and buildHS256JWT. Pure protocol formatting only: no
// registry, service or firmware access and no tokens or key material are logged.
internal static class AcerAgentAuthentication
{
    internal const string RequestChallengeJson = "{\"Function\":\"Request_Challenge\"}";
    internal sealed record Challenge(string Nonce, int ExpireSeconds);
    private static readonly JsonSerializerOptions CompactJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    internal static bool TryParseChallenge(string? reply, out Challenge? challenge)
    {
        challenge = null;
        using var document = ParseReply(reply, "Request_Challenge");
        if (document == null) return false;
        var root = document.RootElement;
        if (!root.TryGetProperty("Nonce", out var nonce) || nonce.ValueKind != JsonValueKind.String ||
            !ValidNonce(nonce.GetString()) || !root.TryGetProperty("Expire", out var expires) ||
            expires.ValueKind != JsonValueKind.Number || !expires.TryGetInt32(out int seconds) || seconds <= 0) return false;
        challenge = new(nonce.GetString()!, seconds);
        return true;
    }

    internal static bool IsAuthenticated(string? reply)
    {
        using var document = ParseReply(reply, "Challenge");
        return document != null;
    }

    internal static string CreateChallengeRequest(string model, DateTimeOffset initialTime, Challenge challenge)
    {
        if (string.IsNullOrEmpty(model) || model.Length > 256 || model.Any(char.IsControl))
            throw new ArgumentException("An exact BIOS product name is required.", nameof(model));
        if (challenge == null || !ValidNonce(challenge.Nonce) || challenge.ExpireSeconds <= 0)
            throw new ArgumentException("A valid OEM challenge is required.", nameof(challenge));

        // JS Date.getHours()/getDate() use local wall time, while getTime() uses
        // Unix time. Keep the caller's captured offset; converting to UTC here
        // changes the OEM derivation near midnight or in a different timezone.
        string inverted = string.Create(model.Length, model, static (target, source) =>
        {
            for (int i = 0; i < source.Length; i++)
                target[i] = source[i] is >= 'a' and <= 'z' ? (char)(source[i] - 32) :
                    source[i] is >= 'A' and <= 'Z' ? (char)(source[i] + 32) : source[i];
        });
        string modelBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(inverted));
        string localFactor = ((initialTime.Hour + 21) * initialTime.Day).ToString(CultureInfo.InvariantCulture);
        string input = Convert.ToBase64String(Encoding.UTF8.GetBytes(modelBase64 + localFactor));
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
        string key = digest.Substring(7, 15) + digest[^1] + digest[^2] + digest[^3];

        long issuedAt = initialTime.ToUnixTimeSeconds();
        string header = Base64Url(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
        string payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
            { iat = issuedAt, exp = issuedAt + challenge.ExpireSeconds, nonce = challenge.Nonce }, CompactJson));
        string content = header + "." + payload;
        byte[] keyBytes = Encoding.UTF8.GetBytes(key);
        string signature;
        try { signature = Base64Url(HMACSHA256.HashData(keyBytes, Encoding.ASCII.GetBytes(content))); }
        finally { CryptographicOperations.ZeroMemory(keyBytes); }
        return JsonSerializer.Serialize(new { Function = "Challenge", Nonce = challenge.Nonce, Token = content + "." + signature }, CompactJson);
    }

    private static bool ValidNonce(string? nonce) => !string.IsNullOrEmpty(nonce) && nonce.Length <= 4096 && !nonce.Any(char.IsControl);

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static JsonDocument? ParseReply(string? reply, string request)
    {
        if (string.IsNullOrEmpty(reply) || reply.Length > 16384) return null;
        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(reply, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object && !root.EnumerateObject().GroupBy(p => p.Name).Any(g => g.Count() != 1) &&
                root.TryGetProperty("request", out var echo) && echo.ValueKind == JsonValueKind.String && echo.GetString() == request &&
                root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.String && result.GetString() == "0") return document;
        }
        catch (JsonException) { }
        document?.Dispose();
        return null;
    }
}
