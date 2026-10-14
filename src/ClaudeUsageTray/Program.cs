namespace ClaudeUsageTray;

internal static class Program
{
    private const long MaxLogBytes = 256 * 1024;

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\ClaudeUsageTray", out var isFirstInstance);
        if (!isFirstInstance)
            return;

        // UI-thread exceptions: log and keep running, as nobody is there to click the default crash dialog.
        // Exceptions on other threads still end the process (the runtime gives no choice); they are logged first.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log(e.ExceptionObject);

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp(Settings.Load()));
    }

    private static void Log(object error)
    {
        try
        {
            Directory.CreateDirectory(Settings.Folder);
            var path = Path.Combine(Settings.Folder, "error.log");
            if (File.Exists(path) && new FileInfo(path).Length > MaxLogBytes)
                File.Delete(path);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {error}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex.IsIoError())
        {
        }
    }
}
