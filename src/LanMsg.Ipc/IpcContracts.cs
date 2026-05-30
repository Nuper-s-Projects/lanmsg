namespace LanMsg.Ipc;

public static class PipeNames
{
    public const string RequestPipe = "LanMsg.RequestPipe";
    public const string PushPipe = "LanMsg.PushPipe";
}

public enum IpcKind
{
    Ping,
    Pong,
    InboundMessage,
    SendRequest,
    SendResult,
    GetDevices,
    DeviceList,
    GetConfig,
    ConfigSnapshot,
    UpdateConfig,
    SetGroupCode,
    ToggleReceive,
    TestMessage,
    QueueReplay,
    Error
}
