using VkHotkeys.Core.Ipc;
using VkHotkeys.Core.Player;
using VkHotkeys.Core.Protocol;

namespace VkHotkeys.Core.Tests;

/// <summary>Имитация браузера: отвечает на команды как расширение.</summary>
internal sealed class FakeLink : IBrowserLink
{
    private readonly PlayerController _controller;
    public Guid Id { get; } = Guid.NewGuid();
    public string BrowserName { get; }
    public List<OutgoingCommand> Sent { get; } = new();
    public Func<OutgoingCommand, IncomingMessage?>? Responder { get; set; }

    public FakeLink(PlayerController controller, string name = "Chrome")
    {
        _controller = controller;
        BrowserName = name;
    }

    public Task SendAsync(OutgoingCommand command, CancellationToken ct = default)
    {
        lock (Sent) Sent.Add(command);
        var reply = Responder?.Invoke(command);
        if (reply is not null)
        {
            reply.Type = MessageTypes.Result;
            reply.Id = command.Id;
            _ = Task.Run(() => _controller.OnMessage(Id, reply));
        }
        return Task.CompletedTask;
    }

    public void PushState(PlayerSnapshot s) => _controller.OnMessage(Id, new IncomingMessage { Type = MessageTypes.State, State = s });
}

public class PlayerControllerTests
{
    private static PlayerSnapshot Ready(double volume = 0.5, bool playing = true, string title = "Song") => new()
    {
        VkTabs = 1, PlayerFound = true, HasTrack = true, Playing = playing, Title = title, Artist = "Artist", Volume = volume,
    };

    private static (PlayerController, FakeLink) Setup(PlayerSnapshot? state = null)
    {
        var c = new PlayerController(TimeSpan.FromMilliseconds(300));
        var link = new FakeLink(c);
        c.AddLink(link);
        if (state is not null) link.PushState(state);
        return (c, link);
    }

    private static IncomingMessage Ok(PlayerSnapshot? s = null) => new() { Ok = true, State = s };

    [Fact]
    public async Task NoBrowserFailsImmediatelyWithoutFalseSuccess()
    {
        var c = new PlayerController();
        var r = await c.NextAsync();
        Assert.False(r.Success);
        Assert.Equal(ErrorCodes.Describe(ErrorCodes.NoBrowser), r.Message);
        Assert.Equal(LinkStatus.WaitingForBrowser, c.Status.Status);
    }

    [Fact]
    public async Task NoVkTabReportedHonestly()
    {
        var (c, link) = Setup(new PlayerSnapshot { VkTabs = 0 });
        var r = await c.TogglePlayAsync();
        Assert.False(r.Success);
        Assert.Equal(ErrorCodes.Describe(ErrorCodes.NoVkTab), r.Message);
        Assert.Empty(link.Sent);
        Assert.Equal(LinkStatus.NoVkTab, c.Status.Status);
    }

    [Fact]
    public async Task UnansweredCommandTimesOut()
    {
        var (c, _) = Setup(Ready());
        var r = await c.NextAsync();
        Assert.False(r.Success);
        Assert.Equal(ErrorCodes.Describe(ErrorCodes.Timeout), r.Message);
    }

    [Fact]
    public async Task ErrorFromExtensionIsPropagated()
    {
        var (c, link) = Setup(Ready());
        link.Responder = _ => new IncomingMessage { Ok = false, Error = ErrorCodes.PlayerNotFound };
        var r = await c.PreviousAsync();
        Assert.False(r.Success);
        Assert.Contains("не найден", r.Message);
    }

    [Fact]
    public async Task NextSucceedsWhenConfirmed()
    {
        var (c, link) = Setup(Ready());
        link.Responder = _ => Ok();
        var r = await c.NextAsync();
        Assert.True(r.Success);
        Assert.Equal(PlayerActions.Next, Assert.Single(link.Sent).Action);
    }

    [Fact]
    public async Task ToggleReportsResultingState()
    {
        var (c, link) = Setup(Ready(playing: true));
        link.Responder = _ => Ok(Ready(playing: false));
        var r = await c.TogglePlayAsync();
        Assert.True(r.Success);
        Assert.Equal("Пауза", r.Message);
    }

