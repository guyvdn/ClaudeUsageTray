namespace ClaudeUsageTray;

/// <summary>The NotifyIcon and its menu. Knows nothing about polling; it shows what it is given and reports clicks.</summary>
internal sealed class TrayIconView : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _refreshItem;

    private Icon? _currentIcon;
    private (int? Percent, bool Stale, string Tooltip)? _rendered;

    /// <param name="selected">The metric checked in the menu at start.</param>
    /// <param name="onMetricSelected">Called when the user picks what the icon shows.</param>
    /// <param name="canRefresh">Asked each time the menu opens; a refused refresh is greyed out rather than silently ignored.</param>
    /// <param name="onRefresh">Called when the user clicks Refresh.</param>
    /// <param name="onExit">Called when the user clicks Exit.</param>
    public TrayIconView(IconMetric selected, Action<IconMetric> onMetricSelected, Func<bool> canRefresh, Action onRefresh, Action onExit)
    {
        _refreshItem = new ToolStripMenuItem("Refresh", image: null, (_, _) => onRefresh());

        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) => _refreshItem.Enabled = canRefresh();
        menu.Items.Add(BuildMetricMenu(selected, onMetricSelected));
        menu.Items.Add(_refreshItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", image: null, (_, _) => onExit());

        _notifyIcon = new NotifyIcon { Visible = true, ContextMenuStrip = menu };
    }

    /// <summary>Updates the icon and tooltip; does nothing when neither changed, as this runs every minute for months.</summary>
    public void Show(int? percent, bool stale, string tooltip)
    {
        if (_rendered == (percent, stale, tooltip))
            return;

        var previous = _currentIcon;
        _currentIcon = IconRenderer.Render(percent, stale);
        _notifyIcon.Icon = _currentIcon;
        previous?.Dispose();
        _notifyIcon.Text = tooltip;
        _rendered = (percent, stale, tooltip);
    }

    /// <summary>Forces the next <see cref="Show"/> to redraw, e.g. at a new DPI.</summary>
    public void Invalidate() => _rendered = null;

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _currentIcon?.Dispose();
    }

    private static ToolStripMenuItem BuildMetricMenu(IconMetric selected, Action<IconMetric> onMetricSelected)
    {
        var parent = new ToolStripMenuItem("Icon shows");
        foreach (var metric in Enum.GetValues<IconMetric>())
        {
            var item = new ToolStripMenuItem(IconMetricText.For(metric).MenuLabel) { Checked = metric == selected, Tag = metric };
            item.Click += (_, _) =>
            {
                foreach (ToolStripMenuItem sibling in parent.DropDownItems)
                    sibling.Checked = Equals(sibling.Tag, metric);
                onMetricSelected(metric);
            };
            parent.DropDownItems.Add(item);
        }

        return parent;
    }
}
