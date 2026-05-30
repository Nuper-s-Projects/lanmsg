namespace LanMsg.Core.Security;

public sealed class RateLimiter
{
    private readonly int _perSenderLimit;
    private readonly int _globalLimit;
    private readonly Dictionary<string, Queue<DateTime>> _senderBuckets = new();
    private readonly Queue<DateTime> _globalBucket = new();
    private readonly object _lock = new();

    public RateLimiter(int perSenderLimit, int globalLimit)
    {
        _perSenderLimit = perSenderLimit;
        _globalLimit = globalLimit;
    }

    public bool Allow(string senderKey)
    {
        lock (_lock)
        {
            Prune(_globalBucket, TimeSpan.FromMinutes(1));
            if (_globalBucket.Count >= _globalLimit)
                return false;

            if (!_senderBuckets.TryGetValue(senderKey, out var q))
            {
                q = new Queue<DateTime>();
                _senderBuckets[senderKey] = q;
            }

            Prune(q, TimeSpan.FromMinutes(1));
            if (q.Count >= _perSenderLimit)
                return false;

            var now = DateTime.UtcNow;
            q.Enqueue(now);
            _globalBucket.Enqueue(now);
            return true;
        }
    }

    private static void Prune(Queue<DateTime> q, TimeSpan window)
    {
        var cutoff = DateTime.UtcNow - window;
        while (q.Count > 0 && q.Peek() < cutoff)
            q.Dequeue();
    }
}
