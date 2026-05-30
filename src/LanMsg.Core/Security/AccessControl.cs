using System.Net;
using LanMsg.Core.Models;

namespace LanMsg.Core.Security;

public sealed class AccessControl
{
    private readonly LanConfig _cfg;

    public AccessControl(LanConfig cfg) => _cfg = cfg;

    public bool IsAllowed(string senderId, IPAddress remoteIp)
    {
        var ip = remoteIp.ToString();
        if (_cfg.BlockedIPs.Contains(ip, StringComparer.OrdinalIgnoreCase))
            return false;
        if (_cfg.BlockedDeviceIds.Contains(senderId, StringComparer.OrdinalIgnoreCase))
            return false;
        if (_cfg.AllowlistOnly && _cfg.AllowedDeviceIds.Count > 0)
            return _cfg.AllowedDeviceIds.Contains(senderId, StringComparer.OrdinalIgnoreCase);
        return true;
    }
}
