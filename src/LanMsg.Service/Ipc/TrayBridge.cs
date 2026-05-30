using System.Collections.Concurrent;
using System.IO.Pipes;
using LanMsg.Core.Logging;
using LanMsg.Ipc;

namespace LanMsg.Service.Ipc;

public sealed class TrayBridge : IDisposable
{
    private readonly Func<IpcEnvelope, Task<IpcEnvelope?>> _handler;
    private CancellationTokenSource? _cts;
    private Task? _reqLoop;
    private Task? _pushLoop;
    private readonly ConcurrentBag<StreamWriter> _pushWriters = new();

    public TrayBridge(Func<IpcEnvelope, Task<IpcEnvelope?>> handler) => _handler = handler;

    public void Start()
    {
        if (_reqLoop != null) return;
        _cts = new CancellationTokenSource();
        _reqLoop = Task.Run(() => RequestLoop(_cts.Token));
        _pushLoop = Task.Run(() => PushAcceptLoop(_cts.Token));
    }

    public void Stop() => _cts?.Cancel();

    public async Task PushAsync(IpcEnvelope env, CancellationToken ct = default)
    {
        var data = Convert.ToBase64String(IpcJson.Serialize(env));
        foreach (var w in _pushWriters.ToArray())
        {
            try
            {
                await w.WriteLineAsync(data.AsMemory(), ct);
                await w.FlushAsync();
            }
            catch { }
        }
    }

    private async Task RequestLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    PipeNames.RequestPipe,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(ct);
                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

                var line = await reader.ReadLineAsync();
                if (line == null) continue;

                IpcEnvelope? response;
                try
                {
                    var data = Convert.FromBase64String(line);
                    var req = IpcJson.Deserialize(data);
                    response = req != null ? await _handler(req) : new IpcEnvelope { Kind = IpcKind.Error, Text = "Bad request" };
                }
                catch (Exception ex)
                {
                    response = new IpcEnvelope { Kind = IpcKind.Error, Text = ex.Message };
                }

                await writer.WriteLineAsync(Convert.ToBase64String(IpcJson.Serialize(response ?? new IpcEnvelope { Kind = IpcKind.Error })));
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                AppLogger.Error("TrayBridge", "Request pipe error", ex);
                await Task.Delay(500, ct);
            }
        }
    }

    private async Task PushAcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var pipe = new NamedPipeServerStream(
                    PipeNames.PushPipe,
                    PipeDirection.Out,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(ct);
                var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                _pushWriters.Add(writer);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        while (pipe.IsConnected && !ct.IsCancellationRequested)
                            await Task.Delay(1000, ct);
                    }
                    catch { }
                    finally
                    {
                        try { writer.Dispose(); pipe.Dispose(); } catch { }
                    }
                }, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                AppLogger.Error("TrayBridge", "Push pipe error", ex);
                await Task.Delay(500, ct);
            }
        }
    }

    public void Dispose() => Stop();
}
