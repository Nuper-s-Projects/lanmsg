using System.IO;
using System.Windows;
using System.Windows.Threading;
using LanMsg.Core.Logging;

namespace LanMsg.Tray;

public partial class App : System.Windows.Application
{
    public static TrayHost? Host { get; private set; }

    public App()
    {
        DispatcherUnhandledException += OnUnhandled;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                LogCrash("AppDomain", ex);
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Directory.CreateDirectory(LanMsg.Core.Config.ConfigStore.UserDir);
        Directory.CreateDirectory(LanMsg.Core.Config.ConfigStore.LogDir);

        try
        {
            Host = new TrayHost();
            Host.Start();

            if (e.Args.Contains("--sender"))
                Host.OpenSenderPublic();
            else if (e.Args.Contains("--settings"))
                Host.OpenSettingsPublic();
        }
        catch (Exception ex)
        {
            LogCrash("Startup", ex);
            System.Windows.MessageBox.Show(
                $"LanMsg failed to start:\n\n{ex.Message}",
                "LanMsg",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash("UI", e.Exception);
        System.Windows.MessageBox.Show(
            $"LanMsg error:\n\n{e.Exception.Message}",
            "LanMsg",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void LogCrash(string source, Exception ex)
    {
        try
        {
            var path = Path.Combine(LanMsg.Core.Config.ConfigStore.LogDir, "tray.log");
            File.AppendAllText(path, $"{DateTime.UtcNow:O} [{source}] {ex}\n");
        }
        catch { }
        AppLogger.Error("Tray", source, ex);
    }
}
