namespace LanMsg.Core.Logging;

public static class AppLogger
{
    private static readonly object Lock = new();

    public static void Info(string source, string msg, bool debugOnly = false, bool debugEnabled = false)
    {
        if (debugOnly && !debugEnabled) return;
        Write("INFO", source, msg);
    }

    public static void Error(string source, string msg, Exception? ex = null, bool debugEnabled = false)
    {
        var detail = ex == null ? msg : $"{msg}: {ex.Message}";
        Write("ERROR", source, detail);
    }

    private static void Write(string level, string source, string msg)
    {
        try
        {
            var dir = Config.ConfigStore.LogDir;
            Directory.CreateDirectory(dir);
            var line = $"{DateTime.UtcNow:O} [{level}] {source} {msg}{Environment.NewLine}";
            lock (Lock)
            {
                File.AppendAllText(Path.Combine(dir, "service.log"), line);
            }
        }
        catch { }
    }
}
