namespace ClaudeUsageTray.Tests;

public sealed class PollStateTests
{
    private static readonly DateTimeOffset Now = TestClock.Now;
    private static readonly CredentialsVersion Version = new(new DateTime(2026, 10, 14, 11, 0, 0, DateTimeKind.Utc));
    private static readonly LimitsSnapshot Limits = new(new LimitWindow(10, ResetsAt: null), SevenDay: null);

    [Fact]
    public void SuccessStoresTheLimitsAndClearsARejectedToken()
    {
        var before = PollState.Initial with { RejectedCredentials = Version };

        var after = before.After(new FetchResult(FetchStatus.Ok, Limits), Version, Now);

        Assert.Equal(Limits, after.Limits);
        Assert.Equal(Now, after.LastSuccess);
        Assert.Equal(Now, after.LastAttempt);
        Assert.Null(after.RejectedCredentials);
    }

    [Fact]
    public void AFailureKeepsTheLastGoodLimits()
    {
        var before = PollState.Initial.After(new FetchResult(FetchStatus.Ok, Limits), Version, Now.AddMinutes(-5));

        var after = before.After(new FetchResult(FetchStatus.RateLimited), Version, Now);

        Assert.Equal(Limits, after.Limits);
        Assert.Equal(Now.AddMinutes(-5), after.LastSuccess);
        Assert.Equal(FetchStatus.RateLimited, after.LastResult?.Status);
    }

    [Fact]
    public void A401RejectsTheCredentialsVersionReadBeforeTheCall() =>
        Assert.Equal(Version, PollState.Initial.After(new FetchResult(FetchStatus.Unauthorized), Version, Now).RejectedCredentials);

    [Theory]
    [InlineData(nameof(FetchStatus.NoToken))]
    [InlineData(nameof(FetchStatus.TokenExpired))]
    public void LocallyDecidedOutcomesDoNotCountAsAnAttempt(string status)
    {
        var before = PollState.Initial with { LastAttempt = Now.AddHours(-1) };

        var after = before.After(new FetchResult(Enum.Parse<FetchStatus>(status)), credentials: null, Now);

        Assert.Equal(Now.AddHours(-1), after.LastAttempt);
    }

    [Theory]
    [InlineData(nameof(FetchStatus.Ok), true, false)]
    [InlineData(nameof(FetchStatus.NoToken), false, true)]
    [InlineData(nameof(FetchStatus.TokenExpired), false, true)]
    [InlineData(nameof(FetchStatus.Unauthorized), true, true)]
    [InlineData(nameof(FetchStatus.RateLimited), true, false)]
    [InlineData(nameof(FetchStatus.Failed), true, false)]
    public void StatusGroups(string status, bool reachedServer, bool credentialProblem)
    {
        var parsed = Enum.Parse<FetchStatus>(status);

        Assert.Equal(reachedServer, parsed.ReachedServer());
        Assert.Equal(credentialProblem, parsed.IsCredentialProblem());
    }
}
