using Microsoft.Win32;

namespace ClaudeUsageTray.Tests;

/// <summary>Runs against a throwaway key under HKCU, never the real Run key.</summary>
public sealed class StartupRegistrationTests : IDisposable
{
    private const string Command = "\"C:\\Apps\\ClaudeUsageTray.exe\"";

    private readonly string _keyPath = $@"Software\ClaudeUsageTray.Tests\{Guid.NewGuid():N}";

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(@"Software\ClaudeUsageTray.Tests", throwOnMissingSubKey: false);

    [Fact]
    public void OffWhenNothingIsRegistered() =>
        Assert.False(Registration(Command).IsEnabled());

    [Fact]
    public void TurningItOnAndOffRoundTrips()
    {
        var registration = Registration(Command);

        Assert.True(registration.TrySet(enabled: true));
        Assert.True(registration.IsEnabled());
        Assert.Equal(Command, ReadValue());

        Assert.True(registration.TrySet(enabled: false));
        Assert.False(registration.IsEnabled());
        Assert.Null(ReadValue());
    }

    [Fact]
    public void AnEntryForACopyElsewhereShowsAsOffAndIsRepointed()
    {
        Registration("\"D:\\old\\ClaudeUsageTray.exe\"").TrySet(enabled: true);
        var registration = Registration(Command);

        Assert.False(registration.IsEnabled());
        registration.TrySet(enabled: true);
        Assert.Equal(Command, ReadValue());
    }

    [Fact]
    public void TurningOffWhenAlreadyOffSucceeds() =>
        Assert.True(Registration(Command).TrySet(enabled: false));

    private StartupRegistration Registration(string command) => new(_keyPath, "ClaudeUsageTray", command);

    private object? ReadValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_keyPath);
        return key?.GetValue("ClaudeUsageTray");
    }
}