    [Fact]
    public async Task VolumeStepsFromCurrentValue()
    {
        var (c, link) = Setup(Ready(volume: 0.5));
        link.Responder = _ => Ok();
        c.VolumeStepPercent = 5;
        await c.ChangeVolumeAsync(+5);
        await c.ChangeVolumeAsync(+5); // быстрое повторное нажатие: состояние браузера ещё не обновилось
        Assert.Equal(new double?[] { 0.55, 0.60 }, link.Sent.Select(s => s.Value).ToArray());
    }

    [Fact]
    public async Task VolumeIsClamped()
    {
        var (c, link) = Setup(Ready(volume: 0.98));
        link.Responder = _ => Ok();
        await c.ChangeVolumeAsync(+5);
        Assert.Equal(1.0, link.Sent.Single().Value);
    }

    [Fact]
    public async Task MuteRemembersAndRestoresVolume()
    {
        var (c, link) = Setup(Ready(volume: 0.4));
        link.Responder = cmd => Ok(Ready(volume: cmd.Value ?? 0.4));

        var mute = await c.ToggleMuteAsync();
        Assert.True(mute.Success);
        Assert.True(c.Status.IsMuted);
        Assert.Equal(0.0, link.Sent[^1].Value);

        var unmute = await c.ToggleMuteAsync();
        Assert.True(unmute.Success);
        Assert.False(c.Status.IsMuted);
        Assert.Equal(0.4, link.Sent[^1].Value);
    }

    [Fact]
    public async Task VolumeUpWhileMutedRestores()
    {
        var (c, link) = Setup(Ready(volume: 0.3));
        link.Responder = cmd => Ok(Ready(volume: cmd.Value ?? 0.3));
        await c.ToggleMuteAsync();
        await Task.Delay(1600); // выходим из окна «быстрых повторов»
        await c.ChangeVolumeAsync(+5);
        Assert.Equal(0.3, link.Sent[^1].Value);
        Assert.False(c.Status.IsMuted);
    }

    [Fact]
    public async Task VolumeUnavailableIsReported()
    {
        var s = Ready();
        s.Volume = null;
        var (c, link) = Setup(s);
        link.Responder = _ => Ok();
        var r = await c.ChangeVolumeAsync(+5);
        Assert.False(r.Success);
        Assert.Empty(link.Sent);
    }

    [Fact]
    public async Task PlayingBrowserIsPreferred()
    {
        var c = new PlayerController(TimeSpan.FromMilliseconds(300));
        var idle = new FakeLink(c, "Edge") { Responder = _ => Ok() };
        var playing = new FakeLink(c, "Chrome") { Responder = _ => Ok() };
        c.AddLink(idle);
        c.AddLink(playing);
        idle.PushState(Ready(playing: false));
        playing.PushState(Ready(playing: true));
        idle.PushState(Ready(playing: false)); // более свежее обновление, но не играет

        await c.NextAsync();
        Assert.Single(playing.Sent);
        Assert.Empty(idle.Sent);
        Assert.Equal("Chrome", c.Status.BrowserName);
    }

    [Fact]
    public void TrackChangeEventFires()
    {
        var (c, link) = Setup(Ready(title: "A"));
        var changes = new List<string>();
        c.TrackChanged += (t, _) => changes.Add(t);
        link.PushState(Ready(title: "A"));
        link.PushState(Ready(title: "B"));
        link.PushState(Ready(title: "B"));
        link.PushState(Ready(title: "C"));
        Assert.Equal(new[] { "B", "C" }, changes);
    }

    [Fact]
    public void DisconnectResetsStatus()
    {
        var (c, link) = Setup(Ready());
        Assert.Equal(LinkStatus.Ready, c.Status.Status);
        c.RemoveLink(link.Id);
        Assert.Equal(LinkStatus.WaitingForBrowser, c.Status.Status);
    }

    [Fact]
    public async Task SeekSendsDelta()
    {
        var (c, link) = Setup(Ready());
        link.Responder = _ => Ok();
        c.SeekStepSeconds = 15;
        var r = await c.ExecuteAsync(Hotkeys.HotkeyAction.SeekBackward);
        Assert.True(r.Success);
        Assert.Equal(PlayerActions.Seek, link.Sent.Single().Action);
        Assert.Equal(-15, link.Sent.Single().Value);
    }
}
