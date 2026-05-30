using System.Text.Json;
using LanMsg.Core.Models;
using Microsoft.Data.Sqlite;

namespace LanMsg.Service.Storage;

public sealed class MessageQueueStore : IDisposable
{
    private readonly SqliteConnection _conn;

    public MessageQueueStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _conn = new SqliteConnection($"Data Source={path}");
        _conn.Open();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS pending (
                id TEXT PRIMARY KEY,
                json TEXT NOT NULL,
                created TEXT NOT NULL
            );";
        cmd.ExecuteNonQuery();
    }

    public void Enqueue(LanMessage msg)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO pending (id, json, created) VALUES ($id, $json, $created)";
        cmd.Parameters.AddWithValue("$id", msg.Id);
        cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(msg));
        cmd.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public List<LanMessage> DequeueAll()
    {
        var list = new List<LanMessage>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, json FROM pending ORDER BY created";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var json = r.GetString(1);
            var msg = JsonSerializer.Deserialize<LanMessage>(json);
            if (msg != null) list.Add(msg);
        }
        if (list.Count > 0)
        {
            using var del = _conn.CreateCommand();
            del.CommandText = "DELETE FROM pending";
            del.ExecuteNonQuery();
        }
        return list;
    }

    public void Dispose() => _conn.Dispose();
}
