using System.Windows;
using System.Windows.Controls;
using LanMsg.Core.Config;
using LanMsg.Core.Models;
using LanMsg.Ipc;
using LanMsg.Client;

namespace LanMsg.Tray.Views;

public partial class SetupWizardWindow : Window
{
    private readonly IpcClient _ipc;
    private int _step;
    private System.Windows.Controls.TextBox? _groupCodeBox;
    private System.Windows.Controls.TextBox? _nameBox;
    private System.Windows.Controls.CheckBox? _receiveBox;
    private System.Windows.Controls.CheckBox? _allowlistBox;

    public SetupWizardWindow(IpcClient ipc)
    {
        InitializeComponent();
        _ipc = ipc;
        ShowStep();
    }

    private void ShowStep()
    {
        InputPanel.Children.Clear();
        BackBtn.Visibility = _step > 0 ? Visibility.Visible : Visibility.Collapsed;

        switch (_step)
        {
            case 0:
                TitleText.Text = "Welcome to LanMsg";
                BodyText.Text = "LanMsg lets trusted computers on your local network send popup messages. You must opt in to receive messages. Nothing runs stealthily.";
                NextBtn.Content = "Get Started";
                break;
            case 1:
                TitleText.Text = "Join your LAN group";
                BodyText.Text = "Enter the same group code on every computer that should talk to each other. Treat it like a shared Wi‑Fi password.";
                _groupCodeBox = new System.Windows.Controls.TextBox { Margin = new Thickness(0, 0, 0, 8) };
                InputPanel.Children.Add(new TextBlock { Text = "LAN group code", Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"), Margin = new Thickness(0, 0, 0, 4) });
                InputPanel.Children.Add(_groupCodeBox);
                NextBtn.Content = "Continue";
                break;
            case 2:
                TitleText.Text = "Your display name";
                BodyText.Text = "Other users will see this name on messages you send.";
                _nameBox = new System.Windows.Controls.TextBox { Text = $"{Environment.UserName} on {Environment.MachineName}" };
                InputPanel.Children.Add(new TextBlock { Text = "Display name", Foreground = (System.Windows.Media.Brush)FindResource("MutedBrush"), Margin = new Thickness(0, 0, 0, 4) });
                InputPanel.Children.Add(_nameBox);
                NextBtn.Content = "Continue";
                break;
            case 3:
                TitleText.Text = "Receiving messages";
                BodyText.Text = "You can turn receiving off anytime from the tray menu.";
                _receiveBox = new System.Windows.Controls.CheckBox { Content = "Allow this PC to receive messages", IsChecked = true, Margin = new Thickness(0, 0, 0, 8) };
                _allowlistBox = new System.Windows.Controls.CheckBox { Content = "Allowlist only (block unknown senders)", IsChecked = false };
                InputPanel.Children.Add(_receiveBox);
                InputPanel.Children.Add(_allowlistBox);
                NextBtn.Content = "Continue";
                break;
            case 4:
                TitleText.Text = "Test your setup";
                BodyText.Text = "Send a test popup to this computer.";
                NextBtn.Content = "Send Test Message";
                break;
            case 5:
                TitleText.Text = "You're ready";
                BodyText.Text = "1. Install LanMsg on other computers\n2. Enter the same LAN group code\n3. Click Test Message\n4. Start sending messages";
                NextBtn.Content = "Finish";
                break;
        }
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step == 1)
        {
            if (string.IsNullOrWhiteSpace(_groupCodeBox?.Text))
            {
                System.Windows.MessageBox.Show("Enter a group code.", "LanMsg", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var resp = await _ipc.RequestAsync(new IpcEnvelope { Kind = IpcKind.SetGroupCode, Text = _groupCodeBox.Text.Trim() });
            if (resp?.Kind == IpcKind.Error)
            {
                System.Windows.MessageBox.Show(resp.Text ?? "Failed to save group code.", "LanMsg", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }
        else if (_step == 2)
        {
            await SaveConfigPartial();
        }
        else if (_step == 3)
        {
            await SaveConfigPartial(receive: _receiveBox?.IsChecked == true, allowlist: _allowlistBox?.IsChecked == true);
        }
        else if (_step == 4)
        {
            var resp = await _ipc.RequestAsync(new IpcEnvelope
            {
                Kind = IpcKind.TestMessage,
                Send = new SendRequest { Body = "LanMsg test message — setup successful!", IsTest = true, Priority = MessagePriority.Normal }
            });
            if (resp?.Results?.FirstOrDefault()?.Status != DeliveryStatus.Delivered)
            {
                System.Windows.MessageBox.Show("Test failed. Ensure LanMsg Service is running and try again.", "LanMsg", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        else if (_step == 5)
        {
            Close();
            return;
        }

        _step++;
        ShowStep();
    }

    private async Task SaveConfigPartial(bool? receive = null, bool? allowlist = null)
    {
        var cfg = ConfigStore.Load();
        if (_nameBox != null)
            cfg.DisplayName = _nameBox.Text.Trim();
        if (receive.HasValue)
            cfg.ReceiveEnabled = receive.Value;
        if (allowlist.HasValue)
            cfg.AllowlistOnly = allowlist.Value;
        cfg.SetupComplete = _step >= 5;
        await _ipc.RequestAsync(new IpcEnvelope { Kind = IpcKind.UpdateConfig, Config = cfg });
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_step > 0)
        {
            _step--;
            ShowStep();
        }
    }
}
