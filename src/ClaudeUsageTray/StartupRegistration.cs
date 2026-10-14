using System.Security;
using Microsoft.Win32;

namespace ClaudeUsageTray;

/// <summary>
/// "Start with Windows" through the per-user Run key, which also lists the app under
/// Settings ▸ Apps ▸ Startup. No admin rights needed.
/// </summary>
internal sealed class StartupRegistration(string keyPath, string valueName, string command)
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static StartupRegistration ForThisExe() =>
        new(RunKeyPath, "ClaudeUsageTray", $"\"{Environment.ProcessPath}\"");

    /// <summary>True only when registered for this exe; an entry left by a copy elsewhere shows as off, and turning it on repoints it.</summary>
    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath);
            return key?.GetValue(valueName) is string registered
                   && string.Equals(registered, command, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is SecurityException || ex.IsIoError())
        {
            return false;
        }
    }

    /// <returns>Whether the change was written.</returns>
    public bool TrySet(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(keyPath);
            if (enabled)
                key.SetValue(valueName, command);
            else
                key.DeleteValue(valueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex) when (ex is SecurityException || ex.IsIoError())
        {
            return false;
        }
    }
}
