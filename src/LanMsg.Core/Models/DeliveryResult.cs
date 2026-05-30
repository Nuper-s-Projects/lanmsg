namespace LanMsg.Core.Models;

public enum DeliveryStatus
{
    Delivered,
    Offline,
    InvalidKey,
    Blocked,
    RateLimited,
    Rejected,
    ReceiveDisabled,
    Error
}

public sealed class DeliveryResult
{
    public string Target { get; set; } = "";
    public DeliveryStatus Status { get; set; }
    public string Detail { get; set; } = "";
}
