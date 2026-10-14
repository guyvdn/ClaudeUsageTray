namespace ClaudeUsageTray.Tests;

public sealed class PollPolicyTests
{
    private static readonly TimeSpan Base = TimeSpan.FromMinutes(5);
    private static readonly DateTimeOffset Now = TestClock.Now;

    // 0.5 is the midpoint of [0, 1): symmetric jitter comes out at exactly zero.
    private static PollPolicy Policy(double random = 0.5) => new(Base, () => random);

    private static FetchResult Result(FetchStatus status, TimeSpan? retryAfter = null) => new(status, RetryAfter: retryAfter);

    [Fact]
    public void OkWaitsTheBaseIntervalAndClearsStrikes()
    {
        var (delay, strikes) = Policy().Decide(Result(FetchStatus.Ok), new Strikes(RateLimit: 3, Failure: 2));

        Assert.Equal(Base, delay);
        Assert.Equal(default, strikes);
    }

    [Theory]
    [InlineData(0.0, 270)]
    [InlineData(0.999999, 330)]
    public void OkJitterStaysWithinTenPercent(double random, int expectedSeconds)
    {
        var (delay, _) = Policy(random).Decide(Result(FetchStatus.Ok), default);

        Assert.Equal(expectedSeconds, delay.TotalSeconds, precision: 0);
    }

    [Fact]
    public void RateLimitWithoutRetryAfterClimbsTheLadderAndStaysAtTheTop()
    {
        var policy = Policy();
        var strikes = default(Strikes);
        var delays = new List<double>();
        for (var i = 0; i < 5; i++)
        {
            (var delay, strikes) = policy.Decide(Result(FetchStatus.RateLimited), strikes);
            delays.Add(delay.TotalMinutes);
        }

        Assert.Equal([10, 20, 40, 60, 60], delays);
    }

