using System.Text.Json;

namespace ClaudeUsageTray;

/// <summary>Parses the usage endpoint's body: <c>{"five_hour": {"utilization": 23.0, "resets_at": "…"}, "seven_day": {…}}</c>.</summary>
internal static class UsageParser
{
    /// <returns>The snapshot, or null when the body is not a JSON object.</returns>
    public static LimitsSnapshot? Parse(string json) =>
        JsonParsing.ParseObject(json, root => new LimitsSnapshot(Window(root, "five_hour"), Window(root, "seven_day")));

    private static LimitWindow? Window(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object)
            return null;
        if (!window.TryGetProperty("utilization", out var u) || u.ValueKind != JsonValueKind.Number || !u.TryGetDouble(out var used))
            return null;

        DateTimeOffset? resetsAt = window.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.String
                                   && r.TryGetDateTimeOffset(out var parsed)
            ? parsed
            : null;

        return new LimitWindow(used, resetsAt);
    }
}
