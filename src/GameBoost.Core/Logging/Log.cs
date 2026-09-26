namespace GameBoost.Core.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error
}

public static class Log
{
    private static readonly object Sync = new();
    private static string _directory = string.Empty;
    private static bool _initialized;

    public static event Action<string, LogLevel, string>? Logged;

    public static void Init(string directory, int retentionDays = 14)
    {
        lock (Sync)
        {
            _directory = directory;
            Directory.CreateDirectory(directory);
            _initialized = true;
            TryCleanup(retentionDays);
        }
        Info("GameBoost", "Journal initialisé dans " + directory);
    }

    public static string CurrentFile
    {
        get
        {
            var dir = _initialized ? _directory : FallbackDirectory();
            return Path.Combine(dir, $"gameboost-{DateTime.Now:yyyyMMdd}.log");
        }
    }

    public static void Debug(string source, string message) => Write(LogLevel.Debug, source, message, null);
    public static void Info(string source, string message) => Write(LogLevel.Info, source, message, null);
    public static void Warn(string source, string message) => Write(LogLevel.Warn, source, message, null);
    public static void Error(string source, string message, Exception? ex = null) => Write(LogLevel.Error, source, message, ex);

    public static void Fatal(string source, string message, Exception? ex = null) => Write(LogLevel.Error, source, message, ex);

    private static string FallbackDirectory()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameBoost", "logs");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Write(LogLevel level, string source, string message, Exception? ex)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level.ToString().ToUpperInvariant()}] [{source}] {message}";
        if (ex != null)
        {
            line += Environment.NewLine + "    " + ex.GetType().Name + ": " + ex.Message + Environment.NewLine +
                    "    " + (ex.StackTrace ?? string.Empty);
        }

        try
        {
            var dir = _initialized ? _directory : FallbackDirectory();
            Directory.CreateDirectory(dir);
            lock (Sync)
            {
                File.AppendAllText(Path.Combine(dir, $"gameboost-{DateTime.Now:yyyyMMdd}.log"), line + Environment.NewLine);
            }
        }
        catch
        {
        }

        try
        {
            Logged?.Invoke(line, level, source);
        }
        catch
        {
        }

        System.Diagnostics.Debug.WriteLine(line);
    }

    public static IReadOnlyList<string> ReadRecent(int maxLines = 500)
    {
        try
        {
            var file = CurrentFile;
            if (!File.Exists(file)) return Array.Empty<string>();
            var lines = File.ReadAllLines(file);
            return lines.Length <= maxLines ? lines : lines[^maxLines..];
        }
        catch (Exception ex)
        {
            return new[] { "Lecture du journal impossible : " + ex.Message };
        }
    }

    public static void TrimAll(int retentionDays)
    {
        try
        {
            var dir = _initialized ? _directory : FallbackDirectory();
            TryCleanup(retentionDays);
        }
        catch (Exception ex)
        {
            Error("Log", "Échec du nettoyage des journaux", ex);
        }
    }

    private static void TryCleanup(int retentionDays)
    {
        if (!_initialized || retentionDays <= 0) return;
        var cutoff = DateTime.Now.AddDays(-retentionDays);
        foreach (var file in Directory.EnumerateFiles(_directory, "gameboost-*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
            }
            catch
            {
            }
        }
    }
}
