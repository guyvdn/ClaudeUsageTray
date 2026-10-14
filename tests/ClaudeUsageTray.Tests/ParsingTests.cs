using System.Net.Http.Headers;

namespace ClaudeUsageTray.Tests;

public sealed class ParsingTests
{
    [Fact]
    public void ParsesBothWindows()
    {
        var limits = UsageParser.Parse("""
            {
              "five_hour": { "utilization": 23.5, "resets_at": "2026-10-14T15:00:00+00:00" },
              "seven_day": { "utilization": 41, "resets_at": null }
            }
            """);

        Assert.NotNull(limits);
        Assert.Equal(new LimitWindow(23.5, new DateTimeOffset(2026, 10, 14, 15, 0, 0, TimeSpan.Zero)), limits.FiveHour);
        Assert.Equal(new LimitWindow(41, ResetsAt: null), limits.SevenDay);
    }

    [Theory]
    [InlineData("""{ "five_hour": { "utilization": "23" } }""")]
    [InlineData("""{ "five_hour": { "utilization": null } }""")]
    [InlineData("""{ "five_hour": "busy" }""")]
    [InlineData("{ }")]
    public void AMissingOrMalformedWindowIsNull(string json)
    {
        var limits = UsageParser.Parse(json);

        Assert.NotNull(limits);
        Assert.Null(limits.FiveHour);
    }

    [Fact]
    public void AnInvalidResetTimeIsDropped() =>
        Assert.Null(UsageParser.Parse("""{ "five_hour": { "utilization": 1, "resets_at": "soon" } }""")!.FiveHour!.ResetsAt);

    [Theory]
    [InlineData("[]")]
    [InlineData("")]
    [InlineData("<html>")]
    public void ABodyThatIsNotAnObjectIsRejected(string json) =>
        Assert.Null(UsageParser.Parse(json));

    [Theory]
    [InlineData(22.5, 23)]
    [InlineData(22.4, 22)]
    [InlineData(-3, 0)]
    [InlineData(140, 100)]
    public void PercentRoundsHalfAwayFromZeroAndClamps(double used, int expected) =>
        Assert.Equal(expected, new LimitWindow(used, ResetsAt: null).RoundedPercent);

    [Fact]
    public void ParsesCredentials()
    {
        var credentials = Credentials.Parse("""{ "claudeAiOauth": { "accessToken": "tok", "expiresAt": 1792000000000 } }""");

        Assert.Equal(new Credentials("tok", DateTimeOffset.FromUnixTimeMilliseconds(1792000000000)), credentials);
    }

    [Fact]
    public void ANonNumericExpiryIsIgnored() =>
        Assert.Null(Credentials.Parse("""{ "claudeAiOauth": { "accessToken": "tok", "expiresAt": "later" } }""")!.ExpiresAt);

    [Theory]
    [InlineData("{ }")]
    [InlineData("""{ "claudeAiOauth": "tok" }""")]
    [InlineData("""{ "claudeAiOauth": { "accessToken": "" } }""")]
    [InlineData("""{ "claudeAiOauth": { "accessToken": null } }""")]
    [InlineData("not json")]
    public void CredentialsWithoutAUsableTokenAreRejected(string json) =>
        Assert.Null(Credentials.Parse(json));

    [Fact]
    public void CredentialsNeverPrintTheToken() =>
        Assert.DoesNotContain("secret", new Credentials("secret", ExpiresAt: null).ToString(), StringComparison.Ordinal);

    [Fact]
    public void RetryAfterAsDelta() =>
        Assert.Equal(TimeSpan.FromSeconds(471), LimitsClient.RetryAfter(new RetryConditionHeaderValue(TimeSpan.FromSeconds(471)), DateTimeOffset.UtcNow));

    [Fact]
    public void RetryAfterAsDateInThePastIsNegative()
    {
        var now = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

        Assert.True(LimitsClient.RetryAfter(new RetryConditionHeaderValue(now.AddMinutes(-1)), now) < TimeSpan.Zero);
    }

    [Fact]
    public void NoRetryAfterHeader() =>
        Assert.Null(LimitsClient.RetryAfter(header: null, DateTimeOffset.UtcNow));
}
