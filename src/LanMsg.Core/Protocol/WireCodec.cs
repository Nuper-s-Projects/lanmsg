using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using LanMsg.Core.Models;
using LanMsg.Core.Security;

namespace LanMsg.Core.Protocol;

public sealed class WireFrame
{
    public int Magic { get; set; } = LanDefaults.WireMagic;
    public int Version { get; set; } = LanDefaults.WireVersion;
    public string Nonce { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; }
    public byte[] Payload { get; set; } = Array.Empty<byte>();
    public byte[] Signature { get; set; } = Array.Empty<byte>();
}

public static class WireCodec
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static byte[] PackMessage(LanMessage msg, CryptoService crypto)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(msg, JsonOpts);
        var cipher = crypto.EncryptPayload(json, out var nonceBytes);
        var nonce = Convert.ToBase64String(nonceBytes);
        var frame = new WireFrame
        {
            Nonce = nonce,
            Timestamp = DateTimeOffset.UtcNow,
            Payload = cipher
        };
        return PackFrame(frame, crypto);
    }

    public static byte[] PackBeacon(DeviceInfo device, CryptoService crypto)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(device, JsonOpts);
        var cipher = crypto.EncryptPayload(json, out var nonceBytes);
        var frame = new WireFrame
        {
            Nonce = Convert.ToBase64String(nonceBytes),
            Timestamp = DateTimeOffset.UtcNow,
            Payload = cipher
        };
        return PackFrame(frame, crypto);
    }

    public static byte[] PackAck(AckResponse ack, CryptoService crypto)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(ack, JsonOpts);
        var cipher = crypto.EncryptPayload(json, out var nonceBytes);
        var frame = new WireFrame
        {
            Nonce = Convert.ToBase64String(nonceBytes),
            Timestamp = DateTimeOffset.UtcNow,
            Payload = cipher
        };
        return PackFrame(frame, crypto);
    }

    public static byte[] PackFrame(WireFrame frame, CryptoService crypto)
    {
        var header = JsonSerializer.SerializeToUtf8Bytes(new
        {
            frame.Magic,
            frame.Version,
            frame.Nonce,
            timestamp = frame.Timestamp.UtcDateTime.ToString("O"),
            payload = Convert.ToBase64String(frame.Payload)
        }, JsonOpts);

        var sig = crypto.Sign(header);
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            frame.Magic,
            frame.Version,
            frame.Nonce,
            timestamp = frame.Timestamp.UtcDateTime.ToString("O"),
            payload = Convert.ToBase64String(frame.Payload),
            signature = Convert.ToBase64String(sig)
        }, JsonOpts);

        var len = body.Length;
        var buf = new byte[4 + len];
        BinaryPrimitives.WriteInt32BigEndian(buf.AsSpan(0, 4), len);
        body.CopyTo(buf.AsSpan(4));
        return buf;
    }

    public static bool TryUnpack(ReadOnlySpan<byte> data, CryptoService crypto, out WireFrame? frame, out string? error)
    {
        frame = null;
        error = null;
        if (data.Length < 4)
        {
            error = "Frame too short";
            return false;
        }

        var len = BinaryPrimitives.ReadInt32BigEndian(data[..4]);
        if (len <= 0 || 4 + len > data.Length)
        {
            error = "Invalid frame length";
            return false;
        }

        var body = data.Slice(4, len);
        try
        {
            using var doc = JsonDocument.Parse(body.ToArray());
            var root = doc.RootElement;
            var magic = root.GetProperty("magic").GetInt32();
            var version = root.GetProperty("version").GetInt32();
            if (magic != LanDefaults.WireMagic || version != LanDefaults.WireVersion)
            {
                error = "Bad magic/version";
                return false;
            }

            var nonce = root.GetProperty("nonce").GetString() ?? "";
            var ts = DateTimeOffset.Parse(root.GetProperty("timestamp").GetString()!);
            var payload = Convert.FromBase64String(root.GetProperty("payload").GetString()!);
            var sig = Convert.FromBase64String(root.GetProperty("signature").GetString()!);

            var headerJson = JsonSerializer.SerializeToUtf8Bytes(new
            {
                magic,
                version,
                nonce,
                timestamp = ts.UtcDateTime.ToString("O"),
                payload = Convert.ToBase64String(payload)
            }, JsonOpts);

            if (!crypto.Verify(headerJson, sig))
            {
                error = "Invalid signature";
                return false;
            }

            if (!crypto.CheckReplay(nonce, ts))
            {
                error = "Replay detected";
                return false;
            }

            frame = new WireFrame
            {
                Magic = magic,
                Version = version,
                Nonce = nonce,
                Timestamp = ts,
                Payload = payload,
                Signature = sig
            };
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static LanMessage? DecodeMessage(WireFrame frame, CryptoService crypto)
    {
        var nonce = Convert.FromBase64String(frame.Nonce);
        var plain = crypto.DecryptPayload(frame.Payload, nonce);
        return JsonSerializer.Deserialize<LanMessage>(plain, JsonOpts);
    }

    public static DeviceInfo? DecodeBeacon(WireFrame frame, CryptoService crypto)
    {
        var nonce = Convert.FromBase64String(frame.Nonce);
        var plain = crypto.DecryptPayload(frame.Payload, nonce);
        return JsonSerializer.Deserialize<DeviceInfo>(plain, JsonOpts);
    }

    public static AckResponse? DecodeAck(WireFrame frame, CryptoService crypto)
    {
        var nonce = Convert.FromBase64String(frame.Nonce);
        var plain = crypto.DecryptPayload(frame.Payload, nonce);
        return JsonSerializer.Deserialize<AckResponse>(plain, JsonOpts);
    }

    public static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        var lenBuf = new byte[4];
        await ReadExactAsync(stream, lenBuf, ct);
        var len = BinaryPrimitives.ReadInt32BigEndian(lenBuf);
        if (len <= 0 || len > 1024 * 1024)
            throw new InvalidDataException("Invalid frame size");
        var body = new byte[len];
        await ReadExactAsync(stream, body, ct);
        var combined = new byte[4 + len];
        lenBuf.CopyTo(combined, 0);
        body.CopyTo(combined, 4);
        return combined;
    }

    public static async Task WriteFrameAsync(Stream stream, byte[] frame, CancellationToken ct)
    {
        await stream.WriteAsync(frame, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buf, CancellationToken ct)
    {
        var read = 0;
        while (read < buf.Length)
        {
            var n = await stream.ReadAsync(buf.AsMemory(read, buf.Length - read), ct);
            if (n == 0)
                throw new EndOfStreamException();
            read += n;
        }
    }
}
