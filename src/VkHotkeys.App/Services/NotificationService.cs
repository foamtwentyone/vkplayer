using System.Windows.Threading;
using VkHotkeys.App.Views;
using VkHotkeys.Core.Hotkeys;
using VkHotkeys.Core.Player;
using VkHotkeys.Core.Settings;

namespace VkHotkeys.App.Services;

/// <summary>Уведомления: плашка поверх экрана или уведомления Windows — по настройке.</summary>
public sealed class NotificationService
{
    private readonly Dispatcher _dispatcher;
    private readonly TrayIconService _tray;
    private OsdWindow? _osd;

    public NotificationMode Mode { get; set; } = NotificationMode.Overlay;

    public NotificationService(Dispatcher dispatcher, TrayIconService tray)
    {
        _dispatcher = dispatcher;
        _tray = tray;
    }

    private OsdWindow Osd => _osd ??= new OsdWindow();

    public void TrackChanged(string title, string? artist) => _dispatcher.BeginInvoke(() =>
    {
        switch (Mode)
        {
            case NotificationMode.Overlay: Osd.ShowTrack(title, artist); break;
            case NotificationMode.Windows: _tray.ShowBalloon(title, artist ?? ""); break;
        }
    });

    /// <summary>Отклик на горячую клавишу. Ошибки показываются всегда (кроме режима «Выкл»).</summary>
    public void CommandFeedback(HotkeyAction action, CommandOutcome outcome, PlayerStatus status) => _dispatcher.BeginInvoke(() =>
    {
        if (Mode == NotificationMode.None) return;

        if (!outcome.Success)
        {
            if (Mode == NotificationMode.Overlay) Osd.ShowMessage("", outcome.Message, action.DisplayName(), error: true);
            else _tray.ShowBalloon(outcome.Message, action.DisplayName(), error: true);
            return;
        }

        if (Mode != NotificationMode.Overlay) return; // уведомления Windows — только о смене трека и ошибках

        switch (action)
        {
            case HotkeyAction.VolumeUp or HotkeyAction.VolumeDown or HotkeyAction.ToggleMute:
                Osd.ShowVolume(status.VolumePercent ?? 0, status.IsMuted);
                break;
            case HotkeyAction.PlayPause:
                var playing = outcome.Message == "Воспроизведение";
                Osd.ShowMessage(playing ? "" : "", outcome.Message, status.Player?.Title, error: false);
                break;
            case HotkeyAction.SeekForward or HotkeyAction.SeekBackward:
                Osd.ShowMessage(action == HotkeyAction.SeekForward ? "" : "", outcome.Message,
                    status.Player?.Title, error: false);
                break;
            // Следующий/предыдущий: плашку покажет событие смены трека.
        }
    });
}
