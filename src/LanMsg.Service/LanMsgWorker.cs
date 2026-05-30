using LanMsg.Core.Config;
using LanMsg.Core.Discovery;
using LanMsg.Core.Logging;
using LanMsg.Core.Messaging;
using LanMsg.Core.Models;
using LanMsg.Core.Security;
using LanMsg.Ipc;
using LanMsg.Service.Ipc;
using LanMsg.Service.Networking;
using LanMsg.Service.Storage;

namespace LanMsg.Service;

public sealed class LanMsgWorker : BackgroundService
{
    private LanConfig _cfg = ConfigStore.CreateDefault();
    private CryptoService? _crypto;
    private UdpDiscovery? _discovery;
    private MessageTcpServer? _tcp;
    private TrayBridge? _bridge;
    private MessageQueueStore? _queue;
    private RateLimiter? _limiter;
    private AccessControl? _access;
    private DeviceInfo _self = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Directory.CreateDirectory(ConfigStore.ConfigDir);
        Directory.CreateDirectory(ConfigStore.LogDir);
        _cfg = ConfigStore.Load();
        TryLoadCrypto();

        _self = new DeviceInfo
        {
            DeviceId = _cfg.DeviceId,
            Hostname = Environment.MachineName,
            DisplayName = _cfg.DisplayName,
            MessagePort = _cfg.MessagePort,
            ReceiveEnabled = _cfg.ReceiveEnabled
        };

        _queue = new MessageQueueStore(ConfigStore.QueueDbPath);
        _limiter = new RateLimiter(_cfg.RateLimitPerSenderPerMinute, _cfg.RateLimitGlobalPerMinute);
        _access = new AccessControl(_cfg);

        _tcp = new MessageTcpServer(_cfg, () => _crypto, _limiter, _access, _queue, OnInbound);
        _tcp.Start();

        if (_crypto != null)
        {
            _discovery = new UdpDiscovery(_cfg, _crypto, _self);
            _discovery.Start();
        }

        _bridge = new TrayBridge(HandleIpc);
        _bridge.Start();

