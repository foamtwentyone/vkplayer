using System.Text.Json;
using VkHotkeys.Core.Hotkeys;
using VkHotkeys.Core.Ipc;
using VkHotkeys.Core.Player;

// Сторона «Windows-приложения» для E2E-теста: сервер канала + контроллер плеера.
// Читает команды со stdin (по одной в строке: имя HotkeyAction, "status" или "wait-ready"),
// пишет JSON-ответы в stdout. Именно этот код (PlayerController) вызывается горячими клавишами в приложении.

var controller = new PlayerController(TimeSpan.FromSeconds(5));
using var server = new PipeServer();
server.ClientConnected += controller.Attach;
server.Start();

void Reply(object o) => Console.WriteLine(JsonSerializer.Serialize(o));

object Status()
{
    var s = controller.Status;
    return new
    {
        status = s.Status.ToString(), browser = s.BrowserName, title = s.Player?.Title, artist = s.Player?.Artist,
        playing = s.Player?.Playing, volume = s.VolumePercent, muted = s.IsMuted, position = s.Player?.Position,
        strategy = s.Player?.Strategy,
    };
}

Reply(new { ready = true });
string? line;
while ((line = Console.ReadLine()) is not null)
{
    line = line.Trim();
    if (line.Length == 0) continue;
    if (line == "status")
    {
        Reply(Status());
    }
    else if (line.StartsWith("wait-"))
    {
        var want = line[5..];
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (controller.Status.Status.ToString() != want && DateTime.UtcNow < deadline) await Task.Delay(50);
        Reply(Status());
    }
    else if (Enum.TryParse<HotkeyAction>(line, out var action))
    {
        var r = await controller.ExecuteAsync(action);
        await Task.Delay(150);
        Reply(new { action = line, ok = r.Success, message = r.Message, state = Status() });
    }
    else
    {
        Reply(new { error = "unknown command " + line });
    }
}
