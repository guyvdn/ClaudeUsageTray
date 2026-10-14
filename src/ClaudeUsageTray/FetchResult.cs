namespace ClaudeUsageTray;

internal sealed record FetchResult(FetchStatus Status, LimitsSnapshot? Limits = null, TimeSpan? RetryAfter = null, string? Error = null);
