namespace LanMsg.Core.Models;

public sealed class DeviceInfo
{
    public string DeviceId { get; set; } = "";
    public string Hostname { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public int MessagePort { get; set; } = LanDefaults.MessagePort;
    public bool ReceiveEnabled { get; set; } = true;
    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;

    public string Label => string.IsNullOrWhiteSpace(IpAddress)
        ? DisplayName
        : $"{DisplayName} ({IpAddress})";

    public bool IsStale(TimeSpan ttl) => DateTimeOffset.UtcNow - LastSeen > ttl;
}
