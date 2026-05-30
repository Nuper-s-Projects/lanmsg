using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using LanMsg.Core.Models;
using LanMsg.Ipc;
using LanMsg.Client;

namespace LanMsg.Tray.Views;

public partial class SenderWindow : Window
{
    private readonly IpcClient _ipc;
    private readonly HistoryStore _history;
    private readonly ObservableCollection<DeviceRow> _devices = new();
    private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;

    public SenderWindow(IpcClient ipc, HistoryStore history)
    {
        InitializeComponent();
        _ipc = ipc;
        _history = history;
        DeviceList.ItemsSource = _devices;
        _refreshTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _refreshTimer.Tick += async (_, _) => await LoadDevices();
        Loaded += async (_, _) => await LoadDevices();
        _refreshTimer.Start();
    }

    private async Task LoadDevices()
    {
        var resp = await _ipc.RequestAsync(new IpcEnvelope { Kind = IpcKind.GetDevices });
        if (resp?.Devices == null) return;

        var selected = _devices.Where(d => d.IsSelected).Select(d => d.DeviceId).ToHashSet();
        _devices.Clear();
        foreach (var d in resp.Devices)
        {
            _devices.Add(new DeviceRow
            {
                DeviceId = d.DeviceId,
                Label = d.Label,
                IsSelected = selected.Contains(d.DeviceId)
            });
        }
    }

    private void Log(string line)
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}\n");
        LogBox.ScrollToEnd();
    }

    private MessagePriority GetPriority()
    {
        var idx = PriorityBox.SelectedIndex;
        return idx switch { 1 => MessagePriority.Important, 2 => MessagePriority.Urgent, _ => MessagePriority.Normal };
    }

    private async void Send_Click(object sender, RoutedEventArgs e) => await Send(false);
    private async void Broadcast_Click(object sender, RoutedEventArgs e) => await Send(true);
    private async void Test_Click(object sender, RoutedEventArgs e) => await Send(false, true);
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadDevices();

    private async Task Send(bool broadcast, bool test = false)
    {
        var body = MessageInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(body) && !test)
        {
            System.Windows.MessageBox.Show("Enter a message.", "LanMsg", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (test)
            body = string.IsNullOrWhiteSpace(body) ? "LanMsg test message — setup successful!" : body;

        var req = new SendRequest
        {
            Body = body,
            Priority = GetPriority(),
            Broadcast = broadcast,
            IsTest = test,
            TargetDeviceIds = _devices.Where(d => d.IsSelected).Select(d => d.DeviceId).ToList()
        };

        var manual = ManualHost.Text.Trim();
        if (!string.IsNullOrWhiteSpace(manual))
            req.TargetHosts.Add(manual);

        if (!broadcast && !test && req.TargetDeviceIds.Count == 0 && req.TargetHosts.Count == 0)
        {
            System.Windows.MessageBox.Show("Select a device or enter a host/IP.", "LanMsg", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var resp = await _ipc.RequestAsync(new IpcEnvelope { Kind = IpcKind.SendRequest, Send = req });
        if (resp?.Kind == IpcKind.Error)
        {
            Log(resp.Text ?? "Error");
            return;
        }

        foreach (var r in resp?.Results ?? new List<DeliveryResult>())
            Log($"{r.Target}: {r.Status} — {r.Detail}");

        if (!test)
        {
            _history.Add(new LanMessage
            {
                SenderName = Environment.UserName,
                Hostname = Environment.MachineName,
                Body = body,
                Priority = req.Priority
            }, "out");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _refreshTimer.Stop();
        base.OnClosed(e);
    }
}

public sealed class DeviceRow
{
    public string DeviceId { get; set; } = "";
    public string Label { get; set; } = "";
    public bool IsSelected { get; set; }
}
