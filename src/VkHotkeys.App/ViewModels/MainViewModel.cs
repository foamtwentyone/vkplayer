using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Threading;
using VkHotkeys.App.Hotkeys;
using VkHotkeys.App.Services;
using VkHotkeys.Core.Hotkeys;
using VkHotkeys.Core.Player;
using VkHotkeys.Core.Settings;

namespace VkHotkeys.App.ViewModels;

public enum StatusLevel { Idle, Ok, Warning, Error }

public sealed class MainViewModel : ObservableObject
{
    private readonly AppEngine _engine;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _volumeDebounce;
    private bool _loading;
    private bool _applyingStatus;
    private long _volumeUserUntil;

    public MainViewModel(AppEngine engine, Dispatcher dispatcher, string bridgeStatus, bool bridgeOk)
    {
        _engine = engine;
        _dispatcher = dispatcher;
        _bridgeStatus = bridgeStatus;
        _bridgeOk = bridgeOk;

        _volumeDebounce = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = TimeSpan.FromMilliseconds(90) };
        _volumeDebounce.Tick += async (_, _) =>
        {
            _volumeDebounce.Stop();
            var r = await _engine.Player.SetVolumeAsync((int)Math.Round(_volume));
            if (!r.Success) LastAction = r.Message;
        };

        foreach (var action in HotkeyActionInfo.All)
            Hotkeys.Add(new HotkeyRowViewModel(action, HotkeyGesture.Empty, OnSettingChanged));

        PreviousCommand = new RelayCommand(() => Run(_engine.Player.PreviousAsync), () => HasPlayer);
        ToggleCommand = new RelayCommand(() => Run(_engine.Player.TogglePlayAsync), () => HasPlayer);
        NextCommand = new RelayCommand(() => Run(_engine.Player.NextAsync), () => HasPlayer);
        MuteCommand = new RelayCommand(() => Run(_engine.Player.ToggleMuteAsync), () => HasPlayer);
        SaveCommand = new RelayCommand(Save, () => IsDirty && !HasErrors);
        RevertCommand = new RelayCommand(() => LoadFrom(_engine.Settings), () => IsDirty);
        DefaultsCommand = new RelayCommand(ResetToDefaults);
        OpenVkCommand = new RelayCommand(() => OpenUrl("https://vk.com/audio"));
        OpenExtensionFolderCommand = new RelayCommand(OpenExtensionFolder);
        OpenLogCommand = new RelayCommand(() => OpenUrl(Log.Directory));

