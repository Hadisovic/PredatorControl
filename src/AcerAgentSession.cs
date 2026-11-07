using System.Net.Sockets;
using System.Text;
using Microsoft.Win32;

namespace PredatorControlApp;

public static partial class AcerAgentClient
{
    // Accessed only while _sendLock is held. Authentication belongs to the
    // connection, so capability queries and readbacks reuse that same connection.
    private static TcpClient? _reviewedConnection;
    private static uint _reviewedServerPid, _reviewedServicePid;
    private static DateTimeOffset _reviewedExpires;
    internal static string? LastReadFailure { get; private set; }

    private static void ResetReviewedSession()
    {
        _reviewedConnection?.Dispose();
        _reviewedConnection = null;
        _reviewedServerPid = _reviewedServicePid = 0;
        _reviewedExpires = default;
    }

    private static byte[] Frame(uint packetId, string json)
    {
        byte[] payload = Encoding.UTF8.GetBytes(json);
        if (payload.Length > 16384) throw new ArgumentException("OEM request exceeds the supported size.");
        byte[] frame = new byte[8 + payload.Length];
        MagicBytes.CopyTo(frame, 0);
        BitConverter.GetBytes(packetId).CopyTo(frame, 4);
        payload.CopyTo(frame, 8);
        return frame;
    }

    private static async Task<string?> SessionExchangeAsync(uint packetId, string json, CancellationToken token)
    {
        if (_reviewedConnection == null || !AcerAgentEndpoint.TcpConnectedToTrustedServer(_reviewedConnection.Client, TcpPort, _reviewedServerPid)) return null;
        if (packetId == CMD_SET_DEVICE_DATA && !IsAuthorizedWrite()) return null;
        var stream = _reviewedConnection.GetStream();
        await stream.WriteAsync(Frame(packetId, json), token).ConfigureAwait(false);
        return await AcerAgentProtocol.ReadReply(stream, packetId, token).ConfigureAwait(false);
    }

    private static async Task<string?> SendReviewedSessionAsync(uint packetId, string json, ServiceEvidence service, int timeoutMs)
    {
        using var deadline = new CancellationTokenSource(timeoutMs);
        try
        {
            if (_reviewedConnection != null && (_reviewedServicePid != service.ProcessId || DateTimeOffset.UtcNow >= _reviewedExpires ||
                !AcerAgentEndpoint.TcpConnectedToTrustedServer(_reviewedConnection.Client, TcpPort, _reviewedServerPid))) ResetReviewedSession();
            if (_reviewedConnection == null)
            {
                if (!AcerAgentEndpoint.TcpTrusted(TcpPort, out uint listenerPid))
                { LastReadFailure = "The trusted OEM TCP endpoint is unavailable."; return null; }
                using var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
                if (bios?.GetValue("SystemProductName") is not string { Length: > 0 and <= 256 } model)
                { LastReadFailure = "The OEM authentication model value is unavailable."; return null; }
                _reviewedConnection = new TcpClient();
                _reviewedServerPid = listenerPid;
                _reviewedServicePid = service.ProcessId;
                await _reviewedConnection.ConnectAsync(Host, TcpPort, deadline.Token).ConfigureAwait(false);
                var started = DateTimeOffset.Now; // OEM client uses local hour/day and Unix time from the same Date.
                string? challengeReply = await SessionExchangeAsync(CMD_INITIALIZATION, AcerAgentAuthentication.RequestChallengeJson, deadline.Token).ConfigureAwait(false);
                if (!AcerAgentAuthentication.TryParseChallenge(challengeReply, out var challenge))
                { LastReadFailure = "The reviewed OEM authentication challenge is unavailable or malformed."; ResetReviewedSession(); return null; }
                string request = AcerAgentAuthentication.CreateChallengeRequest(model, started, challenge!);
                string? authenticated = await SessionExchangeAsync(CMD_INITIALIZATION, request, deadline.Token).ConfigureAwait(false);
                if (!AcerAgentAuthentication.IsAuthenticated(authenticated))
                { LastReadFailure = "The OEM service did not accept client authentication."; ResetReviewedSession(); return null; }
                _reviewedExpires = started.AddSeconds(challenge!.ExpireSeconds);
                if (DateTimeOffset.UtcNow >= _reviewedExpires)
                { LastReadFailure = "The OEM authentication challenge expired."; ResetReviewedSession(); return null; }
            }
            string? reply = await SessionExchangeAsync(packetId, json, deadline.Token).ConfigureAwait(false);
            LastReadFailure = reply == null ? "OEM reply was unavailable, truncated or incompatible; no command was retried." : null;
            if (reply == null) ResetReviewedSession();
            return reply;
        }
        catch
        {
            LastReadFailure = "The reviewed OEM session failed or timed out; no command was retried.";
            ResetReviewedSession();
            return null;
        }
    }
}
