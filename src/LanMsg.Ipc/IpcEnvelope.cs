using LanMsg.Core.Models;

namespace LanMsg.Ipc;

public sealed class IpcEnvelope
{
    public IpcKind Kind { get; set; }
    public LanMessage? Message { get; set; }
    public SendRequest? Send { get; set; }
    public List<DeliveryResult>? Results { get; set; }
    public List<DeviceInfo>? Devices { get; set; }
    public LanConfig? Config { get; set; }
    public string? Text { get; set; }
    public bool BoolValue { get; set; }
}

public sealed class SendRequest
{
    public List<string> TargetDeviceIds { get; set; } = new();
    public List<string> TargetHosts { get; set; } = new();
    public bool Broadcast { get; set; }
    public string Body { get; set; } = "";
    public MessagePriority Priority { get; set; } = MessagePriority.Normal;
    public bool IsTest { get; set; }
}

public static class IpcJson
{
    private static readonly System.Text.Json.JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static byte[] Serialize(IpcEnvelope env) =>
        System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(env, Opts);

    public static IpcEnvelope? Deserialize(byte[] data) =>
        System.Text.Json.JsonSerializer.Deserialize<IpcEnvelope>(data, Opts);
}
