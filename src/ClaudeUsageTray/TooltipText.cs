using System.Globalization;

namespace ClaudeUsageTray;

/// <summary>Builds the tray tooltip. Times in <see cref="PollState"/> are UTC and shown in local time.</summary>
internal static class TooltipText
{
    /// <summary>NotifyIcon.Text throws above this length.</summary>
    public const int MaxLength = 127;

    /// <summary>Exception messages can be long; keep room for the limits above them.</summary>
    private const int MaxErrorLength = 40;

    public static string Build(PollState state, bool paused)
    {
        var lines = new List<string> { state.LastSuccess is { } at ? $"Claude usage · as of {Local(at, "HH:mm")}" : "Claude usage" };

        foreach (var metric in Enum.GetValues<IconMetric>())
        {
            if (state.Limits?.For(metric) is not { } window)
                continue;

            var text = IconMetricText.For(metric);
            lines.Add($"{text.ShortLabel} {window.RoundedPercent}%{ResetSuffix(window.ResetsAt, text.ResetFormat)}");
        }

        if (paused)
            lines.Add("Paused while locked or asleep");
        else if (state.LastResult is { Status: not FetchStatus.Ok, Error: { } error })
            lines.Add(state.NextPoll is { } next ? $"Retry {Local(next, "HH:mm")}: {ShortenError(error)}" : ShortenError(error));
        else if (state.LastResult is null)
            lines.Add("Loading…");

        return FitMaxLength(string.Join('\n', lines));
    }

    /// <summary>Cuts to <see cref="MaxLength"/> without leaving half a surrogate pair at the end.</summary>
    internal static string FitMaxLength(string text)
    {
        if (text.Length <= MaxLength)
            return text;

        return text[..(char.IsHighSurrogate(text[MaxLength - 1]) ? MaxLength - 1 : MaxLength)];
    }

    private static string ShortenError(string error) =>
        error.Length <= MaxErrorLength ? error : error[..(MaxErrorLength - 1)] + "…";

    private static string ResetSuffix(DateTimeOffset? at, string format) => at is { } a ? " ↻ " + Local(a, format) : "";

    private static string Local(DateTimeOffset at, string format) => at.ToLocalTime().ToString(format, CultureInfo.CurrentCulture);
}
