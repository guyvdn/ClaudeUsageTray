namespace ClaudeUsageTray;

/// <summary>Everything known about polling so far; immutable, replaced after every fetch.</summary>
internal sealed record PollState
{
    public static PollState Initial { get; } = new();

    /// <summary>The last limits successfully fetched; kept through errors so the icon has something to show.</summary>
    public LimitsSnapshot? Limits { get; init; }

    public FetchResult? LastResult { get; init; }

    public DateTimeOffset? LastSuccess { get; init; }

    /// <summary>The last call that actually reached the server; what manual refresh is rate-limited against.</summary>
    public DateTimeOffset? LastAttempt { get; init; }

    /// <summary>When the poll timer fires next; null while paused.</summary>
    public DateTimeOffset? NextPoll { get; init; }

    public Strikes Strikes { get; init; }

    /// <summary>The credentials file version the server rejected; no call is made until the file changes.</summary>
    public CredentialsVersion? RejectedCredentials { get; init; }

    /// <param name="result">The outcome of the fetch.</param>
    /// <param name="credentials">The credentials file version read before the fetch, so a token refreshed mid-flight is not blamed for a 401.</param>
    /// <param name="now">When the fetch completed.</param>
    public PollState After(FetchResult result, CredentialsVersion? credentials, DateTimeOffset now) =>
        this with
        {
            LastResult = result,
            LastAttempt = result.Status.ReachedServer() ? now : LastAttempt,
            Limits = result.Status == FetchStatus.Ok ? result.Limits : Limits,
            LastSuccess = result.Status == FetchStatus.Ok ? now : LastSuccess,
            RejectedCredentials = result.Status switch
            {
                FetchStatus.Ok => null,
                FetchStatus.Unauthorized => credentials,
                _ => RejectedCredentials,
            },
        };
}
