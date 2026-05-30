using System.Text.Json;
using LanMsg.Core.Models;

namespace LanMsg.Core.Config;

public static class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string ConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LanMsg");

    public static string ConfigPath => Path.Combine(ConfigDir, "config.json");
    public static string LogDir => Path.Combine(ConfigDir, "logs");
    public static string QueueDbPath => Path.Combine(ConfigDir, "queue.db");

    public static string UserDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanMsg");

    public static string HistoryDbPath => Path.Combine(UserDir, "history.db");

    public static LanConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
                return CreateDefault();

            var json = File.ReadAllText(ConfigPath);
            var cfg = JsonSerializer.Deserialize<LanConfig>(json, JsonOpts) ?? CreateDefault();
            if (string.IsNullOrWhiteSpace(cfg.DeviceId))
                cfg.DeviceId = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(cfg.DisplayName))
                cfg.DisplayName = $"{Environment.UserName} on {Environment.MachineName}";
            return cfg;
        }
        catch
        {
            return CreateDefault();
        }
    }

    public static void Save(LanConfig cfg)
    {
        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(LogDir);
        var json = JsonSerializer.Serialize(cfg, JsonOpts);
        File.WriteAllText(ConfigPath, json);
    }

    public static LanConfig CreateDefault()
    {
        return new LanConfig
        {
            DeviceId = Guid.NewGuid().ToString("N"),
            DisplayName = $"{Environment.UserName} on {Environment.MachineName}"
        };
    }
}
