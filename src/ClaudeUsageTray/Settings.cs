using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeUsageTray;

/// <summary>User settings, persisted to %AppData%\ClaudeUsageTray\settings.json.</summary>
internal sealed class Settings
{
    public const int MinPollSeconds = 120;
    public const int MaxPollSeconds = 3600;

    public static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeUsageTray");

    private static readonly string DefaultPath = Path.Combine(Folder, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private string _path = DefaultPath;

    // Set when the file exists but could not be read, so a menu click does not overwrite it with defaults.
    private bool _readOnly;

    /// <summary>What the number in the tray icon shows.</summary>
    public IconMetric IconMetric { get; set; } = IconMetric.FiveHour;

    /// <summary>Claude config dir holding <c>.credentials.json</c>. Null means CLAUDE_CONFIG_DIR, or ~/.claude.</summary>
    public string? ConfigDir { get; set; }

    /// <summary>Base poll interval. The endpoint rate-limits hard, so it is clamped to <see cref="MinPollSeconds"/>–<see cref="MaxPollSeconds"/>.</summary>
    public int PollSeconds { get; set; } = 300;

    public string ResolveConfigDir()
    {
        if (ConfigDir is not null)
            return ConfigDir;

        var fromEnv = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        return string.IsNullOrWhiteSpace(fromEnv) ? Path.Combine(UserProfile, ".claude") : ExpandPath(fromEnv);
    }

    /// <summary>Expands <c>%VARS%</c> and a leading <c>~</c>, which people will copy from the README.</summary>
    internal static string ExpandPath(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        var homeRelative = string.Equals(expanded, "~", StringComparison.Ordinal)
                           || expanded.StartsWith("~/", StringComparison.Ordinal)
                           || expanded.StartsWith(@"~\", StringComparison.Ordinal);
        return homeRelative ? UserProfile + expanded[1..] : expanded;
    }

    public static Settings Load() => Load(DefaultPath);

    internal static Settings Load(string path)
    {
        var settings = new Settings();
        try
        {
            if (File.Exists(path))
                settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), JsonOptions) ?? settings;
        }
        catch (JsonException)
        {
            // Keep the broken file for inspection; the next save starts afresh.
            SafeFile.TryMove(path, path + ".invalid");
        }
        catch (Exception ex) when (ex.IsIoError())
        {
            // The file exists but is unreadable: never overwrite it with defaults.
            settings._readOnly = true;
        }

        settings._path = path;
        settings.Normalize();
        return settings;
    }

    internal void Normalize()
    {
        PollSeconds = Math.Clamp(PollSeconds, MinPollSeconds, MaxPollSeconds);
        if (!Enum.IsDefined(IconMetric))
            IconMetric = IconMetric.FiveHour;

        ConfigDir = string.IsNullOrWhiteSpace(ConfigDir) ? null : ExpandPath(ConfigDir);
    }

    public void Save()
    {
        if (_readOnly)
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex.IsIoError())
        {
            // Not worth interrupting the user over; the choice still applies to this run.
        }
    }

    private static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
