namespace ClaudeUsageTray;

internal enum FetchStatus
{
    Ok,
    NoToken,
    TokenExpired,
    Unauthorized,
    RateLimited,
    Failed,
}
