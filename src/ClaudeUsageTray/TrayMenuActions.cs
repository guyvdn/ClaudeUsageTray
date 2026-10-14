namespace ClaudeUsageTray;

/// <summary>What the tray menu can ask of the app.</summary>
/// <param name="SelectMetric">Called when the user picks what the icon shows.</param>
/// <param name="CanRefresh">Asked each time the menu opens; a refused refresh is greyed out rather than silently ignored.</param>
/// <param name="Refresh">Called when the user clicks Refresh.</param>
/// <param name="StartsWithWindows">Asked each time the menu opens, as the setting can also change in Windows Settings.</param>
/// <param name="SetStartWithWindows">Called with the new value; returns whether it was applied.</param>
/// <param name="Exit">Called when the user clicks Exit.</param>
internal sealed record TrayMenuActions(
    Action<IconMetric> SelectMetric,
    Func<bool> CanRefresh,
    Action Refresh,
    Func<bool> StartsWithWindows,
    Func<bool, bool> SetStartWithWindows,
    Action Exit);
