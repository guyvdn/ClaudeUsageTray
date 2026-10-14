namespace ClaudeUsageTray.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ClaudeUsageTray.Tests.").FullName;

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void MissingFileGivesDefaults()
    {
        var settings = Settings.Load(SettingsPath);

        Assert.Equal(IconMetric.FiveHour, settings.IconMetric);
        Assert.Equal(300, settings.PollSeconds);
        Assert.Null(settings.ConfigDir);
    }

    [Theory]
    [InlineData(10, Settings.MinPollSeconds)]
    [InlineData(99999, Settings.MaxPollSeconds)]
    [InlineData(600, 600)]
    public void PollSecondsIsClamped(int configured, int expected)
    {
        File.WriteAllText(SettingsPath, $$"""{ "PollSeconds": {{configured}} }""");

        Assert.Equal(expected, Settings.Load(SettingsPath).PollSeconds);
    }

    [Theory]
    [InlineData("\"SevenDay\"")]
    [InlineData("1")]
    public void IconMetricReadsNamesAndNumbers(string value)
    {
        File.WriteAllText(SettingsPath, $$"""{ "IconMetric": {{value}} }""");

        Assert.Equal(IconMetric.SevenDay, Settings.Load(SettingsPath).IconMetric);
    }

    [Fact]
    public void AnUnknownIconMetricFallsBack()
    {
        File.WriteAllText(SettingsPath, """{ "IconMetric": 7 }""");

        Assert.Equal(IconMetric.FiveHour, Settings.Load(SettingsPath).IconMetric);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"  \"")]
    public void ABlankConfigDirMeansTheDefault(string value)
    {
        File.WriteAllText(SettingsPath, $$"""{ "ConfigDir": {{value}} }""");

        Assert.Null(Settings.Load(SettingsPath).ConfigDir);
    }

    [Fact]
    public void ATildeConfigDirExpandsToTheUserProfile()
    {
        File.WriteAllText(SettingsPath, """{ "ConfigDir": "~/.claude-work" }""");

        var expected = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.claude-work";
        Assert.Equal(expected, Settings.Load(SettingsPath).ConfigDir);
    }

    [Theory]
    [InlineData("~", "")]
    [InlineData("~/.claude", "/.claude")]
    [InlineData(@"~\.claude", @"\.claude")]
    public void ExpandPathResolvesTheHomeDirectory(string path, string suffix) =>
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + suffix, Settings.ExpandPath(path));

    [Fact]
    public void ExpandPathExpandsEnvironmentVariables() =>
        Assert.Equal(Environment.GetEnvironmentVariable("SystemRoot"), Settings.ExpandPath("%SystemRoot%"));

    [Fact]
    public void ExpandPathTrimsAndLeavesAbsolutePathsAlone() =>
        Assert.Equal(@"D:\claude", Settings.ExpandPath(@" D:\claude "));

    [Fact]
    public void ACorruptFileIsSetAsideNotOverwritten()
    {
        File.WriteAllText(SettingsPath, "{ not json");

        var settings = Settings.Load(SettingsPath);
        settings.Save();

        Assert.Equal("{ not json", File.ReadAllText(SettingsPath + ".invalid"));
        Assert.Contains("FiveHour", File.ReadAllText(SettingsPath), StringComparison.Ordinal);
    }

    [Fact]
    public void SaveWritesEnumNamesAndRoundTrips()
    {
        var settings = Settings.Load(SettingsPath);
        settings.IconMetric = IconMetric.SevenDay;
        settings.Save();

        Assert.Contains("\"SevenDay\"", File.ReadAllText(SettingsPath), StringComparison.Ordinal);
        Assert.Equal(IconMetric.SevenDay, Settings.Load(SettingsPath).IconMetric);
    }
}
