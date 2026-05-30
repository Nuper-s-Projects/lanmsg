namespace LanMsg.Core.Models;

public sealed class LanConfig
{
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");
    public string DisplayName { get; set; } = "";
    public string GroupCodeVerifier { get; set; } = "";
    public string ProtectedGroupCode { get; set; } = "";
    public bool ReceiveEnabled { get; set; } = true;
    public bool AllowlistOnly { get; set; } = false;
    public List<string> AllowedDeviceIds { get; set; } = new();
    public List<string> BlockedDeviceIds { get; set; } = new();
    public List<string> BlockedIPs { get; set; } = new();
    public int DiscoveryPort { get; set; } = LanDefaults.DiscoveryPort;
    public int MessagePort { get; set; } = LanDefaults.MessagePort;
    public int RateLimitPerSenderPerMinute { get; set; } = 10;
    public int RateLimitGlobalPerMinute { get; set; } = 30;
    public bool PlaySounds { get; set; } = true;
    public bool DebugLogging { get; set; } = false;
    public bool SetupComplete { get; set; } = false;
}

public static class LanDefaults
{
    public const int DiscoveryPort = 9847;
    public const int MessagePort = 9848;
    public const int WireMagic = 0x4C4D5347;
    public const int WireVersion = 1;
    public const string AppSalt = "LanMsg-v1-group";
    public static readonly TimeSpan DiscoveryTtl = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan ReplayWindow = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(2);
}
