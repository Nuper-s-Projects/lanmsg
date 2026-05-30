using System.Diagnostics;
using System.ServiceProcess;
using LanMsg.Core.Logging;

namespace LanMsg.Client;

public static class ServiceHelper
{
    public const string ServiceName = "LanMsgService";

    public static bool IsRunning()
    {
        try
        {
            using var sc = new ServiceController(ServiceName);
            return sc.Status == ServiceControllerStatus.Running;
        }
        catch
        {
            return false;
        }
    }

    public static bool EnsureRunning()
    {
        if (IsRunning()) return true;
        if (TryStart()) return true;
        if (TryStartElevated()) return true;
        return IsRunning();
    }

    public static bool TryStart(int waitSec = 20)
    {
        try
        {
            using var sc = new ServiceController(ServiceName);
            if (sc.Status == ServiceControllerStatus.Running) return true;

            if (sc.Status == ServiceControllerStatus.StartPending)
            {
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(waitSec));
                return sc.Status == ServiceControllerStatus.Running;
            }

            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(waitSec));
            return sc.Status == ServiceControllerStatus.Running;
        }
        catch (Exception ex)
        {
            AppLogger.Error("ServiceHelper", "Start failed", ex);
            return false;
        }
    }

    public static bool TryStartElevated()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"Start-Service -Name '{ServiceName}' -ErrorAction Stop\"",
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(15000);
            return IsRunning();
        }
        catch
        {
            return false;
        }
    }
}
