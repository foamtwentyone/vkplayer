using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using VkHotkeys.App.Controls;
using VkHotkeys.App.Services;
using VkHotkeys.App.ViewModels;
using VkHotkeys.App.Views;
using VkHotkeys.Core.Player;
using VkHotkeys.Core.Settings;

namespace VkHotkeys.App;

/// <summary>
/// Точка входа. Аргументы командной строки:
///   --tray        запуск без окна (используется автозапуском);
///   --register    только зарегистрировать мост Native Messaging и выйти;
///   --unregister  удалить регистрацию моста и автозапуск и выйти.
/// </summary>
public partial class App : Application
{
    private SingleInstance? _single;
    private AppEngine? _engine;
    private TrayIconService? _tray;
    private MainViewModel? _viewModel;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args.Select(a => a.ToLowerInvariant()).ToHashSet();

        if (args.Contains("--unregister"))
        {
            NativeHostRegistrar.Unregister();
            AutoStart.Set(false);
            Shutdown(0);
            return;
        }
        if (args.Contains("--register"))
        {
            var r = NativeHostRegistrar.Register(new SettingsStore().Load().ExtraExtensionIds);
            Shutdown(r.Success ? 0 : 1);
            return;
        }

        _single = new SingleInstance(() => Dispatcher.BeginInvoke(ShowMainWindow));
        if (!_single.IsFirst)
        {
            _single.SignalFirstInstance(); // уже запущено — показываем существующее окно
            Shutdown(0);
            return;
        }

        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Write($"UI exception: {ex.Exception}");
            ex.Handled = true; // фоновая утилита не должна падать из-за ошибки интерфейса
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log.Write($"fatal: {ex.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            Log.Write($"task exception: {ex.Exception}");
            ex.SetObserved();
        };

        Log.Write($"start {string.Join(' ', e.Args)}");

        _tray = new TrayIconService();
        var notifications = new NotificationService(Dispatcher, _tray);
        _engine = new AppEngine(new SettingsStore(), notifications);
        _engine.Start();

        // Регистрация моста при каждом запуске: если программу перенесли в другую папку, путь обновится.
        var registration = NativeHostRegistrar.Register(_engine.Settings.ExtraExtensionIds);
        Log.Write(registration.Message);

        _viewModel = new MainViewModel(_engine, Dispatcher, registration.Message, registration.Success);
        HotkeyBox.CaptureChanged += capturing => _engine.SuspendHotkeys(capturing);

        _tray.ShowRequested += ShowMainWindow;
        _tray.ExitRequested += ExitApplication;
        _tray.CommandRequested += a => _engine.Execute(a);
        _engine.Player.StatusChanged += s => Dispatcher.BeginInvoke(() => UpdateTooltip(s));

        var startHidden = args.Contains("--tray") || _engine.Settings.StartMinimized;
        if (!startHidden) ShowMainWindow();

        if (_engine.Settings.OpenVkIfNotConnected) ScheduleOpenVk();
    }

    private void ShowMainWindow()
    {
        if (_viewModel is null || _engine is null) return;
        // Окно создаётся только по требованию: в фоне программа не тратит ресурсы на интерфейс.
        _window ??= new MainWindow(_viewModel, () => _engine.Settings.CloseToTray);
        _window.BringToFront();
    }

    private void UpdateTooltip(PlayerStatus s)
    {
        var text = s.Status == LinkStatus.Ready && s.Player?.Title is { Length: > 0 } title
            ? $"VK Hotkeys — {(s.IsPlaying ? "▶" : "⏸")} {s.Player.Artist} – {title}"
            : $"VK Hotkeys — {s.Describe()}";
        _tray?.SetTooltip(text);
    }

    private void ScheduleOpenVk()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var status = _engine?.Player.Status.Status;
            if (status is LinkStatus.WaitingForBrowser or LinkStatus.NoVkTab)
            {
                try { Process.Start(new ProcessStartInfo("https://vk.com/audio") { UseShellExecute = true }); }
                catch (Exception ex) { Log.Write($"open vk: {ex.Message}"); }
            }
        };
        timer.Start();
    }

    private void ExitApplication()
    {
        if (_window is not null)
        {
            _window.AllowClose = true;
            _window.Close();
        }
        Shutdown(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _engine?.Dispose();
        _tray?.Dispose();
        _single?.Dispose();
        Log.Write("exit");
        base.OnExit(e);
    }
}
