using System.Diagnostics;
using System.Text.Json;
using VkHotkeys.Core.Ipc;
using VkHotkeys.Core.Player;
using VkHotkeys.Core.Protocol;

namespace VkHotkeys.Core.Tests;

/// <summary>
/// Сквозной тест: настоящий процесс моста VkHotkeys.Bridge, «браузер» на его stdin/stdout
/// и сервер канала приложения. Проверяет весь путь команды F10 -> расширение -> подтверждение.
/// </summary>
public sealed class BridgeIntegrationTests : IDisposable
{
    private readonly string _pipe = "vkhk-test-" + Guid.NewGuid().ToString("N")[..12];
    private readonly List<IDisposable> _cleanup = new();

    private Process StartBridge()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "VkHotkeys.Bridge.dll");
        var psi = new ProcessStartInfo("dotnet", $"\"{dll}\" chrome-extension://{PipeNames.ExtensionId}/")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment[PipeNames.OverrideVariable] = _pipe;
        var p = Process.Start(psi)!;
        p.ErrorDataReceived += (_, _) => { };
        p.BeginErrorReadLine();
        _cleanup.Add(new ActionDisposable(() => { try { if (!p.HasExited) p.Kill(); } catch { } p.Dispose(); }));
        return p;
    }

    private static async Task<JsonElement> ReadJsonAsync(Stream s, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var frame = await FrameCodec.ReadFrameAsync(s, cts.Token) ?? throw new EndOfStreamException();
        return JsonDocument.Parse(frame).RootElement.Clone();
    }

    private static Task WriteJsonAsync(Stream s, object value) => FrameCodec.WriteFrameAsync(s, ProtocolJson.Serialize(value));

    private static async Task<JsonElement> ReadUntilAsync(Stream s, Func<JsonElement, bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var left = deadline - DateTime.UtcNow;
            if (left <= TimeSpan.Zero) throw new TimeoutException();
            var msg = await ReadJsonAsync(s, left);
            if (predicate(msg)) return msg;
        }
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
            await Task.Delay(25);
        }
    }

    [Fact]
    public async Task FullRoundTripThroughBridge()
    {
        var controller = new PlayerController(TimeSpan.FromSeconds(3));
        var server = new PipeServer(_pipe);
        _cleanup.Add(server);
        server.ClientConnected += controller.Attach;
        server.Start();

        var bridge = StartBridge();
        var toBridge = bridge.StandardInput.BaseStream;
        var fromBridge = bridge.StandardOutput.BaseStream;

        // 1. Мост сразу сообщает расширению статус приложения.
        var first = await ReadJsonAsync(fromBridge, TimeSpan.FromSeconds(10));
        Assert.Equal("appStatus", first.GetProperty("type").GetString());

        // 2. «Расширение» представляется и отправляет состояние плеера.
        await WriteJsonAsync(toBridge, new { type = "hello", browser = "Chrome", version = "1.0.0" });
        await WriteJsonAsync(toBridge, new
        {
            type = "state",
            state = new { vkTabs = 1, playerFound = true, hasTrack = true, playing = true, title = "Трек", artist = "Исполнитель", volume = 0.5 },
        });

        await ReadUntilAsync(fromBridge, m => m.GetProperty("type").GetString() == "appStatus" && m.GetProperty("connected").GetBoolean(),
            TimeSpan.FromSeconds(10));
        await WaitForAsync(() => controller.Status.Status == LinkStatus.Ready, TimeSpan.FromSeconds(5));
        Assert.Equal("Chrome", controller.Status.BrowserName);
        Assert.Equal("Трек", controller.Status.Player?.Title);

        // 3. Приложение отправляет «следующий трек», расширение подтверждает.
        var nextTask = controller.NextAsync();
        var cmd = await ReadUntilAsync(fromBridge, m => m.GetProperty("type").GetString() == "command", TimeSpan.FromSeconds(5));
        Assert.Equal("next", cmd.GetProperty("action").GetString());
        await WriteJsonAsync(toBridge, new { type = "result", id = cmd.GetProperty("id").GetString(), ok = true });
        var outcome = await nextTask;
        Assert.True(outcome.Success, outcome.Message);

        // 4. Громкость: значение доходит до расширения как 0..1.
        var volTask = controller.ChangeVolumeAsync(+10);
        cmd = await ReadUntilAsync(fromBridge, m => m.GetProperty("type").GetString() == "command", TimeSpan.FromSeconds(5));
        Assert.Equal("setVolume", cmd.GetProperty("action").GetString());
        Assert.Equal(0.6, cmd.GetProperty("value").GetDouble(), 3);
        await WriteJsonAsync(toBridge, new { type = "result", id = cmd.GetProperty("id").GetString(), ok = true });
        Assert.True((await volTask).Success);

        // 5. Браузер закрыт (stdin закрыт) — мост завершается, приложение видит отключение.
        toBridge.Close();
        Assert.True(bridge.WaitForExit(5000), "мост не завершился после закрытия stdin");
        await WaitForAsync(() => controller.Status.Status == LinkStatus.WaitingForBrowser, TimeSpan.FromSeconds(5));
        Assert.False((await controller.NextAsync()).Success);
    }

    [Fact]
    public async Task BridgeWaitsForAppAndReplaysCachedState()
    {
        var bridge = StartBridge();
        var toBridge = bridge.StandardInput.BaseStream;
        var fromBridge = bridge.StandardOutput.BaseStream;

        var first = await ReadJsonAsync(fromBridge, TimeSpan.FromSeconds(10));
        Assert.False(first.GetProperty("connected").GetBoolean());

        // Приложение ещё не запущено: состояние кэшируется мостом.
        await WriteJsonAsync(toBridge, new { type = "hello", browser = "Edge" });
        await WriteJsonAsync(toBridge, new { type = "state", state = new { vkTabs = 1, playerFound = true, hasTrack = true, title = "X" } });

        // Приложение стартует позже.
        await Task.Delay(500);
        var controller = new PlayerController();
        var server = new PipeServer(_pipe);
        _cleanup.Add(server);
        server.ClientConnected += controller.Attach;
        server.Start();

        await WaitForAsync(() => controller.Status.Status == LinkStatus.Ready, TimeSpan.FromSeconds(10));
        Assert.Equal("Edge", controller.Status.BrowserName);
        Assert.Equal("X", controller.Status.Player?.Title);
    }

    public void Dispose()
    {
        foreach (var d in Enumerable.Reverse(_cleanup)) d.Dispose();
    }

    private sealed class ActionDisposable(Action a) : IDisposable
    {
        public void Dispose() => a();
    }
}

