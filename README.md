# ClaudeUsageTray

A Windows tray icon that shows your Claude subscription usage (5-hour and weekly limits), refreshed
every 5 minutes. It is meant for hosts without Claude Code's terminal status line, such as
[T3 Code](https://github.com/pingdotgg/t3code).

The icon shows the percentage used, scaled to fill the tile:

| Tile | Meaning |
|---|---|
| Green / amber (≥70) / red (≥90) | Current usage |
| Grey with a number | Out of date: older than 2.5 poll intervals, or the window has reset since |
| Grey `?` | No data yet |

The tooltip shows both windows:

```
Claude usage · as of 14:05
5h 23% ↻ 15:00
7d 41% ↻ Fri 09:00
```

Right-click to choose which window the icon shows, refresh, start with Windows, or exit.

## Where the numbers come from

`GET https://api.anthropic.com/api/oauth/usage` is the endpoint behind Claude Code's `/usage`
screen. The app calls it with the OAuth token from `.credentials.json` in your Claude config dir,
re-read on every call because Claude Code refreshes it.

**The endpoint is undocumented and rate limited hard, sharing its budget with Claude Code itself.**
Polling every 30–60 s is reported to get 429s that last from 30 minutes to hours, and it breaks
`/usage` in Claude Code too:

- https://github.com/anthropics/claude-code/issues/31637
- https://github.com/anthropics/claude-code/issues/77477

The app is careful because of that (`PollPolicy.cs`):

| Situation | Next call |
|---|---|
| Success | 5 min (`PollSeconds`, 120–3600), ±10% jitter |
| 429 with `Retry-After` | That or the base interval, whichever is longer, plus up to 10%; never more than 6 h in total |
| 429 without a usable `Retry-After` (absent, `0`, or a time already past) | 10 → 20 → 40 → 60 min, ±20% |
| Network error / 5xx | 1 → 2 → 5 → 15 min, ±20% |
| Token expired (`expiresAt`) | No call; re-check every 5 min |
| 401 / 403 | No call until the credentials file changes |
| Session locked / asleep | No calls; 20 s after unlock, or later if a backoff is still running |
| Manual Refresh | Greyed out while locked, rate limited, a call is in flight, or within 60 s of the last call. A click retries a rejected token once, in case you just started Claude Code |

Only one request is ever in flight.

Unhandled errors are logged to `%AppData%\ClaudeUsageTray\error.log`, not shown in a dialog.

## Run

Requires Windows 10 1607 or later and the .NET 10 SDK.

```bash
dotnet run --project src/ClaudeUsageTray
```

To install, publish a single exe somewhere outside the repo (so a running copy does not lock your
build output), start it, and tick **Start with Windows** in its menu:

```bash
dotnet publish src/ClaudeUsageTray -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o "$LOCALAPPDATA/Programs/ClaudeUsageTray"
```

**Start with Windows** writes the per-user `Run` registry key (no admin rights), so the app also
appears under Settings ▸ Apps ▸ Startup. It registers the exe it is ticked from; ticking it in a
copy elsewhere repoints the entry. To update, exit the app from its menu, publish again, and start it.

## Settings

`%AppData%\ClaudeUsageTray\settings.json`, written when you change something in the menu:

| Key | Default | |
|---|---|---|
| `IconMetric` | `FiveHour` | `FiveHour` or `SevenDay` |
| `ConfigDir` | `null` | Claude config dir holding `.credentials.json`. `null` uses `CLAUDE_CONFIG_DIR`, or `~/.claude`. `~` and `%VARS%` are expanded in both |
| `PollSeconds` | `300` | Clamped to 120–3600 |

A file that cannot be parsed is renamed to `settings.json.invalid` and defaults are used.

## Development

```bash
dotnet build      # Release treats warnings as errors
dotnet test
```

Analysis: Meziantou.Analyzer, the .NET `10.0-recommended` rule set, and the code style in
`.editorconfig` enforced on build. Package versions are central in `Directory.Packages.props`.

## License

[MIT](LICENSE)
