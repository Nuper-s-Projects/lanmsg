using System.Drawing;
using System.Windows;
using LanMsg.Core.Config;
using LanMsg.Core.Models;
using LanMsg.Ipc;
using LanMsg.Tray.Helpers;
using LanMsg.Tray.History;
using LanMsg.Tray.Services;
using LanMsg.Tray.Views;
using Forms = System.Windows.Forms;

namespace LanMsg.Tray;

public sealed class TrayHost : IDisposable
{
    private readonly IpcClient _ipc = new();
    private readonly HistoryStore _history = new();
    private readonly Forms.NotifyIcon _icon = new();
    private LanConfig _cfg = ConfigStore.Load();
    private SenderWindow? _sender;
    private SettingsWindow? _settings;
    private HistoryWindow? _historyWin;

    public void Start()
    {
        ServiceHelper.EnsureRunning();

        _ipc.PushReceived += OnPush;
        _ipc.StartListener();

        _icon.Text = "LanMsg";
        _icon.Icon = SystemIcons.Information;
        _icon.Visible = true;
        _icon.ContextMenuStrip = BuildMenu();
        _icon.DoubleClick += (_, _) => OpenSender();

        if (!_cfg.SetupComplete || string.IsNullOrWhiteSpace(_cfg.ProtectedGroupCode))
        {
            var wiz = new SetupWizardWindow(_ipc);
            wiz.ShowDialog();
        }
        else
        {
            _ = _ipc.RequestAsync(new IpcEnvelope { Kind = IpcKind.QueueReplay });
        }

        _ = RefreshConfig();
        _ = CheckService();
    }

    private async Task CheckService()
    {
        await Task.Delay(300);
        if (ServiceHelper.EnsureRunning())
            return;

        _icon.ShowBalloonTip(
            5000,
            "LanMsg",
            "Could not start the background service. Reinstall LanMsg or run as Administrator once.",
            Forms.ToolTipIcon.Warning);
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Sender", null, (_, _) => OpenSender());
        menu.Items.Add("Toggle Receiving", null, async (_, _) => await ToggleReceive());
        menu.Items.Add("View History", null, (_, _) => OpenHistory());
        menu.Items.Add("Settings", null, (_, _) => OpenSettings());
        menu.Items.Add("Exit", null, (_, _) => Exit());
        return menu;
    }

    private void OpenSender()
    {
        OpenSenderPublic();
    }

    public void OpenSenderPublic()
    {
        Ui.Run(() =>
        {
            if (_sender == null || !_sender.IsLoaded)
            {
                _sender = new SenderWindow(_ipc, _history);
                _sender.Closed += (_, _) => _sender = null;
                _sender.Show();
            }
            else
            {
                _sender.Activate();
            }
        });
    }

    private void OpenSettings()
    {
        OpenSettingsPublic();
    }

    public void OpenSettingsPublic()
    {
        Ui.Run(() =>
        {
            if (_settings == null || !_settings.IsLoaded)
            {
                _settings = new SettingsWindow(_ipc, _history);
                _settings.Closed += (_, _) => _settings = null;
                _settings.Show();
            }
            else _settings.Activate();
        });
    }

    private void OpenHistory()
    {
        Ui.Run(() =>
        {
            if (_historyWin == null || !_historyWin.IsLoaded)
            {
                _historyWin = new HistoryWindow(_history);
                _historyWin.Closed += (_, _) => _historyWin = null;
                _historyWin.Show();
            }
            else _historyWin.Activate();
        });
    }

    private async Task ToggleReceive()
    {
        var resp = await _ipc.RequestAsync(new IpcEnvelope
        {
            Kind = IpcKind.ToggleReceive,
            BoolValue = !_cfg.ReceiveEnabled
        });
        if (resp?.Config != null)
        {
            _cfg = resp.Config;
            _icon.ShowBalloonTip(2000, "LanMsg", _cfg.ReceiveEnabled ? "Receiving enabled" : "Receiving disabled", Forms.ToolTipIcon.Info);
        }
    }

    private async Task RefreshConfig()
    {
        var resp = await _ipc.RequestAsync(new IpcEnvelope { Kind = IpcKind.GetConfig });
        if (resp?.Config != null)
            _cfg = resp.Config;
    }

    private void OnPush(IpcEnvelope env)
    {
        if (env.Kind != IpcKind.InboundMessage || env.Message == null) return;
        var msg = env.Message;
        _history.Add(msg, "in");

        if (_cfg.PlaySounds)
            NotifySound.Play(msg.Priority);

        Ui.Run(() =>
        {
            var popup = new MessagePopupWindow(msg, _ipc, _history);
            popup.Show();
        });
    }

    private void Exit()
    {
        _icon.Visible = false;
        _icon.Dispose();
        Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _ipc.Dispose();
        _history.Dispose();
    }
}
