using System.IO.Pipes;
using VkHotkeys.Core.Protocol;

namespace VkHotkeys.Core.Ipc;

/// <summary>Серверная сторона канала для одного экземпляра моста (= одного браузера).</summary>
public sealed class BrowserConnection : IBrowserLink, IDisposable
{
    private readonly Stream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private int _closed;

    public Guid Id { get; } = Guid.NewGuid();
    public string BrowserName { get; private set; } = "Браузер";

    public event Action<BrowserConnection, IncomingMessage>? MessageReceived;
    public event Action<BrowserConnection>? Closed;

    public BrowserConnection(Stream stream) => _stream = stream;

    public async Task RunAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var frame = await FrameCodec.ReadFrameAsync(_stream, _cts.Token).ConfigureAwait(false);
                if (frame is null) break;
                var msg = ProtocolJson.DeserializeIncoming(frame);
                if (msg is null) continue; // мусор пропускаем, соединение не рвём
                if (msg.Type == MessageTypes.Hello && !string.IsNullOrWhiteSpace(msg.Browser))
                    BrowserName = msg.Browser!;
                MessageReceived?.Invoke(this, msg);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or InvalidDataException)
        {
            // Разрыв канала: браузер закрыт или мост завершился.
        }
        finally
        {
            Close();
        }
    }

    public async Task SendAsync(OutgoingCommand command, CancellationToken ct = default)
    {
        var payload = ProtocolJson.Serialize(command);
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await FrameCodec.WriteFrameAsync(_stream, payload, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            Close();
            throw new IOException("Соединение с браузером потеряно", ex);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        _cts.Cancel();
        try { _stream.Dispose(); } catch { /* игнорируем */ }
        Closed?.Invoke(this);
    }

    public void Dispose() => Close();
}

/// <summary>Сервер именованного канала. Каждый запущенный браузером мост — отдельное подключение.</summary>
public sealed class PipeServer : IDisposable
{
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public event Action<BrowserConnection>? ClientConnected;

    public PipeServer(string? pipeName = null) => _pipeName = pipeName ?? PipeNames.Bridge;

    public void Start() => _loop ??= Task.Run(AcceptLoopAsync);

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                // CurrentUserOnly: подключиться может только процесс того же пользователя.
                server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            }
            catch (IOException)
            {
                await Task.Delay(1000).ConfigureAwait(false);
                continue;
            }

            try
            {
                await server.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                server.Dispose();
                break;
            }
            catch (IOException)
            {
                server.Dispose();
                await Task.Delay(200).ConfigureAwait(false);
                continue;
            }

            var connection = new BrowserConnection(server);
            ClientConnected?.Invoke(connection);
            _ = Task.Run(connection.RunAsync);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(1000); } catch { /* игнорируем */ }
    }
}