        AppLogger.Info("Worker", "LanMsg service started", debugEnabled: _cfg.DebugLogging);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }

    private void TryLoadCrypto()
    {
        if (!string.IsNullOrWhiteSpace(_cfg.ProtectedGroupCode))
        {
            var code = CryptoService.UnprotectGroupCode(_cfg.ProtectedGroupCode);
            if (!string.IsNullOrWhiteSpace(code))
                _crypto = CryptoService.FromGroupCode(code);
        }
    }

    private void OnInbound(LanMessage msg)
    {
        _ = _bridge?.PushAsync(new IpcEnvelope { Kind = IpcKind.InboundMessage, Message = msg });
    }

    private async Task<IpcEnvelope?> HandleIpc(IpcEnvelope req)
    {
        switch (req.Kind)
        {
            case IpcKind.Ping:
                return new IpcEnvelope { Kind = IpcKind.Pong };

            case IpcKind.SetGroupCode:
                if (string.IsNullOrWhiteSpace(req.Text)) break;
                _cfg.GroupCodeVerifier = CryptoService.CreateVerifier(req.Text);
                _cfg.ProtectedGroupCode = CryptoService.ProtectGroupCode(req.Text);
                _cfg.SetupComplete = true;
                ConfigStore.Save(_cfg);
                _crypto = CryptoService.FromGroupCode(req.Text);
                RestartDiscovery();
                return new IpcEnvelope { Kind = IpcKind.Pong, Text = "Group key set" };

            case IpcKind.UpdateConfig:
                if (req.Config == null) break;
                _cfg.DisplayName = req.Config.DisplayName;
                _cfg.ReceiveEnabled = req.Config.ReceiveEnabled;
                _cfg.AllowlistOnly = req.Config.AllowlistOnly;
                _cfg.AllowedDeviceIds = req.Config.AllowedDeviceIds;
                _cfg.BlockedDeviceIds = req.Config.BlockedDeviceIds;
                _cfg.BlockedIPs = req.Config.BlockedIPs;
                _cfg.PlaySounds = req.Config.PlaySounds;
                _cfg.DebugLogging = req.Config.DebugLogging;
                _cfg.SetupComplete = req.Config.SetupComplete;
                ConfigStore.Save(_cfg);
                _self.DisplayName = _cfg.DisplayName;
                _self.ReceiveEnabled = _cfg.ReceiveEnabled;
                _access = new AccessControl(_cfg);
                return new IpcEnvelope { Kind = IpcKind.Pong, Text = "Config updated" };

            case IpcKind.ToggleReceive:
                _cfg.ReceiveEnabled = req.BoolValue;
                _self.ReceiveEnabled = req.BoolValue;
                ConfigStore.Save(_cfg);
                return new IpcEnvelope { Kind = IpcKind.ConfigSnapshot, Config = _cfg };

            case IpcKind.GetConfig:
                return new IpcEnvelope { Kind = IpcKind.ConfigSnapshot, Config = _cfg };

            case IpcKind.GetDevices:
                var peers = (_discovery?.GetPeers() ?? Array.Empty<DeviceInfo>()).ToList();
                return new IpcEnvelope { Kind = IpcKind.DeviceList, Devices = peers };

            case IpcKind.QueueReplay:
                var pending = _queue?.DequeueAll() ?? new List<LanMessage>();
                foreach (var m in pending)
                    await (_bridge?.PushAsync(new IpcEnvelope { Kind = IpcKind.InboundMessage, Message = m }) ?? Task.CompletedTask);
                return new IpcEnvelope { Kind = IpcKind.Pong, Text = $"{pending.Count} replayed" };

            case IpcKind.SendRequest:
            case IpcKind.TestMessage:
                return await HandleSend(req);

            default:
                return new IpcEnvelope { Kind = IpcKind.Error, Text = "Unknown request" };
        }
        return new IpcEnvelope { Kind = IpcKind.Error, Text = "Unhandled" };
    }

    private async Task<IpcEnvelope> HandleSend(IpcEnvelope req)
    {
        if (_crypto == null)
            return new IpcEnvelope { Kind = IpcKind.SendResult, Results = new List<DeliveryResult> { new() { Status = DeliveryStatus.InvalidKey, Detail = "Group key not configured" } } };

        var send = req.Send ?? new SendRequest { Body = req.Text ?? "" };
        if (string.IsNullOrWhiteSpace(send.Body))
            return new IpcEnvelope { Kind = IpcKind.Error, Text = "Empty message" };

        var msg = new LanMessage
        {
            SenderId = _cfg.DeviceId,
            SenderName = _cfg.DisplayName,
            Hostname = Environment.MachineName,
            Body = send.Body,
            Priority = send.Priority,
            Timestamp = DateTimeOffset.UtcNow
        };

        var targets = ResolveTargets(send);
        if (send.IsTest || req.Kind == IpcKind.TestMessage)
        {
            await (_bridge?.PushAsync(new IpcEnvelope { Kind = IpcKind.InboundMessage, Message = msg }) ?? Task.CompletedTask);
            return new IpcEnvelope
            {
                Kind = IpcKind.SendResult,
                Results = new List<DeliveryResult> { new() { Target = "local", Status = DeliveryStatus.Delivered, Detail = "Test message shown locally" } }
            };
        }

        var results = await MessageClient.SendToManyAsync(targets, msg, _crypto);
        return new IpcEnvelope { Kind = IpcKind.SendResult, Results = results };
    }

    private List<(string host, int port)> ResolveTargets(SendRequest send)
    {
        var list = new List<(string, int)>();
        var peers = _discovery?.GetPeers() ?? new List<DeviceInfo>();

        if (send.Broadcast)
        {
            foreach (var p in peers)
                list.Add((p.IpAddress, p.MessagePort));
        }

        foreach (var id in send.TargetDeviceIds)
        {
            var peer = peers.FirstOrDefault(p => p.DeviceId == id);
            if (peer != null)
                list.Add((peer.IpAddress, peer.MessagePort));
        }

        foreach (var host in send.TargetHosts)
        {
            var h = host.Trim();
            if (string.IsNullOrWhiteSpace(h)) continue;
            list.Add((h, _cfg.MessagePort));
        }

        return list.Distinct().ToList();
    }

    private void RestartDiscovery()
    {
        _discovery?.Stop();
        if (_crypto != null)
        {
            _discovery = new UdpDiscovery(_cfg, _crypto, _self);
            _discovery.Start();
        }
    }

    public override void Dispose()
    {
        _tcp?.Dispose();
        _discovery?.Dispose();
        _bridge?.Dispose();
        _queue?.Dispose();
        base.Dispose();
    }
}
