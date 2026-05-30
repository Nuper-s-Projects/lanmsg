using System.IO;
using LanMsg.Core.Config;
using LanMsg.Core.Models;
using Microsoft.Data.Sqlite;

namespace LanMsg.Client;

public sealed class HistoryStore : IDisposable
{
    private readonly SqliteConnection _conn;

    public HistoryStore()
    {
        Directory.CreateDirectory(ConfigStore.UserDir);
        _conn = new SqliteConnection($"Data Source={ConfigStore.HistoryDbPath}");
        _conn.Open();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS history (
                id TEXT PRIMARY KEY,
                direction TEXT NOT NULL,
                senderName TEXT,
                hostname TEXT,
                body TEXT,
                priority INTEGER,
                timestamp TEXT
            );";
        cmd.ExecuteNonQuery();
    }

    public void Add(LanMessage msg, string direction)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO history (id, direction, senderName, hostname, body, priority, timestamp)
            VALUES ($id, $dir, $sn, $hn, $body, $pri, $ts)";
        cmd.Parameters.AddWithValue("$id", msg.Id);
        cmd.Parameters.AddWithValue("$dir", direction);
        cmd.Parameters.AddWithValue("$sn", msg.SenderName);
        cmd.Parameters.AddWithValue("$hn", msg.Hostname);
        cmd.Parameters.AddWithValue("$body", msg.Body);
        cmd.Parameters.AddWithValue("$pri", (int)msg.Priority);
        cmd.Parameters.AddWithValue("$ts", msg.Timestamp.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public List<HistoryEntry> GetAll(int limit = 200)
    {
        var list = new List<HistoryEntry>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, direction, senderName, hostname, body, priority, timestamp FROM history ORDER BY timestamp DESC LIMIT $lim";
        cmd.Parameters.AddWithValue("$lim", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new HistoryEntry
            {
                Id = r.GetString(0),
                Direction = r.GetString(1),
                SenderName = r.GetString(2),
                Hostname = r.GetString(3),
                Body = r.GetString(4),
                Priority = (MessagePriority)r.GetInt32(5),
                Timestamp = DateTimeOffset.Parse(r.GetString(6))
            });
        }
        return list;
    }

    public void Clear()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM history";
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _conn.Dispose();
}

public sealed class HistoryEntry
{
    public string Id { get; set; } = "";
    public string Direction { get; set; } = "";
    public string SenderName { get; set; } = "";
    public string Hostname { get; set; } = "";
    public string Body { get; set; } = "";
    public MessagePriority Priority { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}
