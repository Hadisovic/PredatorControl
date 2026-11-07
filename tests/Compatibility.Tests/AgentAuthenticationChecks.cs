using PredatorControlApp;
using System.Text.Json;

internal static class AgentAuthenticationChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const string challengeReply = "{\"request\":\"Request_Challenge\",\"result\":\"0\",\"Nonce\":\"fixture-nonce-001\",\"Expire\":120}";
        check(AcerAgentAuthentication.TryParseChallenge(challengeReply, out var challenge) && challenge is { ExpireSeconds: 120, Nonce: "fixture-nonce-001" },
            "OEM challenge accepts exact top-level nonce/expiry and success echo");

        // Independent golden values from a Python hashlib/hmac implementation of
        // the inspected production JS, using synthetic models, dates and nonces.
        // No real host key, nonce, token or OEM process participates in these tests.
        const string token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpYXQiOjE4OTQ3MDUzODIsImV4cCI6MTg5NDcwNTUwMiwibm9uY2UiOiJmaXh0dXJlLW5vbmNlLTAwMSJ9.Y7zmAo4JDT1d1ME1aP57INAKhbYWAfqsLr-V4PJtyy4";
        string request = AcerAgentAuthentication.CreateChallengeRequest("Synthetic PHN16-TEST", new(2030, 1, 15, 14, 3, 2, TimeSpan.FromHours(3)), challenge!);
        using (var json = JsonDocument.Parse(request))
            check(json.RootElement.GetProperty("Function").GetString() == "Challenge" && json.RootElement.GetProperty("Nonce").GetString() == challenge!.Nonce &&
                json.RootElement.GetProperty("Token").GetString() == token, "OEM authentication matches independent synthetic JWT golden vector");

        const string localToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpYXQiOjE4OTM0MDU2NjAsImV4cCI6MTg5MzQwNTcwNSwibm9uY2UiOiJmaXh0dXJlLWxvY2FsLWRhdGUifQ.wn302Gru6z5uhYDQ3PhU1LN5HwBZSL5SmmXYJOLdT_c";
        request = AcerAgentAuthentication.CreateChallengeRequest(" Acer Synthetic X ", new(2030, 1, 1, 0, 1, 0, TimeSpan.FromHours(14)), new("fixture-local-date", 45));
        using (var json = JsonDocument.Parse(request))
            check(json.RootElement.GetProperty("Token").GetString() == localToken, "OEM derivation preserves local date/hour, model whitespace and UTC claim time");

        foreach (string invalid in new[]
        {
            "{}", "null", "[]", challengeReply.Replace("Request_Challenge", "Challenge"),
            challengeReply.Replace("\"result\":\"0\"", "\"result\":0"), challengeReply.Replace("\"result\":\"0\"", "\"result\":\"1\""),
            challengeReply.Replace("120", "0"), challengeReply.Replace("120", "-1"), challengeReply.Replace("120", "1.5"),
            challengeReply.Replace("120", "\"120\""), challengeReply.Replace("120", "2147483648"),
            challengeReply.Replace("fixture-nonce-001", ""), challengeReply.Replace("fixture-nonce-001", "bad\\nnonce"),
            challengeReply.Replace("\"Nonce\":", "\"Nonce\":\"duplicate\",\"Nonce\":"),
            challengeReply.Replace("\"Nonce\":\"fixture-nonce-001\"", "\"data\":{\"Nonce\":\"fixture-nonce-001\"}"),
            new string(' ', 16385)
        }) check(!AcerAgentAuthentication.TryParseChallenge(invalid, out _), "Malformed, ambiguous or unsuccessful OEM challenge stays unauthenticated");

        check(AcerAgentAuthentication.IsAuthenticated("{\"request\":\"Challenge\",\"result\":\"0\"}"), "Challenge authentication requires explicit matching success");
        check(!AcerAgentAuthentication.IsAuthenticated("{\"request\":\"VERSION\",\"result\":\"0\"}") &&
            !AcerAgentAuthentication.IsAuthenticated("{\"request\":\"Challenge\",\"result\":0}") &&
            !AcerAgentAuthentication.IsAuthenticated("{\"request\":\"Challenge\",\"result\":\"1\",\"result\":\"0\"}"),
            "Other request echoes, ambiguous results and numeric success cannot authenticate");
    }
}
