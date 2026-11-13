namespace VkHotkeys.Core.Hotkeys;

public enum HotkeyAction
{
    PlayPause,
    NextTrack,
    PreviousTrack,
    VolumeUp,
    VolumeDown,
    ToggleMute,
    SeekForward,
    SeekBackward,
}

public static class HotkeyActionInfo
{
    public static IReadOnlyList<HotkeyAction> All { get; } = Enum.GetValues<HotkeyAction>();

    public static string DisplayName(this HotkeyAction action) => action switch
    {
        HotkeyAction.PlayPause => "Пауза / воспроизведение",
        HotkeyAction.NextTrack => "Следующий трек",
        HotkeyAction.PreviousTrack => "Предыдущий трек",
        HotkeyAction.VolumeUp => "Громкость +",
        HotkeyAction.VolumeDown => "Громкость −",
        HotkeyAction.ToggleMute => "Mute / вернуть громкость",
        HotkeyAction.SeekForward => "Перемотка вперёд",
        HotkeyAction.SeekBackward => "Перемотка назад",
        _ => action.ToString(),
    };

    /// <summary>
    /// Срабатывает ли действие повторно при удержании клавиши (автоповтор).
    /// Переключение треков и пауза — строго один раз на нажатие, чтобы не «пролистать» плейлист.
    /// </summary>
    public static bool IsRepeatable(this HotkeyAction action) => action is
        HotkeyAction.VolumeUp or HotkeyAction.VolumeDown or
        HotkeyAction.SeekForward or HotkeyAction.SeekBackward;

    public static IReadOnlyDictionary<HotkeyAction, HotkeyGesture> Defaults { get; } =
        new Dictionary<HotkeyAction, HotkeyGesture>
        {
            [HotkeyAction.PlayPause] = new(VirtualKeys.F8, HotkeyModifiers.None),
            [HotkeyAction.PreviousTrack] = new(VirtualKeys.F9, HotkeyModifiers.None),
            [HotkeyAction.NextTrack] = new(VirtualKeys.F10, HotkeyModifiers.None),
            [HotkeyAction.VolumeUp] = new(VirtualKeys.Up, HotkeyModifiers.Control | HotkeyModifiers.Alt),
            [HotkeyAction.VolumeDown] = new(VirtualKeys.Down, HotkeyModifiers.Control | HotkeyModifiers.Alt),
            [HotkeyAction.ToggleMute] = new(VirtualKeys.M, HotkeyModifiers.Control | HotkeyModifiers.Alt),
            [HotkeyAction.SeekForward] = new(VirtualKeys.Right, HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift),
            [HotkeyAction.SeekBackward] = new(VirtualKeys.Left, HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift),
        };
}
