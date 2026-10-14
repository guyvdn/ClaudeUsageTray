namespace ClaudeUsageTray;

/// <summary>How each <see cref="IconMetric"/> is labelled in the menu and the tooltip.</summary>
internal sealed record IconMetricText(string MenuLabel, string ShortLabel, string ResetFormat)
{
    private static readonly IconMetricText FiveHour = new("5-hour limit", "5h", "HH:mm");
    private static readonly IconMetricText SevenDay = new("Weekly limit", "7d", "ddd HH:mm");

    public static IconMetricText For(IconMetric metric) => metric == IconMetric.SevenDay ? SevenDay : FiveHour;
}
