using VkHotkeys.App.Hotkeys;
using VkHotkeys.Core.Hotkeys;
using VkHotkeys.Core.Ipc;
using VkHotkeys.Core.Player;
using VkHotkeys.Core.Settings;

namespace VkHotkeys.App.Services;

/// <summary>
/// «Сердце» программы, не зависящее от окна: сервер канала для браузеров, контроллер плеера,
/// глобальные горячие клавиши и уведомления. Работает, даже если главное окно ни разу не открывалось.
/// </summary>
public sealed class AppEngine : IDisposable
{
    private readonly SettingsStore _store;
    private readonly PipeServer _server = new();
    private readonly NotificationService _notifications;
    private IHotkeyService? _hotkeys;
    private int _suspendCount;

    public PlayerController Player { get; } = new();
    public AppSettings Settings { get; private set; }
    public IReadOnlyList<HotkeyIssue> RegistrationIssues { get; private set; } = Array.Empty<HotkeyIssue>();

    /// <summary>Результат команды, выполненной горячей клавишей (фоновый поток).</summary>
    public event Action<HotkeyAction, CommandOutcome>? HotkeyExecuted;

    public AppEngine(SettingsStore store, NotificationService notifications)
    {
        _store = store;
        _notifications = notifications;
        Settings = store.Load();
    }

    public void Start()
    {
        _server.ClientConnected += c =>
        {
            Log.Write("browser bridge connected");
            c.Closed += _ => Log.Write($"browser bridge disconnected ({c.BrowserName})");
            Player.Attach(c);
        };
        _server.Start();

        Player.TrackChanged += (title, artist) =>
        {
            if (Settings.NotifyOnTrackChange) _notifications.TrackChanged(title, artist);
        };

        ApplyRuntimeSettings(Settings, backendChanged: true);
    }

    /// <summary>Сохраняет и применяет настройки. Возвращает ошибки регистрации клавиш (если есть).</summary>
    public IReadOnlyList<HotkeyIssue> ApplySettings(AppSettings settings)
    {
        settings = settings.Clone();
        settings.Normalize();
        var backendChanged = settings.Backend != Settings.Backend;
        _store.Save(settings);
        Settings = settings;
        return ApplyRuntimeSettings(settings, backendChanged);
    }

    private IReadOnlyList<HotkeyIssue> ApplyRuntimeSettings(AppSettings s, bool backendChanged)
    {
        Player.VolumeStepPercent = s.VolumeStep;
        Player.SeekStepSeconds = s.SeekStepSeconds;
        _notifications.Mode = s.Notifications;
        AutoStart.Set(s.StartWithWindows);

        if (backendChanged || _hotkeys is null)
        {
            _hotkeys?.Dispose();
            _hotkeys = s.Backend == HotkeyBackend.RegisterHotKey
                ? new RegisterHotKeyService()
                : new LowLevelHookService();
            _hotkeys.Triggered += OnHotkey;
            _hotkeys.Suspended = _suspendCount > 0;
        }

        RegistrationIssues = _hotkeys.Apply(s.Hotkeys);
        foreach (var issue in RegistrationIssues) Log.Write($"hotkey issue: {issue.Action}: {issue.Message}");
        Log.Write($"hotkeys applied via {_hotkeys.Name}: " +
                  string.Join(", ", s.Hotkeys.Where(h => !h.Value.IsEmpty).Select(h => $"{h.Key}={h.Value}")));
        return RegistrationIssues;
    }

    /// <summary>Отключение горячих клавиш на время ввода новой комбинации в настройках.</summary>
    public void SuspendHotkeys(bool suspend)
    {
        _suspendCount = Math.Max(0, _suspendCount + (suspend ? 1 : -1));
        if (_hotkeys is not null) _hotkeys.Suspended = _suspendCount > 0;
    }

    public async void Execute(HotkeyAction action)
    {
        try
        {
            var outcome = await Player.ExecuteAsync(action).ConfigureAwait(false);
            if (!outcome.Success) Log.Write($"{action}: {outcome.Message}");
            _notifications.CommandFeedback(action, outcome, Player.Status);
            HotkeyExecuted?.Invoke(action, outcome);
        }
        catch (Exception ex)
        {
            Log.Write($"execute {action}: {ex}");
        }
    }

    private void OnHotkey(HotkeyAction action) => Execute(action);

    public void Dispose()
    {
        _hotkeys?.Dispose();
        _server.Dispose();
    }
}
