using System.Collections.Concurrent;
using VkHotkeys.Core.Hotkeys;
using VkHotkeys.Core.Ipc;
using VkHotkeys.Core.Protocol;

namespace VkHotkeys.Core.Player;

/// <summary>
/// Логика управления плеером VK: выбор активного браузера/вкладки, отправка команд с ожиданием
/// подтверждения, громкость с шагом, Mute с восстановлением предыдущего уровня.
/// Успех сообщается только после положительного ответа расширения.
/// </summary>
public sealed class PlayerController
{
    private sealed class LinkEntry(IBrowserLink link)
    {
        public IBrowserLink Link { get; } = link;
        public PlayerSnapshot? State { get; set; }
        public long UpdatedAt { get; set; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<Guid, LinkEntry> _links = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IncomingMessage>> _pending = new();
    private readonly TimeSpan _timeout;
    private long _nextId;
    private long _updateSeq;

    private bool _muted;
    private int _volumeBeforeMute = 50;
    private int? _requestedVolume;
    private long _requestedVolumeAt;

    private bool _trackPrimed;
    private string? _lastTrackKey;
    private PlayerStatus _status = PlayerStatus.Initial;

    public PlayerController(TimeSpan? commandTimeout = null) => _timeout = commandTimeout ?? TimeSpan.FromSeconds(4);

    public int VolumeStepPercent { get; set; } = 5;
    public int SeekStepSeconds { get; set; } = 10;

    public PlayerStatus Status { get { lock (_gate) return _status; } }

    /// <summary>Вызывается в фоновом потоке при любом изменении статуса/состояния плеера.</summary>
    public event Action<PlayerStatus>? StatusChanged;

    /// <summary>Смена трека (название, исполнитель).</summary>
    public event Action<string, string?>? TrackChanged;

    // ---------------------------------------------------------------- подключения

    public void Attach(BrowserConnection connection)
    {
        AddLink(connection);
        connection.MessageReceived += (c, m) => OnMessage(c.Id, m);
        connection.Closed += c => RemoveLink(c.Id);
    }

    public void AddLink(IBrowserLink link)
    {
        lock (_gate) _links[link.Id] = new LinkEntry(link);
        Publish();
    }

    public void RemoveLink(Guid id)
    {
        lock (_gate) _links.Remove(id);
        Publish();
    }

    public void OnMessage(Guid linkId, IncomingMessage message)
    {
        switch (message.Type)
        {
            case MessageTypes.State:
                if (message.State is not null) ApplyState(linkId, message.State);
                break;
            case MessageTypes.Result:
                if (message.State is not null) ApplyState(linkId, message.State);
                if (message.Id is not null && _pending.TryRemove(message.Id, out var tcs))
                    tcs.TrySetResult(message);
                break;
            case MessageTypes.Hello:
                Publish();
                break;
        }
    }

    private void ApplyState(Guid linkId, PlayerSnapshot state)
    {
        lock (_gate)
        {
            if (!_links.TryGetValue(linkId, out var entry)) return;
            entry.State = state;
            entry.UpdatedAt = Interlocked.Increment(ref _updateSeq);

            // Пользователь сам поднял громкость в VK — режим Mute больше не актуален.
            if (_muted && state.Volume is > 0.005 && SelectTargetLocked() == entry)
                _muted = false;
        }
        Publish();
    }

    // ---------------------------------------------------------------- команды

    public Task<CommandOutcome> ExecuteAsync(HotkeyAction action) => action switch
    {
        HotkeyAction.PlayPause => TogglePlayAsync(),
        HotkeyAction.NextTrack => NextAsync(),
        HotkeyAction.PreviousTrack => PreviousAsync(),
        HotkeyAction.VolumeUp => ChangeVolumeAsync(+VolumeStepPercent),
        HotkeyAction.VolumeDown => ChangeVolumeAsync(-VolumeStepPercent),
        HotkeyAction.ToggleMute => ToggleMuteAsync(),
        HotkeyAction.SeekForward => SeekAsync(+SeekStepSeconds),
        HotkeyAction.SeekBackward => SeekAsync(-SeekStepSeconds),
        _ => Task.FromResult(CommandOutcome.Fail(ErrorCodes.Internal)),
    };

    public async Task<CommandOutcome> NextAsync()
    {
        var r = await SendAsync(PlayerActions.Next).ConfigureAwait(false);
        return r.Ok ? CommandOutcome.Ok("Следующий трек") : CommandOutcome.Fail(r.Error);
    }

    public async Task<CommandOutcome> PreviousAsync()
    {
        var r = await SendAsync(PlayerActions.Previous).ConfigureAwait(false);
        return r.Ok ? CommandOutcome.Ok("Предыдущий трек") : CommandOutcome.Fail(r.Error);
    }

    public async Task<CommandOutcome> TogglePlayAsync()
    {
        var r = await SendAsync(PlayerActions.Toggle).ConfigureAwait(false);
        if (!r.Ok) return CommandOutcome.Fail(r.Error);
        var playing = r.State?.Playing ?? Status.Player?.Playing ?? false;
        return CommandOutcome.Ok(playing ? "Воспроизведение" : "Пауза");
    }

    public async Task<CommandOutcome> SeekAsync(int seconds)
    {
        var r = await SendAsync(PlayerActions.Seek, seconds).ConfigureAwait(false);
        if (!r.Ok) return CommandOutcome.Fail(r.Error);
        var sign = seconds >= 0 ? "+" : "−";
        return CommandOutcome.Ok($"Перемотка {sign}{Math.Abs(seconds)} с");
    }

    public async Task<CommandOutcome> ChangeVolumeAsync(int deltaPercent)
    {
        bool muted;
        lock (_gate) muted = _muted;
        if (muted)
        {
            if (deltaPercent > 0) return await ToggleMuteAsync().ConfigureAwait(false);
            return CommandOutcome.Ok("Звук выключен");
        }

        var current = CurrentVolumeForChange();
        if (current is null)
            return CommandOutcome.Fail(Status.Status == LinkStatus.Ready ? ErrorCodes.VolumeUnavailable : StatusError());
        return await SetVolumeAsync(Math.Clamp(current.Value + deltaPercent, 0, 100)).ConfigureAwait(false);
    }

    public async Task<CommandOutcome> SetVolumeAsync(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        lock (_gate)
        {
            _requestedVolume = percent;
            _requestedVolumeAt = Environment.TickCount64;
        }

        var r = await SendAsync(PlayerActions.SetVolume, percent / 100.0).ConfigureAwait(false);
        if (!r.Ok)
        {
            lock (_gate) _requestedVolume = null;
            return CommandOutcome.Fail(r.Error);
        }
        lock (_gate)
        {
            if (percent > 0) _muted = false;
        }
        return CommandOutcome.Ok($"Громкость {percent}%");
    }

    public async Task<CommandOutcome> ToggleMuteAsync()
    {
        bool muted;
        int restore;
        lock (_gate) { muted = _muted; restore = _volumeBeforeMute; }

        if (muted)
        {
            var r = await SetVolumeAsync(restore > 0 ? restore : 50).ConfigureAwait(false);
            return r.Success ? CommandOutcome.Ok($"Звук включён · {restore}%") : r;
        }

        var current = Status.VolumePercent;
        if (current is null)
            return CommandOutcome.Fail(Status.Status == LinkStatus.Ready ? ErrorCodes.VolumeUnavailable : StatusError());

        if (current == 0)
        {
            // Громкость уже 0 (например, выставлена вручную) — возвращаем последний ненулевой уровень.
            var r = await SetVolumeAsync(restore > 0 ? restore : 50).ConfigureAwait(false);
            return r.Success ? CommandOutcome.Ok($"Звук включён · {restore}%") : r;
        }

        lock (_gate) _volumeBeforeMute = current.Value;
        var res = await SetVolumeAsync(0).ConfigureAwait(false);
        if (!res.Success) return res;
        lock (_gate) _muted = true;
        Publish();
        return CommandOutcome.Ok("Звук выключен");
    }

    /// <summary>
    /// Базовое значение для шага громкости: при быстрых повторных нажатиях состояние из браузера
    /// может не успеть обновиться, поэтому берём последнее запрошенное значение.
    /// </summary>
    private int? CurrentVolumeForChange()
    {
        lock (_gate)
        {
            if (_requestedVolume is { } req && Environment.TickCount64 - _requestedVolumeAt < 1500)
                return req;
            return _status.VolumePercent;
        }
    }

    private string StatusError() => Status.Status switch
    {
        LinkStatus.WaitingForBrowser => ErrorCodes.NoBrowser,
        LinkStatus.NoVkTab => ErrorCodes.NoVkTab,
        LinkStatus.PlayerNotFound => ErrorCodes.PlayerNotFound,
        _ => ErrorCodes.Internal,
    };

    private sealed record RawResult(bool Ok, string? Error, PlayerSnapshot? State);

    private async Task<RawResult> SendAsync(string action, double? value = null)
    {
        LinkEntry? target;
        lock (_gate) target = SelectTargetLocked();
        if (target is null) return new(false, ErrorCodes.NoBrowser, null);

        // Если браузер явно сообщил, что вкладок VK нет — не ждём таймаут, сразу честная ошибка.
        if (target.State is { VkTabs: 0 }) return new(false, ErrorCodes.NoVkTab, null);

        var id = Interlocked.Increment(ref _nextId).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var tcs = new TaskCompletionSource<IncomingMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            await target.Link.SendAsync(new OutgoingCommand { Id = id, Action = action, Value = value }, cts.Token)
                .ConfigureAwait(false);
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(_timeout)).ConfigureAwait(false);
            if (completed != tcs.Task) return new(false, ErrorCodes.Timeout, null);
            var msg = tcs.Task.Result;
            return new(msg.Ok == true, msg.Ok == true ? null : msg.Error ?? ErrorCodes.Internal, msg.State);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            return new(false, ErrorCodes.NoBrowser, null);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    // ---------------------------------------------------------------- выбор вкладки и статус

    /// <summary>
    /// Если подключено несколько браузеров: играющий > с выбранным треком > недавно игравший > с плеером >
    /// с открытой вкладкой VK > любой.
    /// </summary>
    private LinkEntry? SelectTargetLocked()
    {
        if (_links.Count == 0) return null;
        return _links.Values
            .OrderByDescending(e => e.State?.PlayerFound == true && e.State.Playing)
            .ThenByDescending(e => e.State?.PlayerFound == true && e.State.HasTrack)
            .ThenByDescending(e => e.State?.LastPlayingAt ?? 0)
            .ThenByDescending(e => e.State?.PlayerFound == true)
            .ThenByDescending(e => (e.State?.VkTabs ?? 0) > 0)
            .ThenByDescending(e => e.UpdatedAt)
            .First();
    }

    private void Publish()
    {
        PlayerStatus status;
        (string Title, string? Artist)? trackChange = null;
        lock (_gate)
        {
            var target = SelectTargetLocked();
            var state = target?.State;
            var linkStatus = target is null ? LinkStatus.WaitingForBrowser
                : state is null || state.VkTabs == 0 ? LinkStatus.NoVkTab
                : !state.PlayerFound ? LinkStatus.PlayerNotFound
                : LinkStatus.Ready;

            status = new PlayerStatus(linkStatus, target?.Link.BrowserName, _links.Count,
                linkStatus == LinkStatus.Ready ? state : null, _muted);
            _status = status;

            if (state is not null)
            {
                var key = state.HasTrack && !string.IsNullOrWhiteSpace(state.Title)
                    ? $"{state.Title}\u0001{state.Artist}" : "";
                if (!_trackPrimed)
                {
                    _trackPrimed = true;
                    _lastTrackKey = key;
                }
                else if (key != _lastTrackKey)
                {
                    _lastTrackKey = key;
                    if (key.Length > 0) trackChange = (state.Title!, state.Artist);
                }
            }
        }

        StatusChanged?.Invoke(status);
        if (trackChange is { } t) TrackChanged?.Invoke(t.Title, t.Artist);
    }
}
