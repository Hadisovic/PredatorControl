using System.Text;
using System.Text.Json;

namespace PredatorControlApp;

internal static class AcerAgentProtocol
{
    // Byte-stream reads may split the ACER header, UTF-8 codepoints or JSON at any position.
    internal static async Task<string?> ReadReply(Stream stream, uint command, CancellationToken token)
    {
        byte[] buffer = new byte[32768];
        int count = 0;
        while (count < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(count), token);
            if (read == 0) return null;
            count += read;
            bool framed = buffer[0] == (byte)'A';
            if (framed && count < 8) continue;
            int offset = framed ? 8 : 0;
            if (framed && (!buffer.AsSpan(0, 4).SequenceEqual("ACER"u8) || BitConverter.ToUInt32(buffer, 4) != command)) return null;
            try
            {
                using var json = JsonDocument.Parse(buffer.AsMemory(offset, count - offset));
                if (json.RootElement.ValueKind != JsonValueKind.Object) return null;
                return new UTF8Encoding(false, true).GetString(buffer, offset, count - offset);
            }
            catch (JsonException) { /* Incomplete JSON; bounded by size, deadline and stream EOF. */ }
            catch (DecoderFallbackException) { return null; }
        }
        return null;
    }

    internal static int GpuMask(string? reply)
    {
        try
        {
            using var json = JsonDocument.Parse(reply ?? "");
            if (json.RootElement.ValueKind != JsonValueKind.Object) return 0;
            if (json.RootElement.TryGetProperty("result", out var result) &&
                !(result.ValueKind == JsonValueKind.String && result.GetString() == "0") &&
                !(result.ValueKind == JsonValueKind.Number && result.TryGetInt32(out int code) && code == 0)) return 0;
            if (json.RootElement.TryGetProperty("SUPPORT_GPU_MODE_CAPABILITY", out var mask) &&
                mask.ValueKind == JsonValueKind.Array && mask.GetArrayLength() == 1 && mask[0].ValueKind == JsonValueKind.Number && mask[0].TryGetInt32(out int value) && value is > 0 and <= 7) return value;
        }
        catch (JsonException) { }
        return 0;
    }
}
