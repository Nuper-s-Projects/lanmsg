using System.IO;
using System.IO.Pipes;
using LanMsg.Core.Logging;
using LanMsg.Ipc;

namespace LanMsg.Tray.Services;

public sealed class IpcClient : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _pushListener;
    public event Action<IpcEnvelope>? PushReceived;

    public void StartListener()
    {
        if (_pushListener != null) return;
        _cts = new CancellationTokenSource();
        _pushListener = Task.Run(() => PushLoop(_cts.Token));
    }

    private async Task PushLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeClientStream(".", PipeNames.PushPipe, PipeDirection.In, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(5000, ct);
                using var reader = new StreamReader(pipe);
                while (!ct.IsCancellationRequested && pipe.IsConnected)
                {
                    var line = await reader.ReadLineAsync();
                    if (line == null) break;
                    var data = Convert.FromBase64String(line);
                    var env = IpcJson.Deserialize(data);
                    if (env != null)
                        PushReceived?.Invoke(env);
                }
            }
            catch { await Task.Delay(2000, ct); }
        }
    }

    public async Task<IpcEnvelope?> RequestAsync(IpcEnvelope req, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            for (var i = 0; i < 3; i++)
            {
                try
                {
                    return await DoRequestAsync(req, ct);
                }
                catch (Exception ex) when (i < 2)
                {
                    AppLogger.Error("IpcClient", $"Request attempt {i + 1} failed", ex);
                    ServiceHelper.EnsureRunning();
                    await Task.Delay(800, ct);
                }
            }

            ServiceHelper.EnsureRunning();
            try
            {
                return await DoRequestAsync(req, ct);
            }
            catch (Exception ex)
            {
                AppLogger.Error("IpcClient", "Request failed", ex);
                return new IpcEnvelope
                {
                    Kind = IpcKind.Error,
                    Text = "LanMsg service is not running. It was started automatically but could not connect. Try restarting LanMsg from the Start Menu."
                };
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private static async Task<IpcEnvelope?> DoRequestAsync(IpcEnvelope req, CancellationToken ct)
    {
        await using var pipe = new NamedPipeClientStream(".", PipeNames.RequestPipe, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000, ct);
        using var reader = new StreamReader(pipe, leaveOpen: true);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(Convert.ToBase64String(IpcJson.Serialize(req)));
        var line = await reader.ReadLineAsync();
        if (line == null) return null;
        return IpcJson.Deserialize(Convert.FromBase64String(line));
    }

    public void Dispose() => _cts?.Cancel();
}
