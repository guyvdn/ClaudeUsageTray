using System.Text.Json;

namespace ClaudeUsageTray;

/// <summary>The OAuth token Claude Code keeps in <c>.credentials.json</c>.</summary>
internal sealed record Credentials(string Token, DateTimeOffset? ExpiresAt)
{
    public const string FileName = ".credentials.json";

    /// <summary>Reads <c>claudeAiOauth.accessToken</c> and <c>expiresAt</c> (epoch ms).</summary>
    /// <returns>The credentials, or null when the file holds no usable token.</returns>
    public static Credentials? Parse(string json) => JsonParsing.ParseObject(json, Read);

    // Keep the token out of logs and debugger displays.
    public override string ToString() => $"Credentials {{ ExpiresAt = {ExpiresAt} }}";

    private static Credentials? Read(JsonElement root)
    {
        if (!root.TryGetProperty("claudeAiOauth", out var oauth)
            || oauth.ValueKind != JsonValueKind.Object
            || !oauth.TryGetProperty("accessToken", out var token)
            || token.ValueKind != JsonValueKind.String
            || token.GetString() is not { Length: > 0 } value)
            return null;

        DateTimeOffset? expiresAt = oauth.TryGetProperty("expiresAt", out var exp) && exp.ValueKind == JsonValueKind.Number
                                    && exp.TryGetInt64(out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : null;

        return new Credentials(value, expiresAt);
    }
}
