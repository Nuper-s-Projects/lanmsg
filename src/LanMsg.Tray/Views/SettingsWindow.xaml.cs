using System.Windows;
using LanMsg.Core.Models;
using LanMsg.Ipc;
using LanMsg.Client;

namespace LanMsg.Tray.Views;

public partial class SettingsWindow : Window
{
    private readonly IpcClient _ipc;
    private readonly HistoryStore _history;
    private LanConfig _cfg = new();

    public SettingsWindow(IpcClient ipc, HistoryStore history)
    {
        InitializeComponent();
        _ipc = ipc;
        _history = history;
        Loaded += async (_, _) => await Load();
    }

    private async Task Load()
    {
        var resp = await _ipc.RequestAsync(new IpcEnvelope { Kind = IpcKind.GetConfig });
        if (resp?.Config == null) return;
        _cfg = resp.Config;
        NameBox.Text = _cfg.DisplayName;
        ReceiveBox.IsChecked = _cfg.ReceiveEnabled;
        SoundBox.IsChecked = _cfg.PlaySounds;
        AllowlistBox.IsChecked = _cfg.AllowlistOnly;
        DebugBox.IsChecked = _cfg.DebugLogging;
        BlockDevicesBox.Text = string.Join(Environment.NewLine, _cfg.BlockedDeviceIds);
        BlockIpsBox.Text = string.Join(Environment.NewLine, _cfg.BlockedIPs);
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var code = GroupBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(code))
        {
            var set = await _ipc.RequestAsync(new IpcEnvelope { Kind = IpcKind.SetGroupCode, Text = code });
            if (set?.Kind == IpcKind.Error)
            {
                System.Windows.MessageBox.Show(set.Text ?? "Failed to update group code.", "LanMsg", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        _cfg.DisplayName = NameBox.Text.Trim();
        _cfg.ReceiveEnabled = ReceiveBox.IsChecked == true;
        _cfg.PlaySounds = SoundBox.IsChecked == true;
        _cfg.AllowlistOnly = AllowlistBox.IsChecked == true;
        _cfg.DebugLogging = DebugBox.IsChecked == true;
        _cfg.BlockedDeviceIds = BlockDevicesBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        _cfg.BlockedIPs = BlockIpsBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        _cfg.SetupComplete = true;

        var resp = await _ipc.RequestAsync(new IpcEnvelope { Kind = IpcKind.UpdateConfig, Config = _cfg });
        if (resp?.Kind == IpcKind.Error)
            System.Windows.MessageBox.Show(resp.Text ?? "Save failed.", "LanMsg", MessageBoxButton.OK, MessageBoxImage.Error);
        else
            System.Windows.MessageBox.Show("Settings saved.", "LanMsg", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.MessageBox.Show("Clear all local message history?", "LanMsg", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _history.Clear();
            System.Windows.MessageBox.Show("History cleared.", "LanMsg", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
