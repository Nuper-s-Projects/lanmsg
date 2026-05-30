using System.Text.Json;
using LanMsg.Client;
using LanMsg.Core.Config;
using LanMsg.Core.Models;
using LanMsg.Ipc;

namespace LanMsg.Cli;

internal static class Program
{
    private static int Main(string[] args) => CliApp.Run(args).GetAwaiter().GetResult();
}

internal sealed class CliApp : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private readonly IpcClient _ipc = new();
    private readonly HistoryStore _history = new();
    private bool _json;

    public static async Task<int> Run(string[] args)
    {
        using var app = new CliApp();
        return await app.Exec(args);
    }

    private async Task<int> Exec(string[] raw)
    {
        var args = new List<string>();
        foreach (var a in raw)
        {
            if (a == "--json") _json = true;
            else if (a is "-h" or "--help" or "-?") { PrintHelp(); return 0; }
            else args.Add(a);
        }

        if (args.Count == 0) { PrintHelp(); return 0; }

        var cmd = args[0].ToLowerInvariant();
        try
        {
            return cmd switch
            {
                "ping" => await Ping(),
                "service" => Service(args.Skip(1).ToList()),
                "config" => await Config(args.Skip(1).ToList()),
                "group" => await Group(args.Skip(1).ToList()),
                "setup" => await Setup(args.Skip(1).ToList()),
                "receive" => await Receive(args.Skip(1).ToList()),
                "devices" => await Devices(),
                "send" => await Send(args.Skip(1).ToList(), test: false),
                "test" => await Send(args.Skip(1).ToList(), test: true),
                "reply" => await Reply(args.Skip(1).ToList()),
                "queue" => await Queue(args.Skip(1).ToList()),
                "watch" => await Watch(),
                "history" => History(args.Skip(1).ToList()),
                "tray" => Tray(args.Skip(1).ToList()),
                "gui" => Tray(args.Skip(1).ToList()),
                "help" => ShowHelp(),
                _ => Fail($"Unknown command: {cmd}. Run lanmsg help.")
            };
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    private static int ShowHelp()
    {
        PrintHelp();
        return 0;
    }

    private async Task<int> Ping()
    {
        if (!EnsureService()) return 1;
        var resp = await _ipc.RequestAsync(new IpcEnvelope { Kind = IpcKind.Ping });
        if (resp?.Kind == IpcKind.Error) return Fail(resp.Text ?? "Ping failed");
        if (resp?.Kind != IpcKind.Pong) return Fail("Unexpected response");
        return Ok(new { status = "ok", message = resp.Text ?? "pong" });
    }

    private int Service(List<string> args)
    {
        if (args.Count == 0 || args[0] is "status") return Ok(new { running = ServiceHelper.IsRunning(), name = ServiceHelper.ServiceName });
        if (args[0] == "start")
        {
            var ok = ServiceHelper.EnsureRunning();
            return ok ? Ok(new { running = true, started = true }) : Fail("Could not start LanMsg service.");
        }
        return Fail("Usage: lanmsg service [status|start]");
    }

    private async Task<int> Config(List<string> args)
    {
        if (args.Count == 0) return Fail("Usage: lanmsg config get|set ...");
        if (args[0] == "get")
        {
            if (!EnsureService()) return 1;
            var resp = await _ipc.RequestAsync(new IpcEnvelope { Kind = IpcKind.GetConfig });
            if (resp?.Config == null) return Fail("Could not load config.");
            return Ok(resp.Config);
        }
        if (args[0] == "set")
        {
            if (!EnsureService()) return 1;
            var resp = await _ipc.RequestAsync(new IpcEnvelope { Kind = IpcKind.GetConfig });
            if (resp?.Config == null) return Fail("Could not load config.");
            var cfg = resp.Config;

            var code = Opt(args, "--code");
            if (code != null)
            {
                var set = await _ipc.RequestAsync(new IpcEnvelope { Kind = IpcKind.SetGroupCode, Text = code });
                if (set?.Kind == IpcKind.Error) return Fail(set.Text ?? "Group code failed.");
            }

            if (Opt(args, "--name") is { } name) cfg.DisplayName = name;
            if (ParseBool(args, "--receive") is { } recv) cfg.ReceiveEnabled = recv;
            if (ParseBool(args, "--sounds") is { } snd) cfg.PlaySounds = snd;
            if (ParseBool(args, "--allowlist") is { } al) cfg.AllowlistOnly = al;
            if (ParseBool(args, "--debug") is { } dbg) cfg.DebugLogging = dbg;
            if (ParseBool(args, "--setup-complete") is { } sc) cfg.SetupComplete = sc;
            if (OptList(args, "--block-devices") is { } bd) cfg.BlockedDeviceIds = bd;
            if (OptList(args, "--block-ips") is { } bi) cfg.BlockedIPs = bi;
            if (OptList(args, "--allowed-devices") is { } ad) cfg.AllowedDeviceIds = ad;

            var upd = await _ipc.RequestAsync(new IpcEnvelope { Kind = IpcKind.UpdateConfig, Config = cfg });
            if (upd?.Kind == IpcKind.Error) return Fail(upd.Text ?? "Save failed.");
            return Ok(new { saved = true, config = cfg });
        }
        return Fail("Usage: lanmsg config get|set [--name ...] [--receive on|off] ...");
    }

    private async Task<int> Group(List<string> args)
    {
        if (args.Count < 2 || args[0] != "set") return Fail("Usage: lanmsg group set <code>");
        if (!EnsureService()) return 1;
        var resp = await _ipc.RequestAsync(new IpcEnvelope { Kind = IpcKind.SetGroupCode, Text = args[1] });
        if (resp?.Kind == IpcKind.Error) return Fail(resp.Text ?? "Failed.");
        return Ok(new { groupCodeSet = true });
    }

    private async Task<int> Setup(List<string> args)
    {
        if (!EnsureService()) return 1;

        var code = Opt(args, "--code");
        var name = Opt(args, "--name") ?? $"{Environment.UserName} on {Environment.MachineName}";
        var receive = ParseBool(args, "--receive") ?? true;
        var allowlist = ParseBool(args, "--allowlist") ?? false;
        var runTest = !HasFlag(args, "--skip-test");
        var finish = HasFlag(args, "--finish") || HasFlag(args, "-f");

        if (string.IsNullOrWhiteSpace(code))
            return Fail("Setup requires --code <lan-group-code>");

        var set = await _ipc.RequestAsync(new IpcEnvelope { Kind = IpcKind.SetGroupCode, Text = code });
        if (set?.Kind == IpcKind.Error) return Fail(set.Text ?? "Group code failed.");

        var cfg = ConfigStore.Load();
        cfg.DisplayName = name;
        cfg.ReceiveEnabled = receive;
        cfg.AllowlistOnly = allowlist;
        cfg.SetupComplete = finish;

        var upd = await _ipc.RequestAsync(new IpcEnvelope { Kind = IpcKind.UpdateConfig, Config = cfg });
        if (upd?.Kind == IpcKind.Error) return Fail(upd.Text ?? "Config save failed.");

        if (runTest)
        {
            var test = await _ipc.RequestAsync(new IpcEnvelope
            {
                Kind = IpcKind.TestMessage,
                Send = new SendRequest { Body = "LanMsg test message — setup successful!", IsTest = true }
            });
            var ok = test?.Results?.FirstOrDefault()?.Status == DeliveryStatus.Delivered;
            if (!ok) return Fail("Setup saved but test message failed. Check that the service is running.");
        }

        if (!finish)
        {
            cfg.SetupComplete = true;
            await _ipc.RequestAsync(new IpcEnvelope { Kind = IpcKind.UpdateConfig, Config = cfg });
        }

        return Ok(new { setupComplete = true, displayName = name, receiveEnabled = receive, testSent = runTest });
    }

    private async Task<int> Receive(List<string> args)
    {
        if (args.Count == 0) return Fail("Usage: lanmsg receive on|off|toggle|status");
        if (!EnsureService()) return 1;

        if (args[0] == "status")
        {
            var cfg = await _ipc.RequestAsync(new IpcEnvelope { Kind = IpcKind.GetConfig });
            return Ok(new { receiveEnabled = cfg?.Config?.ReceiveEnabled ?? false });
        }

        bool target;
        if (args[0] == "toggle")
        {
            var cur = await _ipc.RequestAsync(new IpcEnvelope { Kind = IpcKind.GetConfig });
            target = !(cur?.Config?.ReceiveEnabled ?? true);
        }
        else if (args[0] == "on") target = true;
        else if (args[0] == "off") target = false;
        else return Fail("Usage: lanmsg receive on|off|toggle|status");

        var resp = await _ipc.RequestAsync(new IpcEnvelope { Kind = IpcKind.ToggleReceive, BoolValue = target });
        if (resp?.Config == null) return Fail("Toggle failed.");
        return Ok(new { receiveEnabled = resp.Config.ReceiveEnabled });
    }

    private async Task<int> Devices()
    {
        if (!EnsureService()) return 1;
        var resp = await _ipc.RequestAsync(new IpcEnvelope { Kind = IpcKind.GetDevices });
        var list = resp?.Devices ?? new List<DeviceInfo>();
        if (_json) return Ok(list);
        if (list.Count == 0) { Console.WriteLine("No devices discovered."); return 0; }
        Console.WriteLine($"{"DeviceId",-36} {"Label"}");
        foreach (var d in list) Console.WriteLine($"{d.DeviceId,-36} {d.Label}");
        return 0;
    }

    private async Task<int> Send(List<string> args, bool test)
    {
        if (!EnsureService()) return 1;

        var body = Opt(args, "--body") ?? Opt(args, "-m");
        if (test)
            body = string.IsNullOrWhiteSpace(body) ? "LanMsg test message — setup successful!" : body;
        else if (string.IsNullOrWhiteSpace(body))
            return Fail("Usage: lanmsg send --body \"message\" [--device id] [--host ip] [--broadcast] [--priority normal|important|urgent]");

        var req = new SendRequest
        {
            Body = body!,
            Priority = ParsePriority(Opt(args, "--priority")),
            Broadcast = HasFlag(args, "--broadcast"),
            IsTest = test,
            TargetDeviceIds = OptMulti(args, "--device", "-d"),
            TargetHosts = OptMulti(args, "--host", "-H")
        };

        if (!test && !req.Broadcast && req.TargetDeviceIds.Count == 0 && req.TargetHosts.Count == 0)
            return Fail("Select --device, --host, or --broadcast.");

        var kind = test ? IpcKind.TestMessage : IpcKind.SendRequest;
        var resp = await _ipc.RequestAsync(new IpcEnvelope { Kind = kind, Send = req });
        if (resp?.Kind == IpcKind.Error) return Fail(resp.Text ?? "Send failed.");

        if (!test)
        {
            _history.Add(new LanMessage
            {
                SenderName = Environment.UserName,
                Hostname = Environment.MachineName,
                Body = body!,
                Priority = req.Priority
            }, "out");
        }

        if (_json) return Ok(resp?.Results ?? new List<DeliveryResult>());
        foreach (var r in resp?.Results ?? new List<DeliveryResult>())
            Console.WriteLine($"{r.Target}: {r.Status} — {r.Detail}");
        return 0;
    }

    private async Task<int> Reply(List<string> args)
    {
        var to = Opt(args, "--to") ?? Opt(args, "-t");
        var body = Opt(args, "--body") ?? Opt(args, "-m");
        if (string.IsNullOrWhiteSpace(to) || string.IsNullOrWhiteSpace(body))
            return Fail("Usage: lanmsg reply --to <host-or-ip> --body \"message\"");

        if (!EnsureService()) return 1;

        var resp = await _ipc.RequestAsync(new IpcEnvelope
        {
            Kind = IpcKind.SendRequest,
            Send = new SendRequest
            {
                TargetHosts = new List<string> { to },
                Body = body,
                Priority = MessagePriority.Normal
            }
        });

        if (resp?.Kind == IpcKind.Error) return Fail(resp.Text ?? "Reply failed.");

        _history.Add(new LanMessage
        {
            SenderName = "You",
            Hostname = Environment.MachineName,
            Body = body,
            Priority = MessagePriority.Normal
        }, "out");

        if (_json) return Ok(resp?.Results);
        foreach (var r in resp?.Results ?? new List<DeliveryResult>())
            Console.WriteLine($"{r.Target}: {r.Status} — {r.Detail}");
        return 0;
    }

    private async Task<int> Queue(List<string> args)
    {
        if (args.Count == 0 || args[0] != "replay") return Fail("Usage: lanmsg queue replay");
        if (!EnsureService()) return 1;
        var resp = await _ipc.RequestAsync(new IpcEnvelope { Kind = IpcKind.QueueReplay });
        if (resp?.Kind == IpcKind.Error) return Fail(resp.Text ?? "Replay failed.");
        return Ok(new { message = resp.Text ?? "done" });
    }

    private async Task<int> Watch()
    {
        if (!EnsureService()) return 1;

        Console.Error.WriteLine("Watching for messages (Ctrl+C to stop)...");
        var tcs = new TaskCompletionSource<int>();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; tcs.TrySetResult(0); };

        _ipc.PushReceived += env =>
        {
            if (env.Kind != IpcKind.InboundMessage || env.Message == null) return;
            var msg = env.Message;
            _history.Add(msg, "in");
            if (_json)
            {
                Console.WriteLine(JsonSerializer.Serialize(msg, JsonOpts));
            }
            else
            {
                Console.WriteLine($"[{msg.Timestamp.ToLocalTime():HH:mm:ss}] {msg.SenderName} ({msg.Hostname}) [{msg.Priority}]");
                Console.WriteLine(msg.Body);
                Console.WriteLine();
            }
        };

        _ipc.StartListener();
        await _ipc.RequestAsync(new IpcEnvelope { Kind = IpcKind.QueueReplay });
        return await tcs.Task;
    }

    private int History(List<string> args)
    {
        if (args.Count == 0) return Fail("Usage: lanmsg history list|clear");
        if (args[0] == "clear")
        {
            if (!HasFlag(args, "--yes") && !HasFlag(args, "-y"))
                return Fail("Use --yes to confirm: lanmsg history clear --yes");
            _history.Clear();
            return Ok(new { cleared = true });
        }
        if (args[0] == "list")
        {
            var limit = 200;
            if (Opt(args, "--limit") is { } lim && int.TryParse(lim, out var n)) limit = n;
            var rows = _history.GetAll(limit);
            if (_json) return Ok(rows);
            if (rows.Count == 0) { Console.WriteLine("No history."); return 0; }
            Console.WriteLine($"{"Time",-20} {"Dir",-4} {"From",-24} Message");
            foreach (var h in rows)
            {
                var from = h.Direction == "out" ? "You" : h.SenderName;
                var line = h.Body.Replace('\n', ' ');
                if (line.Length > 60) line = line[..57] + "...";
                Console.WriteLine($"{h.Timestamp.ToLocalTime():yyyy-MM-dd HH:mm,-20} {h.Direction,-4} {from,-24} {line}");
            }
            return 0;
        }
        return Fail("Usage: lanmsg history list|clear");
    }

    private int Tray(List<string> args)
    {
        var tray = FindTrayExe();
        if (tray == null) return Fail("LanMsg.Tray.exe not found. Install LanMsg first.");

        var trayArgs = "";
        if (args.Contains("--sender")) trayArgs = "--sender";
        else if (args.Contains("--settings")) trayArgs = "--settings";

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = tray,
            Arguments = trayArgs,
            UseShellExecute = true
        });
        return Ok(new { launched = tray, args = trayArgs });
    }

    private static string? FindTrayExe()
    {
        var dir = AppContext.BaseDirectory;
        var local = Path.Combine(dir, "LanMsg.Tray.exe");
        if (File.Exists(local)) return local;
        var pf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LanMsg", "LanMsg.Tray.exe");
        if (File.Exists(pf)) return pf;
        return null;
    }

    private bool EnsureService()
    {
        if (ServiceHelper.EnsureRunning()) return true;
        Fail("LanMsg service is not running. Run: lanmsg service start");
        return false;
    }

    private int Ok(object? data)
    {
        if (_json && data != null) Console.WriteLine(JsonSerializer.Serialize(data, JsonOpts));
        else if (data is not null && !_json && data.GetType().GetProperty("message") != null)
            Console.WriteLine(data.GetType().GetProperty("message")!.GetValue(data));
        return 0;
    }

    private int Fail(string msg)
    {
        if (_json) Console.WriteLine(JsonSerializer.Serialize(new { error = msg }, JsonOpts));
        else Console.Error.WriteLine($"Error: {msg}");
        return 1;
    }

    private static string? Opt(List<string> args, params string[] keys)
    {
        for (var i = 0; i < args.Count - 1; i++)
            if (keys.Contains(args[i], StringComparer.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    private static List<string> OptMulti(List<string> args, params string[] keys)
    {
        var list = new List<string>();
        for (var i = 0; i < args.Count - 1; i++)
            if (keys.Contains(args[i], StringComparer.OrdinalIgnoreCase))
                list.Add(args[i + 1]);
        return list;
    }

    private static List<string>? OptList(List<string> args, string key)
    {
        var v = Opt(args, key);
        if (v == null) return null;
        return v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static bool? ParseBool(List<string> args, string key)
    {
        var v = Opt(args, key);
        if (v == null) return null;
        return v.ToLowerInvariant() switch
        {
            "1" or "true" or "on" or "yes" => true,
            "0" or "false" or "off" or "no" => false,
            _ => null
        };
    }

    private static bool HasFlag(List<string> args, string flag) =>
        args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

    private static MessagePriority ParsePriority(string? p) => p?.ToLowerInvariant() switch
    {
        "important" => MessagePriority.Important,
        "urgent" => MessagePriority.Urgent,
        _ => MessagePriority.Normal
    };

    private static void PrintHelp()
    {
        Console.WriteLine(@"LanMsg CLI — LAN messaging for Windows (same features as the tray GUI)

Usage: lanmsg <command> [options] [--json]

Commands:
  ping                         Check service connectivity
  service status|start         Service status or start
  config get                   Show current config
  config set                   Update settings (see options below)
  group set <code>             Set LAN group code
  setup --code <code>          Run setup wizard from CLI
  receive on|off|toggle|status Toggle receiving
  devices                      List discovered peers
  send --body ""...""           Send message
  test [--body ""...""]         Send test popup to this PC
  reply --to <host> --body """" Reply to a sender
  queue replay                 Replay queued inbound messages
  watch                        Listen for incoming messages
  history list|clear           Local message history
  tray [--sender|--settings]   Open GUI (alias: gui)
  help                         Show this help

Send options:
  --device, -d <id>            Target device (repeatable)
  --host, -H <ip|hostname>     Manual target (repeatable)
  --broadcast                  Send to all discovered devices
  --priority normal|important|urgent

Config set options:
  --code, --name, --receive on|off, --sounds on|off
  --allowlist on|off, --debug on|off, --setup-complete on|off
  --block-devices id,id  --block-ips ip,ip  --allowed-devices id,id

Setup options:
  --code (required), --name, --receive, --allowlist, --skip-test, --finish

Global: --json   Machine-readable output");
    }

    public void Dispose()
    {
        _ipc.Dispose();
        _history.Dispose();
    }
}
