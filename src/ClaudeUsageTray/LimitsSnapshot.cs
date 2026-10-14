namespace ClaudeUsageTray;

internal sealed record LimitsSnapshot(LimitWindow? FiveHour, LimitWindow? SevenDay)
{
    public LimitWindow? For(IconMetric metric) => metric == IconMetric.SevenDay ? SevenDay : FiveHour;
}
