namespace ClaudeUsageTray;

/// <summary>The one place that groups fetch outcomes; policy, state and tooltip all ask here.</summary>
internal static class FetchStatusExtensions
{
    /// <summary>False for outcomes decided locally from the credentials file, before any request.</summary>
    public static bool ReachedServer(this FetchStatus status) =>
        status is not (FetchStatus.NoToken or FetchStatus.TokenExpired);

    /// <summary>Nothing to gain from calling until Claude Code writes a new token.</summary>
    public static bool IsCredentialProblem(this FetchStatus status) =>
        status is FetchStatus.NoToken or FetchStatus.TokenExpired or FetchStatus.Unauthorized;
}
