namespace ClaudeUsageTray;

/// <summary>One usage window: percent used (0–100) and when it resets.</summary>
internal sealed record LimitWindow(double UsedPercent, DateTimeOffset? ResetsAt)
{
    /// <summary>What the icon and tooltip show; both must round the same way.</summary>
    public int RoundedPercent => Math.Clamp((int)Math.Round(UsedPercent, MidpointRounding.AwayFromZero), 0, 100);
}
