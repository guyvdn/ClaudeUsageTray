namespace ClaudeUsageTray;

/// <summary>
/// When to call the usage endpoint next. It shares its rate limit with Claude Code itself and reportedly
/// 429s clients polling every 30–60 s for hours, so this errs on the side of calling less. Pure: no
/// timers, no IO, randomness injected (<paramref name="random"/> returns [0, 1)), so it is unit tested.
/// </summary>
internal sealed class PollPolicy(TimeSpan baseInterval, Func<double> random)
{
    public static readonly TimeSpan CredentialsRecheck = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan AfterWake = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan ManualRefreshMinGap = TimeSpan.FromSeconds(60);

    /// <summary>Upper bound on any wait a server Retry-After causes, jitter included, so a bogus value cannot silence the app for days.</summary>
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromHours(6);

    private const double IntervalJitter = 0.1;
    private const double BackoffJitter = 0.2;

    /// <summary>Data older than this many intervals has missed more than a poll or two.</summary>
    private const double StaleAfterIntervals = 2.5;

    private static readonly TimeSpan[] RateLimitBackoff = [TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(40), TimeSpan.FromMinutes(60)];
    private static readonly TimeSpan[] FailureBackoff = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15)];

    public PollPolicy(TimeSpan baseInterval)
        : this(baseInterval, Random.Shared.NextDouble)
    {
    }

    public (TimeSpan Delay, Strikes Strikes) Decide(FetchResult result, Strikes strikes)
    {
        if (result.Status.IsCredentialProblem())
            return (CredentialsRecheck, strikes);

        switch (result.Status)
        {
            case FetchStatus.Ok:
                return (WithJitter(baseInterval, IntervalJitter), default);

            case FetchStatus.RateLimited:
                // Escalate even when Retry-After decides this delay, so a later 429 without one backs off further.
                var escalated = strikes with { RateLimit = strikes.RateLimit + 1 };
                if (result.RetryAfter is { } retryAfter && retryAfter > TimeSpan.Zero)
                {
                    // Jitter upwards only: calling before the server's deadline is what extends a 429.
                    var floor = Max(retryAfter, baseInterval);
                    return (Min(floor + floor * (random() * IntervalJitter), MaxRetryAfter), escalated);
                }

                // No usable Retry-After (absent, zero, or a date already past): our own ladder.
                return (WithJitter(Step(RateLimitBackoff, strikes.RateLimit), BackoffJitter), escalated);

            default:
                return (WithJitter(Step(FailureBackoff, strikes.Failure), BackoffJitter), strikes with { Failure = strikes.Failure + 1 });
        }
    }

    /// <summary>The number on display may no longer be true: never fetched, too old, or its window has reset since.</summary>
    public bool IsStale(PollState state, IconMetric shown, DateTimeOffset now) =>
        state.LastSuccess is not { } last
        || state.Limits?.For(shown)?.ResetsAt <= now
        || now - last > baseInterval * StaleAfterIntervals;

    /// <summary>Keeps the schedule a lock or sleep interrupted, but gives the network time to come back.</summary>
    public static TimeSpan ResumeDelay(DateTimeOffset? resumeAt, DateTimeOffset now) =>
        resumeAt is { } at ? Max(AfterWake, at - now) : AfterWake;

    public static bool CanRefreshManually(PollState state, DateTimeOffset now) =>
        state.LastResult?.Status != FetchStatus.RateLimited
        && (state.LastAttempt is not { } last || now - last >= ManualRefreshMinGap);

    /// <summary>Spreads <paramref name="value"/> evenly over ±<paramref name="fraction"/>.</summary>
    private TimeSpan WithJitter(TimeSpan value, double fraction) => value * (1 + (random() * 2 - 1) * fraction);

    private static TimeSpan Step(TimeSpan[] ladder, int strikes) => ladder[Math.Min(strikes, ladder.Length - 1)];

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
}