public class FrameCodecTests
{
    [Fact]
    public async Task RoundTripsFrames()
    {
        var ms = new MemoryStream();
        await FrameCodec.WriteFrameAsync(ms, "{\"a\":1}"u8.ToArray());
        await FrameCodec.WriteFrameAsync(ms, Array.Empty<byte>());
        ms.Position = 0;
        Assert.Equal("{\"a\":1}"u8.ToArray(), await FrameCodec.ReadFrameAsync(ms));
        Assert.Empty((await FrameCodec.ReadFrameAsync(ms))!);
        Assert.Null(await FrameCodec.ReadFrameAsync(ms));
    }

    [Fact]
    public async Task RejectsOversizedFrame()
    {
        var ms = new MemoryStream(new byte[] { 0xFF, 0xFF, 0xFF, 0x7F });
        await Assert.ThrowsAsync<InvalidDataException>(() => FrameCodec.ReadFrameAsync(ms));
    }

    [Fact]
    public async Task TruncatedFrameThrows()
    {
        var ms = new MemoryStream(new byte[] { 10, 0, 0, 0, 1, 2 });
        await Assert.ThrowsAsync<EndOfStreamException>(() => FrameCodec.ReadFrameAsync(ms));
    }

    [Fact]
    public void GarbageJsonIsIgnored() => Assert.Null(ProtocolJson.DeserializeIncoming("not json"u8));
}
