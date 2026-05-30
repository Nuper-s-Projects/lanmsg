using System.Media;
using System.Windows;
using LanMsg.Core.Models;

namespace LanMsg.Tray.Helpers;

public static class NotifySound
{
    public static void Play(MessagePriority priority)
    {
        try
        {
            switch (priority)
            {
                case MessagePriority.Urgent:
                    SystemSounds.Hand.Play();
                    break;
                case MessagePriority.Important:
                    SystemSounds.Exclamation.Play();
                    break;
                default:
                    SystemSounds.Asterisk.Play();
                    break;
            }
        }
        catch { }
    }
}

public static class Ui
{
    public static void Run(Action action)
    {
        if (System.Windows.Application.Current?.Dispatcher.CheckAccess() == true)
            action();
        else
            System.Windows.Application.Current?.Dispatcher.Invoke(action);
    }
}
