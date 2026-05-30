using System.Net;
using System.Net.Sockets;
using LanMsg.Core.Models;
using LanMsg.Core.Protocol;
using LanMsg.Core.Security;

namespace LanMsg.Core.Messaging;

public static class MessageClient
{
    public static async Task<DeliveryResult> SendAsync(
        string host,
        int port,
        LanMessage msg,
        CryptoService crypto,
        CancellationToken ct = default)
    {
        var target = $"{host}:{port}";
        try
        {
            using var client = new TcpClient();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(LanDefaults.SendTimeout);

            await client.ConnectAsync(host, port, linked.Token);
            await using var stream = client.GetStream();
            var frame = WireCodec.PackMessage(msg, crypto);
            await WireCodec.WriteFrameAsync(stream, frame, linked.Token);

            var ackFrame = await WireCodec.ReadFrameAsync(stream, linked.Token);
            if (!WireCodec.TryUnpack(ackFrame, crypto, out var wire, out _))
                return new DeliveryResult { Target = target, Status = DeliveryStatus.Error, Detail = "Bad ACK" };

            var ack = WireCodec.DecodeAck(wire!, crypto);
            if (ack == null)
                return new DeliveryResult { Target = target, Status = DeliveryStatus.Error, Detail = "ACK decode failed" };

            return ack.Code switch
            {
                AckCode.Ok => new DeliveryResult { Target = target, Status = DeliveryStatus.Delivered, Detail = "Delivered" },
                AckCode.InvalidKey => new DeliveryResult { Target = target, Status = DeliveryStatus.InvalidKey, Detail = "Invalid group key" },
                AckCode.Blocked => new DeliveryResult { Target = target, Status = DeliveryStatus.Blocked, Detail = "Blocked by recipient" },
                AckCode.RateLimited => new DeliveryResult { Target = target, Status = DeliveryStatus.RateLimited, Detail = "Rate limited" },
                AckCode.ReceiveDisabled => new DeliveryResult { Target = target, Status = DeliveryStatus.ReceiveDisabled, Detail = "Receiving disabled" },
                _ => new DeliveryResult { Target = target, Status = DeliveryStatus.Rejected, Detail = ack.Detail }
            };
        }
        catch (SocketException)
        {
            return new DeliveryResult { Target = target, Status = DeliveryStatus.Offline, Detail = "Offline or unreachable" };
        }
        catch (OperationCanceledException)
        {
            return new DeliveryResult { Target = target, Status = DeliveryStatus.Offline, Detail = "Timed out" };
        }
        catch (Exception ex)
        {
            return new DeliveryResult { Target = target, Status = DeliveryStatus.Error, Detail = ex.Message };
        }
    }

    public static async Task<List<DeliveryResult>> SendToManyAsync(
        IEnumerable<(string host, int port)> targets,
        LanMessage msg,
        CryptoService crypto,
        CancellationToken ct = default)
    {
        var tasks = targets.Select(t => SendAsync(t.host, t.port, msg, crypto, ct));
        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }
}
