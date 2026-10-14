namespace ClaudeUsageTray.Tests;

public sealed class TooltipTextTests
{
    private static readonly DateTimeOffset Now = TestClock.Now;

    [Fact]
    public void ShowsBothWindows()
    {
        var limits = new LimitsSnapshot(new LimitWindow(23.4, Now.AddHours(3)), new LimitWindow(41, Now.AddDays(3)));
        var state = PollState.Initial.After(new FetchResult(FetchStatus.Ok, limits), credentials: null, Now);

        var lines = TooltipText.Build(state, paused: false).Split('\n');

        Assert.Equal(3, lines.Length);
        Assert.StartsWith("Claude usage · as of ", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("5h 23% ↻ ", lines[1], StringComparison.Ordinal);
        Assert.StartsWith("7d 41% ↻ ", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public void PutsTheRetryTimeBeforeALongError()
    {
        var state = PollState.Initial with
        {
            LastResult = new FetchResult(FetchStatus.Failed, Error: new string('x', 200)),
            NextPoll = Now,
        };

        var text = TooltipText.Build(state, paused: false);

        Assert.Contains("Retry ", text, StringComparison.Ordinal);
        Assert.EndsWith("…", text, StringComparison.Ordinal);
        Assert.True(text.Length <= TooltipText.MaxLength);
    }

    [Fact]
    public void SaysWhenPaused() =>
        Assert.Contains("Paused", TooltipText.Build(PollState.Initial, paused: true), StringComparison.Ordinal);

    [Fact]
    public void SaysLoadingBeforeTheFirstResult() =>
        Assert.EndsWith("Loading…", TooltipText.Build(PollState.Initial, paused: false), StringComparison.Ordinal);

    [Fact]
    public void FittingNeverSplitsASurrogatePair()
    {
        var text = new string('a', TooltipText.MaxLength - 1) + "😀" + "tail";

        var fitted = TooltipText.FitMaxLength(text);

        Assert.Equal(TooltipText.MaxLength - 1, fitted.Length);
        Assert.False(char.IsHighSurrogate(fitted[^1]));
    }
}
