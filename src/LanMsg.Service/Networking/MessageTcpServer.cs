using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using LanMsg.Core.Config;
using LanMsg.Core.Logging;
using LanMsg.Core.Models;
using LanMsg.Core.Protocol;
using LanMsg.Core.Security;
using LanMsg.Service.Storage;

namespace LanMsg.Service.Networking;

public sealed class MessageTcpServer : IDisposable
{
    private readonly LanConfig _cfg;
    private readonly Func<CryptoService?> _getCrypto;
    private readonly RateLimiter _limiter;
    private readonly AccessControl _access;
    private readonly MessageQueueStore _queue;
    private readonly Action<LanMessage> _onAccepted;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public MessageTcpServer(
        LanConfig cfg,
        Func<CryptoService?> getCrypto,
        RateLimiter limiter,
        AccessControl access,
        MessageQueueStore queue,
        Action<LanMessage> onAccepted)
    {
        _cfg = cfg;
        _getCrypto = getCrypto;
        _limiter = limiter;
        _access = access;
        _queue = queue;
        _onAccepted = onAccepted;
    }

    public void Start()
    {
        if (_loop != null) return;
        _listener = new TcpListener(IPAddress.Any, _cfg.MessagePort);
        _listener.Start();
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => AcceptLoop(_cts.Token));
        AppLogger.Info("TcpServer", $"Listening on port {_cfg.MessagePort}", debugEnabled: _cfg.DebugLogging);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _loop = null;
    }

    private async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => HandleClient(client, ct), ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                AppLogger.Error("TcpServer", "Accept failed", ex, _cfg.DebugLogging);
            }
        }
    }

    private async Task HandleClient(TcpClient client, CancellationToken ct)
    {
        var remoteIp = ((IPEndPoint)client.Client.RemoteEndPoint!).Address;
        try
        {
            await using var stream = client.GetStream();
            var frameBytes = await WireCodec.ReadFrameAsync(stream, ct);
            var crypto = _getCrypto();
            if (crypto == null)
            {
                await SendAck(stream, new AckResponse { Code = AckCode.InvalidKey, Detail = "No group key" }, null, ct);
                return;
            }

            if (!WireCodec.TryUnpack(frameBytes, crypto, out var wire, out var err))
            {
                await SendAck(stream, new AckResponse { Code = AckCode.InvalidKey, Detail = err ?? "Unpack failed" }, crypto, ct);
                return;
            }

            var msg = WireCodec.DecodeMessage(wire!, crypto);
            if (msg == null)
            {
                await SendAck(stream, new AckResponse { Code = AckCode.Error, Detail = "Decode failed" }, crypto, ct);
                return;
            }

            msg.Id ??= Guid.NewGuid().ToString("N");
            var senderKey = $"{remoteIp}|{msg.SenderId}";

            if (!_limiter.Allow(senderKey))
            {
                await SendAck(stream, new AckResponse { Code = AckCode.RateLimited, MessageId = msg.Id, Detail = "Rate limited" }, crypto, ct);
                return;
            }

            if (!_access.IsAllowed(msg.SenderId, remoteIp))
            {
                await SendAck(stream, new AckResponse { Code = AckCode.Blocked, MessageId = msg.Id, Detail = "Blocked" }, crypto, ct);
                return;
            }

            if (!_cfg.ReceiveEnabled)
            {
                await SendAck(stream, new AckResponse { Code = AckCode.ReceiveDisabled, MessageId = msg.Id, Detail = "Receive disabled" }, crypto, ct);
                return;
            }

            msg.SenderAddress = remoteIp.ToString();

            _queue.Enqueue(msg);
            _onAccepted(msg);
            await SendAck(stream, new AckResponse { Code = AckCode.Ok, MessageId = msg.Id, Detail = "OK" }, crypto, ct);
            AppLogger.Info("TcpServer", $"Accepted message {msg.Id} from {msg.SenderName}", debugEnabled: _cfg.DebugLogging);
        }
        catch (Exception ex)
        {
            AppLogger.Error("TcpServer", "Client handler error", ex, _cfg.DebugLogging);
        }
        finally
        {
            client.Dispose();
        }
    }

    private static async Task SendAck(NetworkStream stream, AckResponse ack, CryptoService? crypto, CancellationToken ct)
    {
        if (crypto == null) return;
        var frame = WireCodec.PackAck(ack, crypto);
        await WireCodec.WriteFrameAsync(stream, frame, ct);
    }

    public void Dispose() => Stop();
}
