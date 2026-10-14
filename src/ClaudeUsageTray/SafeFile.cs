namespace ClaudeUsageTray;

/// <summary>File access for a background app: a locked or missing file is an expected outcome, not a crash.</summary>
internal static class SafeFile
{
    public static bool IsIoError(this Exception ex) => ex is IOException or UnauthorizedAccessException;

    public static string? ReadAllTextOrNull(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex.IsIoError())
        {
            return null;
        }
    }

    public static DateTime? LastWriteUtcOrNull(string path)
    {
        try
        {
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
        }
        catch (Exception ex) when (ex.IsIoError())
        {
            return null;
        }
    }

    public static void TryMove(string from, string to)
    {
        try
        {
            File.Move(from, to, overwrite: true);
        }
        catch (Exception ex) when (ex.IsIoError())
        {
        }
    }
}
