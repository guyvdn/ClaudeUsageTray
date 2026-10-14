using Microsoft.Win32;

namespace ClaudeUsageTray;

/// <summary>
/// Runs the poll loop: one timer, one request at a time, paused while locked or asleep.
/// <see cref="PollPolicy"/> decides when; <see cref="TrayIconView"/> shows the result.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    /// <summary>Re-evaluates staleness between polls, e.g. a window resetting during a long backoff.</summary>
    private static readonly TimeSpan RepaintInterval = TimeSpan.FromMinutes(1);

    private readonly Settings _settings;
    private readonly PollPolicy _policy;
    private readonly TimeProvider _time = TimeProvider.System;
    private readonly TrayIconView _view;
    private readonly LimitsClient _limitsClient;
    private readonly System.Windows.Forms.Timer _pollTimer = new();
    private readonly System.Windows.Forms.Timer _repaintTimer = new() { Interval = (int)RepaintInterval.TotalMilliseconds };

    private PollState _state = PollState.Initial;
    private DateTimeOffset? _resumeAt;
    private bool _inFlight;
    private bool _locked;
    private bool _suspended;

    public TrayApp(Settings settings)
    {
        _settings = settings;
        _policy = new PollPolicy(TimeSpan.FromSeconds(settings.PollSeconds));
        _limitsClient = new LimitsClient(settings.ResolveConfigDir());
        _view = new TrayIconView(settings.IconMetric, SelectMetric, CanRefresh, RefreshNow, ExitThread);

        _pollTimer.Tick += async (_, _) => await PollAsync().ConfigureAwait(true);
        _repaintTimer.Tick += (_, _) => Render();
        _repaintTimer.Start();

        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        Render();
        // First poll goes through the timer so it runs inside the message loop, on the UI thread.
        Schedule(TimeSpan.FromSeconds(1));
    }

    private bool Paused => _locked || _suspended;

    private void Schedule(TimeSpan delay)
    {
        _pollTimer.Stop();
        var due = _time.GetUtcNow() + delay;
        if (Paused)
        {
            // Remember it, so a backoff decided while paused survives the pause.
            _resumeAt = due;
            _state = _state with { NextPoll = null };
            return;
        }

        _pollTimer.Interval = (int)Math.Clamp(delay.TotalMilliseconds, 1, int.MaxValue);
        _state = _state with { NextPoll = due };
        _pollTimer.Start();
    }

    private async Task PollAsync()
    {
        _pollTimer.Stop();
        if (_inFlight || Paused)
            return;

        try
        {
            // Read before the call: if Claude Code rewrites the file while we wait, the new token must not inherit a 401.
            var credentials = _limitsClient.CredentialsVersion();
            if (_state.RejectedCredentials is { } rejected && rejected == credentials)
            {
                Schedule(PollPolicy.CredentialsRecheck);
                return;
            }

            _inFlight = true;
            FetchResult result;
            try
            {
                // Continue on the UI thread: everything after this touches the tray icon.
                result = await _limitsClient.FetchAsync(CancellationToken.None).ConfigureAwait(true);
            }
            finally
            {
                _inFlight = false;
            }

            Apply(result, credentials);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Whatever happened, the loop must stay alive.
            Apply(new FetchResult(FetchStatus.Failed, Error: ex.Message), credentials: null);
        }
        finally
        {
            Render();
        }
    }

    private void Apply(FetchResult result, CredentialsVersion? credentials)
    {
        _state = _state.After(result, credentials, _time.GetUtcNow());
        var (delay, strikes) = _policy.Decide(result, _state.Strikes);
        _state = _state with { Strikes = strikes };
        Schedule(delay);
    }

    private bool CanRefresh() => !_inFlight && !Paused && PollPolicy.CanRefreshManually(_state, _time.GetUtcNow());

    private void RefreshNow()
    {
        if (!CanRefresh())
            return;

        // An explicit click may retry a rejected token: the user may just have started Claude Code.
        _state = _state with { RejectedCredentials = null };
        Schedule(TimeSpan.FromMilliseconds(1));
    }

    private void SelectMetric(IconMetric metric)
    {
        _settings.IconMetric = metric;
        _settings.Save();
        Render();
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
            SetPauseReasons(locked: true, _suspended);
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
            SetPauseReasons(locked: false, _suspended);
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        // Resume from sleep usually lands on the lock screen; the lock flag keeps polling off until unlock.
        if (e.Mode == PowerModes.Suspend)
            SetPauseReasons(_locked, suspended: true);
        else if (e.Mode == PowerModes.Resume)
            SetPauseReasons(_locked, suspended: false);
    }

    private void SetPauseReasons(bool locked, bool suspended)
    {
        var wasPaused = Paused;
        _locked = locked;
        _suspended = suspended;

        if (!wasPaused && Paused)
        {
            _pollTimer.Stop();
            _resumeAt = _state.NextPoll;
            _state = _state with { NextPoll = null };
        }
        else if (wasPaused && !Paused)
            Schedule(PollPolicy.ResumeDelay(_resumeAt, _time.GetUtcNow()));

        Render();
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _view.Invalidate(); // DPI may have changed; redraw at the new size.
        Render();
    }

    private void Render()
    {
        var metric = _settings.IconMetric;
        _view.Show(
            _state.Limits?.For(metric)?.RoundedPercent,
            _policy.IsStale(_state, metric, _time.GetUtcNow()),
            TooltipText.Build(_state, Paused));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // SystemEvents are static and would keep this instance alive.
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _pollTimer.Dispose();
            _repaintTimer.Dispose();
            _limitsClient.Dispose();
            _view.Dispose();
        }

        base.Dispose(disposing);
    }
}
