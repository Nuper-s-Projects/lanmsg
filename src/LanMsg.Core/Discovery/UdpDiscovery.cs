using System.Net;
using System.Net.Sockets;
using LanMsg.Core.Models;
using LanMsg.Core.Protocol;
using LanMsg.Core.Security;

namespace LanMsg.Core.Discovery;

public sealed class UdpDiscovery : IDisposable
{
    private readonly LanConfig _cfg;
    private readonly CryptoService _crypto;
    private readonly DeviceInfo _self;
    private UdpClient? _client;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private readonly object _lock = new();
    private readonly Dictionary<string, DeviceInfo> _peers = new();

    public event Action<DeviceInfo>? PeerDiscovered;
    public event Action<IReadOnlyList<DeviceInfo>>? PeerListChanged;

    public UdpDiscovery(LanConfig cfg, CryptoService crypto, DeviceInfo self)
    {
        _cfg = cfg;
        _crypto = crypto;
        _self = self;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_loop != null) return;
            _client = new UdpClient { EnableBroadcast = true };
            _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _client.Client.Bind(new IPEndPoint(IPAddress.Any, _cfg.DiscoveryPort));
            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => RunAsync(_cts.Token));
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _client?.Close();
            _client = null;
            _loop = null;
        }
    }

    public IReadOnlyList<DeviceInfo> GetPeers()
    {
        lock (_lock)
        {
            PruneStale();
            return _peers.Values.OrderBy(p => p.DisplayName).ToList();
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var client = _client!;
        var beaconInterval = TimeSpan.FromSeconds(5);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                _self.LastSeen = DateTimeOffset.UtcNow;
                _self.IpAddress = GetLocalIPv4();
                _self.ReceiveEnabled = _cfg.ReceiveEnabled;
                var packet = WireCodec.PackBeacon(_self, _crypto);
                var broadcast = new IPEndPoint(IPAddress.Broadcast, _cfg.DiscoveryPort);
                await client.SendAsync(packet, packet.Length, broadcast);
            }
            catch { }

            try
            {
                using var timeout = new CancellationTokenSource(500);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
                var result = await client.ReceiveAsync(linked.Token);
                HandlePacket(result.Buffer, result.RemoteEndPoint);
            }
            catch (OperationCanceledException) { }
            catch { }

            try { await Task.Delay(beaconInterval, ct); } catch { break; }
        }
    }

    private void HandlePacket(byte[] data, IPEndPoint remote)
    {
        if (!WireCodec.TryUnpack(data, _crypto, out var frame, out _))
            return;
        if (frame == null) return;

        var peer = WireCodec.DecodeBeacon(frame, _crypto);
        if (peer == null || peer.DeviceId == _self.DeviceId)
            return;

        peer.IpAddress = remote.Address.ToString();
        peer.LastSeen = DateTimeOffset.UtcNow;

        lock (_lock)
        {
            _peers[peer.DeviceId] = peer;
            PruneStale();
        }

        PeerDiscovered?.Invoke(peer);
        PeerListChanged?.Invoke(GetPeers());
    }

    private void PruneStale()
    {
        var stale = _peers.Where(p => p.Value.IsStale(LanDefaults.DiscoveryTtl)).Select(p => p.Key).ToList();
        foreach (var k in stale)
            _peers.Remove(k);
    }

    public static string GetLocalIPv4()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect("8.8.8.8", 65530);
            var ep = socket.LocalEndPoint as IPEndPoint;
            return ep?.Address.ToString() ?? "127.0.0.1";
        }
        catch
        {
            return Dns.GetHostAddresses(Dns.GetHostName())
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString() ?? "127.0.0.1";
        }
    }

    public void Dispose() => Stop();
}
