using System.Text.Json;

namespace ClaudeUsageTray;

internal static class JsonParsing
{
    /// <summary>Hands a JSON object to <paramref name="read"/>; anything else, malformed input included, gives null.</summary>
    public static T? ParseObject<T>(string json, Func<JsonElement, T?> read)
        where T : class
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? read(doc.RootElement) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
