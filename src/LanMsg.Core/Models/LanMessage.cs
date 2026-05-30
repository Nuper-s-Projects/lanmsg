namespace LanMsg.Core.Models;

public sealed class LanMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SenderId { get; set; } = "";
    public string SenderName { get; set; } = "";
    public string Hostname { get; set; } = "";
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public MessagePriority Priority { get; set; } = MessagePriority.Normal;
    public string Body { get; set; } = "";
    public string? ReplyToId { get; set; }
    public string? SenderAddress { get; set; }
    public bool IsReply => !string.IsNullOrEmpty(ReplyToId);
}
