namespace LanMsg.Core.Protocol;

public enum AckCode
{
    Ok = 0,
    InvalidKey = 1,
    Blocked = 2,
    RateLimited = 3,
    ReceiveDisabled = 4,
    Replay = 5,
    Error = 99
}

public sealed class AckResponse
{
    public AckCode Code { get; set; }
    public string MessageId { get; set; } = "";
    public string Detail { get; set; } = "";
}
