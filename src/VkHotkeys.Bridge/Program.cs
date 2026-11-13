using System.IO.Pipes;
using System.Text.Json;
using VkHotkeys.Core.Ipc;
using VkHotkeys.Core.Protocol;

namespace VkHotkeys.Bridge;

/// <summary>
/// Хост Native Messaging. Браузер запускает его сам (chrome.runtime.connectNative) и общается через stdin/stdout.
/// Мост пересылает кадры между браузером и Windows-приложением (именованный канал) без изменений.
///
/// * Если приложение не запущено — мост ждёт и переподключается каждые 2 с, сообщая расширению appStatus=false.
/// * Последние hello/state кэшируются и отправляются приложению сразу после (пере)подключения.
/// * Закрытие stdin (браузер закрыт / расширение отключено) — завершение процесса.
/// </summary>
internal static class Program
{
    private static readonly SemaphoreSlim StdoutLock = new(1, 1);
    private static readonly SemaphoreSlim PipeLock = new(1, 1);
    private static readonly object CacheGate = new();
    private static byte[]? _lastHello;
    private static byte[]? _lastState;
    private static NamedPipeClientStream? _pipe;

    private static async Task<int> Main(string[] args)
    {
        // Chrome передаёт первым аргументом origin расширения, Edge/Chrome на Windows — ещё и --parent-window.
        Log($"started, args: {string.Join(' ', args)}");

        var stdin = Console.OpenStandardInput();
        var stdout = Console.OpenStandardOutput();
        using var shutdown = new CancellationTokenSource();

        await SendAppStatusAsync(stdout, false).ConfigureAwait(false);

        var pipeTask = Task.Run(() => PipeLoopAsync(stdout, shutdown.Token));
        try
        {
            await StdinLoopAsync(stdin).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log($"stdin error: {ex.Message}");
        }

        shutdown.Cancel();
        try { _pipe?.Dispose(); } catch { /* игнорируем */ }
        try { await pipeTask.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); } catch { /* игнорируем */ }
        Log("stdin closed, exiting");
        return 0;
    }

    /// <summary>Браузер -> приложение.</summary>
    private static async Task StdinLoopAsync(Stream stdin)
    {
        while (true)
        {
            var frame = await FrameCodec.ReadFrameAsync(stdin).ConfigureAwait(false);
            if (frame is null) return;

            var type = PeekType(frame);
            lock (CacheGate)
            {
                if (type == MessageTypes.Hello) _lastHello = frame;
                else if (type == MessageTypes.State) _lastState = frame;
            }
            await TryWritePipeAsync(frame).ConfigureAwait(false);
        }
    }

    /// <summary>Подключение к приложению и пересылка приложение -> браузер.</summary>
    private static async Task PipeLoopAsync(Stream stdout, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var pipe = new NamedPipeClientStream(".", PipeNames.Bridge, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try
            {
                await pipe.ConnectAsync(2000, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
            {
                pipe.Dispose();
                try { await Task.Delay(2000, ct).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
                continue;
            }
            catch (OperationCanceledException)
            {
                pipe.Dispose();
                return;
            }

            Log("connected to app");
            await PipeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                _pipe = pipe;
                byte[]? hello, state;
                lock (CacheGate) { hello = _lastHello; state = _lastState; }
                if (hello is not null) await FrameCodec.WriteFrameAsync(pipe, hello, ct).ConfigureAwait(false);
                if (state is not null) await FrameCodec.WriteFrameAsync(pipe, state, ct).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // приложение сразу закрыло канал — обработаем ниже
            }
            finally
            {
                PipeLock.Release();
            }

            await SendAppStatusAsync(stdout, true).ConfigureAwait(false);

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var frame = await FrameCodec.ReadFrameAsync(pipe, ct).ConfigureAwait(false);
                    if (frame is null) break;
                    await WriteStdoutAsync(stdout, frame).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or InvalidDataException)
            {
            }

            await PipeLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            _pipe = null;
            PipeLock.Release();
            pipe.Dispose();
            Log("disconnected from app");
            if (!ct.IsCancellationRequested) await SendAppStatusAsync(stdout, false).ConfigureAwait(false);
        }
    }

    private static async Task TryWritePipeAsync(byte[] frame)
    {
        await PipeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_pipe is { IsConnected: true } pipe)
                await FrameCodec.WriteFrameAsync(pipe, frame).ConfigureAwait(false);
            // Приложение не запущено: команды не нужны, state/hello уйдут из кэша при подключении.
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
        }
        finally
        {
            PipeLock.Release();
        }
    }

    private static async Task WriteStdoutAsync(Stream stdout, byte[] frame)
    {
        await StdoutLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await FrameCodec.WriteFrameAsync(stdout, frame).ConfigureAwait(false);
        }
        catch (IOException)
        {
            Environment.Exit(0); // браузер ушёл
        }
        finally
        {
            StdoutLock.Release();
        }
    }

    private static Task SendAppStatusAsync(Stream stdout, bool connected) =>
        WriteStdoutAsync(stdout, ProtocolJson.Serialize(new { type = MessageTypes.AppStatus, connected }));

    private static string? PeekType(byte[] frame)
    {
        try
        {
            using var doc = JsonDocument.Parse(frame);
            return doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>stdout занят протоколом, поэтому диагностика — только в stderr (виден в логах браузера).</summary>
    private static void Log(string message)
    {
        try { Console.Error.WriteLine($"[vkhotkeys-bridge] {message}"); } catch { /* игнорируем */ }
    }
}