        LoadFrom(_engine.Settings);
        ApplyStatus(_engine.Player.Status);
        _engine.Player.StatusChanged += s => _dispatcher.BeginInvoke(() => ApplyStatus(s));
        _engine.HotkeyExecuted += (a, o) => _dispatcher.BeginInvoke(() =>
            LastAction = $"{a.DisplayName()} — {(o.Success ? o.Message : "ошибка: " + o.Message)}");
    }

    // ================================================================ плеер

    private string _statusText = "";
    private StatusLevel _statusLevel;
    private string _title = "";
    private string _artist = "";
    private bool _hasPlayer;
    private bool _isPlaying;
    private bool _isMuted;
    private double _volume;
    private bool _volumeAvailable;
    private double _progress;
    private string _timeText = "";
    private string _lastAction = "";
    private string _bridgeStatus;
    private bool _bridgeOk;

    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public StatusLevel StatusLevel { get => _statusLevel; private set => Set(ref _statusLevel, value); }
    public string Title { get => _title; private set => Set(ref _title, value); }
    public string Artist { get => _artist; private set => Set(ref _artist, value); }
    public bool HasPlayer { get => _hasPlayer; private set => Set(ref _hasPlayer, value); }
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public string TimeText { get => _timeText; private set => Set(ref _timeText, value); }
    public string LastAction { get => _lastAction; private set => Set(ref _lastAction, value); }
    public string BridgeStatus { get => _bridgeStatus; set => Set(ref _bridgeStatus, value); }
    public bool BridgeOk { get => _bridgeOk; set => Set(ref _bridgeOk, value); }

    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (Set(ref _isPlaying, value)) OnPropertyChanged(nameof(PlayGlyph));
        }
    }

    /// <summary>Иконки Segoe Fluent: E769 — пауза, E768 — воспроизведение.</summary>
    public string PlayGlyph => IsPlaying ? "" : "";

    public bool IsMuted
    {
        get => _isMuted;
        private set
        {
            if (Set(ref _isMuted, value)) OnPropertyChanged(nameof(MuteGlyph));
        }
    }

    public string MuteGlyph => IsMuted || _volume < 0.5 ? "" : _volume < 34 ? "" : _volume < 67 ? "" : "";

    public bool VolumeAvailable { get => _volumeAvailable; private set => Set(ref _volumeAvailable, value); }

    /// <summary>Громкость плеера VK, 0..100. Изменение пользователем отправляется в браузер.</summary>
    public double Volume
    {
        get => _volume;
        set
        {
            if (!Set(ref _volume, value)) return;
            OnPropertyChanged(nameof(VolumeText));
            OnPropertyChanged(nameof(MuteGlyph));
            if (_applyingStatus) return;
            _volumeUserUntil = Environment.TickCount64 + 1500;
            _volumeDebounce.Stop();
            _volumeDebounce.Start();
        }
    }

    public string VolumeText => VolumeAvailable ? $"{Math.Round(_volume)}%" : "—";

    private void ApplyStatus(PlayerStatus s)
    {
        StatusText = s.Describe();
        StatusLevel = s.Status switch
        {
            LinkStatus.Ready => StatusLevel.Ok,
            LinkStatus.WaitingForBrowser => StatusLevel.Error,
            _ => StatusLevel.Warning,
        };

        var p = s.Player;
        HasPlayer = s.Status == LinkStatus.Ready;
        if (p is { HasTrack: true } && !string.IsNullOrWhiteSpace(p.Title))
        {
            Title = p.Title!;
            Artist = p.Artist ?? "";
        }
        else
        {
            Title = s.Status switch
            {
                LinkStatus.WaitingForBrowser => "Нет связи с браузером",
                LinkStatus.NoVkTab => "VK не открыт",
                LinkStatus.PlayerNotFound => "Плеер VK не найден",
                _ => "Ничего не играет",
            };
            Artist = s.Status switch
            {
                LinkStatus.WaitingForBrowser => "Откройте Chrome или Edge с установленным расширением VK Hotkeys",
                LinkStatus.NoVkTab => "Откройте vk.com/audio в браузере",
                LinkStatus.PlayerNotFound => "Откройте раздел «Музыка» и запустите трек",
                _ => "Запустите музыку в VK",
            };
        }
        IsPlaying = s.IsPlaying;
        IsMuted = s.IsMuted;

        if (p?.Duration is > 0 && p.Position is { } pos)
        {
            Progress = Math.Clamp(pos / p.Duration.Value, 0, 1);
            TimeText = $"{FormatTime(pos)} / {FormatTime(p.Duration.Value)}";
        }
        else
        {
            Progress = 0;
            TimeText = "";
        }

        VolumeAvailable = s.VolumePercent is not null;
        if (s.VolumePercent is { } v && Environment.TickCount64 > _volumeUserUntil)
        {
            _applyingStatus = true;
            Volume = v;
            _applyingStatus = false;
        }
        OnPropertyChanged(nameof(VolumeText));
        CommandManager.InvalidateRequerySuggested();
    }

    private static string FormatTime(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    private async void Run(Func<Task<CommandOutcome>> command)
    {
        var r = await command();
        LastAction = r.Success ? r.Message : $"Ошибка: {r.Message}";
    }

    public ICommand PreviousCommand { get; }
    public ICommand ToggleCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand MuteCommand { get; }

    // ================================================================ настройки

    public ObservableCollection<HotkeyRowViewModel> Hotkeys { get; } = new();

    private bool _startWithWindows, _startMinimized, _closeToTray, _notifyOnTrackChange, _openVk;
    private NotificationMode _notifications;
    private HotkeyBackend _backend;
    private int _volumeStep, _seekStep;
    private bool _isDirty;
    private bool _hasErrors;
    private string _saveStatus = "";
    private string _validationSummary = "";

    public bool StartWithWindows { get => _startWithWindows; set { if (Set(ref _startWithWindows, value)) OnSettingChanged(); } }
    public bool StartMinimized { get => _startMinimized; set { if (Set(ref _startMinimized, value)) OnSettingChanged(); } }
    public bool CloseToTray { get => _closeToTray; set { if (Set(ref _closeToTray, value)) OnSettingChanged(); } }
    public bool NotifyOnTrackChange { get => _notifyOnTrackChange; set { if (Set(ref _notifyOnTrackChange, value)) OnSettingChanged(); } }
    public bool OpenVkIfNotConnected { get => _openVk; set { if (Set(ref _openVk, value)) OnSettingChanged(); } }

    public int VolumeStep
    {
        get => _volumeStep;
        set { if (Set(ref _volumeStep, Math.Clamp(value, 1, 25))) OnSettingChanged(); }
    }

    public int SeekStep
    {
        get => _seekStep;
        set { if (Set(ref _seekStep, Math.Clamp(value, 1, 120))) OnSettingChanged(); }
    }

    public NotificationMode Notifications
    {
        get => _notifications;
        set
        {
            if (!Set(ref _notifications, value)) return;
            OnPropertyChanged(nameof(NotifyOverlay));
            OnPropertyChanged(nameof(NotifyWindows));
            OnPropertyChanged(nameof(NotifyNone));
            OnSettingChanged();
        }
    }

    public bool NotifyOverlay { get => Notifications == NotificationMode.Overlay; set { if (value) Notifications = NotificationMode.Overlay; } }
    public bool NotifyWindows { get => Notifications == NotificationMode.Windows; set { if (value) Notifications = NotificationMode.Windows; } }
    public bool NotifyNone { get => Notifications == NotificationMode.None; set { if (value) Notifications = NotificationMode.None; } }

    public HotkeyBackend Backend
    {
        get => _backend;
        set
        {
            if (!Set(ref _backend, value)) return;
            OnPropertyChanged(nameof(BackendHook));
            OnPropertyChanged(nameof(BackendRegister));
            OnSettingChanged();
        }
    }

    public bool BackendHook { get => Backend == HotkeyBackend.LowLevelHook; set { if (value) Backend = HotkeyBackend.LowLevelHook; } }
    public bool BackendRegister { get => Backend == HotkeyBackend.RegisterHotKey; set { if (value) Backend = HotkeyBackend.RegisterHotKey; } }

    public bool IsDirty { get => _isDirty; private set => Set(ref _isDirty, value); }
    public bool HasErrors { get => _hasErrors; private set => Set(ref _hasErrors, value); }
    public string SaveStatus { get => _saveStatus; private set => Set(ref _saveStatus, value); }
    public string ValidationSummary { get => _validationSummary; private set => Set(ref _validationSummary, value); }

    public ICommand SaveCommand { get; }
    public ICommand RevertCommand { get; }
    public ICommand DefaultsCommand { get; }
    public ICommand OpenVkCommand { get; }
    public ICommand OpenExtensionFolderCommand { get; }
    public ICommand OpenLogCommand { get; }

    private void LoadFrom(AppSettings s)
    {
        _loading = true;
        foreach (var row in Hotkeys)
            row.Gesture = s.Hotkeys.TryGetValue(row.Action, out var g) ? g : HotkeyGesture.Empty;
        StartWithWindows = s.StartWithWindows;
        StartMinimized = s.StartMinimized;
        CloseToTray = s.CloseToTray;
        NotifyOnTrackChange = s.NotifyOnTrackChange;
        OpenVkIfNotConnected = s.OpenVkIfNotConnected;
        VolumeStep = s.VolumeStep;
        SeekStep = s.SeekStepSeconds;
        Notifications = s.Notifications;
        Backend = s.Backend;
        _loading = false;
        IsDirty = false;
        Validate(_engine.RegistrationIssues);
    }

    private AppSettings BuildSettings()
    {
        var s = _engine.Settings.Clone();
        s.Hotkeys = Hotkeys.ToDictionary(r => r.Action, r => r.Gesture);
        s.StartWithWindows = StartWithWindows;
        s.StartMinimized = StartMinimized;
        s.CloseToTray = CloseToTray;
        s.NotifyOnTrackChange = NotifyOnTrackChange;
        s.OpenVkIfNotConnected = OpenVkIfNotConnected;
        s.VolumeStep = VolumeStep;
        s.SeekStepSeconds = SeekStep;
        s.Notifications = Notifications;
        s.Backend = Backend;
        return s;
    }

    private void OnSettingChanged()
    {
        if (_loading) return;
        IsDirty = true;
        SaveStatus = "Есть несохранённые изменения";
        Validate(Array.Empty<HotkeyIssue>());
    }

    private void ResetToDefaults()
    {
        var defaults = new AppSettings();
        foreach (var row in Hotkeys) row.Gesture = defaults.Hotkeys[row.Action];
        VolumeStep = defaults.VolumeStep;
        SeekStep = defaults.SeekStepSeconds;
        Notifications = defaults.Notifications;
        Backend = defaults.Backend;
        NotifyOnTrackChange = defaults.NotifyOnTrackChange;
        IsDirty = true;
        SaveStatus = "Восстановлены значения по умолчанию — нажмите «Сохранить»";
    }

    private void Save()
    {
        try
        {
            var registration = _engine.ApplySettings(BuildSettings());
            IsDirty = false;
            Validate(registration);
            SaveStatus = registration.Count == 0
                ? $"Сохранено в {DateTime.Now:HH:mm:ss}"
                : "Сохранено, но часть клавиш не зарегистрирована";
        }
        catch (Exception ex)
        {
            Log.Write($"save settings: {ex}");
            SaveStatus = $"Не удалось сохранить: {ex.Message}";
        }
    }

    /// <summary>Проверка конфликтов: дубликаты, системные сочетания, занятые другими программами.</summary>
    private void Validate(IReadOnlyList<HotkeyIssue> registrationIssues)
    {
        var bindings = Hotkeys.ToDictionary(r => r.Action, r => r.Gesture);
        var issues = HotkeyConflictChecker.Check(bindings).Concat(registrationIssues).ToList();

        if (Backend == HotkeyBackend.LowLevelHook)
        {
            foreach (var row in Hotkeys)
            {
                if (row.Gesture.IsEmpty || issues.Any(i => i.Action == row.Action)) continue;
                try
                {
                    if (HotkeyProbe.IsTakenByOtherApp(row.Gesture))
                        issues.Add(new HotkeyIssue(row.Action, IssueSeverity.Warning,
                            "Сочетание занято другой программой — она перестанет его получать"));
                }
                catch (DllNotFoundException) { /* не Windows */ }
                catch (EntryPointNotFoundException) { }
            }
        }

        foreach (var row in Hotkeys)
        {
            var issue = issues.Where(i => i.Action == row.Action)
                .OrderByDescending(i => i.Severity).FirstOrDefault();
            row.SetIssue(issue is null ? IssueLevel.None
                    : issue.Severity == IssueSeverity.Error ? IssueLevel.Error : IssueLevel.Warning,
                issue?.Message);
        }

        HasErrors = HotkeyConflictChecker.HasErrors(issues);
        var errors = issues.Count(i => i.Severity == IssueSeverity.Error);
        var warnings = issues.Count - errors;
        ValidationSummary = errors > 0 ? $"Исправьте конфликты ({errors}), чтобы сохранить"
            : warnings > 0 ? $"Предупреждений: {warnings}"
            : "Конфликтов нет";
        CommandManager.InvalidateRequerySuggested();
    }

    private void OpenExtensionFolder()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "extension");
        if (Directory.Exists(dir)) OpenUrl(dir);
        else SaveStatus = "Папка extension не найдена рядом с программой";
    }

    private static void OpenUrl(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Write($"open {target}: {ex.Message}");
        }
    }
}
