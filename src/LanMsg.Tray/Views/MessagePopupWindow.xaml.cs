using System.Windows;
using System.Windows.Media;
using LanMsg.Core.Models;
using LanMsg.Ipc;
using LanMsg.Tray.History;
using LanMsg.Tray.Services;

namespace LanMsg.Tray.Views;

public partial class MessagePopupWindow : Window
{
    private readonly LanMessage _msg;
    private readonly IpcClient _ipc;
    private readonly HistoryStore _history;
    private bool _replyOpen;

    public MessagePopupWindow(LanMessage msg, IpcClient ipc, HistoryStore history)
    {
        InitializeComponent();
        _msg = msg;
        _ipc = ipc;
        _history = history;

        SenderText.Text = msg.SenderName;
        DeviceText.Text = msg.Hostname;
        TimeText.Text = msg.Timestamp.ToLocalTime().ToString("f");
        BodyText.Text = msg.Body;
        PriorityBadge.Text = msg.Priority.ToString().ToUpperInvariant();
        PriorityBadge.Foreground = msg.Priority switch
        {
            MessagePriority.Urgent => (System.Windows.Media.Brush)FindResource("DangerBrush"),
            MessagePriority.Important => (System.Windows.Media.Brush)FindResource("WarnBrush"),
            _ => (System.Windows.Media.Brush)FindResource("MutedBrush")
        };

        var work = SystemParameters.WorkArea;
        Left = work.Right - Width - 24;
        Top = work.Bottom - 200;

        var timeout = msg.Priority switch
        {
            MessagePriority.Normal => 60,
            MessagePriority.Important => 120,
            _ => 0
        };
        if (timeout > 0)
        {
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(timeout) };
            timer.Tick += (_, _) => { timer.Stop(); Close(); };
            timer.Start();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Clipboard.SetText($"{_msg.SenderName} ({_msg.Hostname})\n{_msg.Timestamp.ToLocalTime():f}\n\n{_msg.Body}");
    }

    private async void Reply_Click(object sender, RoutedEventArgs e)
    {
        if (!_replyOpen)
        {
            _replyOpen = true;
            ReplyBox.Visibility = Visibility.Visible;
            ((System.Windows.Controls.Button)sender).Content = "Send Reply";
            ReplyBox.Focus();
            return;
        }

        var text = ReplyBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        var host = ResolveHost();
        if (host == null)
        {
            System.Windows.MessageBox.Show("Cannot determine sender address for reply.", "LanMsg", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var resp = await _ipc.RequestAsync(new IpcEnvelope
        {
            Kind = IpcKind.SendRequest,
            Send = new SendRequest
            {
                TargetHosts = new List<string> { host },
                Body = text,
                Priority = MessagePriority.Normal
            }
        });

        _history.Add(new LanMessage
        {
            SenderName = "You",
            Hostname = Environment.MachineName,
            Body = text,
            Priority = MessagePriority.Normal
        }, "out");

        Close();
    }

    private string? ResolveHost()
    {
        if (!string.IsNullOrWhiteSpace(_msg.SenderAddress))
            return _msg.SenderAddress;
        return _msg.Hostname;
    }
}