    [Fact]
    public void RetryAfterShorterThanTheBaseIntervalStillWaitsTheBaseInterval()
    {
        var (delay, _) = Policy(random: 0).Decide(Result(FetchStatus.RateLimited, TimeSpan.FromSeconds(30)), default);

        Assert.Equal(Base, delay);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(0.999999)]
    public void RetryAfterIsNeverUndercutByJitter(double random)
    {
        var retryAfter = TimeSpan.FromHours(2);

        var (delay, _) = Policy(random).Decide(Result(FetchStatus.RateLimited, retryAfter), default);

        Assert.InRange(delay, retryAfter, retryAfter * 1.1);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.999999)]
    public void AnAbsurdRetryAfterIsCappedJitterIncluded(double random)
    {
        var (delay, _) = Policy(random).Decide(Result(FetchStatus.RateLimited, TimeSpan.FromDays(3)), default);

        Assert.Equal(PollPolicy.MaxRetryAfter, delay);
    }

    [Fact]
    public void JitterCannotPushARetryAfterJustUnderTheCapPastIt()
    {
        var (delay, _) = Policy(random: 0.999999).Decide(Result(FetchStatus.RateLimited, TimeSpan.FromHours(5.9)), default);

        Assert.Equal(PollPolicy.MaxRetryAfter, delay);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-300)]
    public void ANonPositiveRetryAfterFallsBackToTheLadder(int retryAfterSeconds)
    {
        var (delay, _) = Policy().Decide(Result(FetchStatus.RateLimited, TimeSpan.FromSeconds(retryAfterSeconds)), default);

        Assert.Equal(TimeSpan.FromMinutes(10), delay);
    }

    [Fact]
    public void RetryAfterStillEscalatesTheLadderForTheNext429()
    {
        var (_, strikes) = Policy().Decide(Result(FetchStatus.RateLimited, TimeSpan.FromMinutes(15)), default);

        Assert.Equal(1, strikes.RateLimit);
    }

    [Fact]
    public void FailuresClimbTheirOwnLadder()
    {
        var policy = Policy();
        var strikes = new Strikes(RateLimit: 2, Failure: 0);
        var delays = new List<double>();
        for (var i = 0; i < 5; i++)
        {
            (var delay, strikes) = policy.Decide(Result(FetchStatus.Failed), strikes);
            delays.Add(delay.TotalMinutes);
        }

        Assert.Equal([1, 2, 5, 15, 15], delays);
        Assert.Equal(2, strikes.RateLimit);
    }

    [Theory]
    [InlineData(nameof(FetchStatus.NoToken))]
    [InlineData(nameof(FetchStatus.TokenExpired))]
    [InlineData(nameof(FetchStatus.Unauthorized))]
    public void CredentialProblemsRecheckEveryFiveMinutesWithoutEscalating(string status)
    {
        var strikes = new Strikes(RateLimit: 1, Failure: 1);

        var (delay, next) = Policy().Decide(Result(Enum.Parse<FetchStatus>(status)), strikes);

        Assert.Equal(PollPolicy.CredentialsRecheck, delay);
        Assert.Equal(strikes, next);
    }

    private static PollState Fetched(DateTimeOffset at, DateTimeOffset fiveHourResetsAt) => PollState.Initial with
    {
        LastSuccess = at,
        Limits = new LimitsSnapshot(new LimitWindow(10, fiveHourResetsAt), new LimitWindow(20, Now.AddDays(3))),
    };

    [Fact]
    public void NeverFetchedIsStale() =>
        Assert.True(Policy().IsStale(PollState.Initial, IconMetric.FiveHour, Now));

    [Fact]
    public void RecentDataIsFresh() =>
        Assert.False(Policy().IsStale(Fetched(Now.AddMinutes(-5), Now.AddHours(2)), IconMetric.FiveHour, Now));

    [Fact]
    public void DataOlderThanTwoAndAHalfIntervalsIsStale() =>
        Assert.True(Policy().IsStale(Fetched(Now.AddMinutes(-13), Now.AddHours(2)), IconMetric.FiveHour, Now));

    [Fact]
    public void DataFromBeforeTheShownWindowResetIsStale() =>
        Assert.True(Policy().IsStale(Fetched(Now.AddMinutes(-1), Now.AddSeconds(-1)), IconMetric.FiveHour, Now));

    [Fact]
    public void AResetOfTheOtherWindowDoesNotMakeTheShownOneStale() =>
        Assert.False(Policy().IsStale(Fetched(Now.AddMinutes(-1), Now.AddSeconds(-1)), IconMetric.SevenDay, Now));

    [Fact]
    public void ResumingAfterTheDueTimeWaitsForTheNetwork() =>
        Assert.Equal(PollPolicy.AfterWake, PollPolicy.ResumeDelay(Now.AddMinutes(-30), Now));

    [Fact]
    public void ResumingDuringABackoffKeepsTheBackoff() =>
        Assert.Equal(TimeSpan.FromMinutes(30), PollPolicy.ResumeDelay(Now.AddMinutes(30), Now));

    [Fact]
    public void ResumingWithNothingScheduledWaitsForTheNetwork() =>
        Assert.Equal(PollPolicy.AfterWake, PollPolicy.ResumeDelay(resumeAt: null, Now));

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(-120, nameof(FetchStatus.Ok), true)]
    [InlineData(-30, nameof(FetchStatus.Ok), false)]
    [InlineData(-3600, nameof(FetchStatus.RateLimited), false)]
    [InlineData(-3600, nameof(FetchStatus.Unauthorized), true)]
    public void ManualRefreshIsRefusedWhileRateLimitedOrTooSoon(int? secondsSinceLastAttempt, string? lastStatus, bool expected)
    {
        var state = PollState.Initial with
        {
            LastAttempt = secondsSinceLastAttempt is { } s ? Now.AddSeconds(s) : null,
            LastResult = lastStatus is null ? null : new FetchResult(Enum.Parse<FetchStatus>(lastStatus)),
        };

        Assert.Equal(expected, PollPolicy.CanRefreshManually(state, Now));
    }
}
