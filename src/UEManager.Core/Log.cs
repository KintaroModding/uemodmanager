namespace UEManager.Core;

/// <summary>Plain text log, one file per day in the data folder's logs directory.</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string? logsDir;

    public static void Initialize(string directory)
    {
        Directory.CreateDirectory(directory);
        logsDir = directory;
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private static void Write(string level, string message)
    {
        if (logsDir is null)
            return;
        var now = DateTime.Now;
        var line = $"{now:yyyy-MM-dd HH:mm:ss} {level,-5} {message}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                File.AppendAllText(Path.Combine(logsDir, $"app-{now:yyyyMMdd}.log"), line);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never take the application down.
            }
        }
    }
}
