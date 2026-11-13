using System.Text.Json.Serialization;
using VkHotkeys.Core.Hotkeys;

namespace VkHotkeys.Core.Settings;

public enum HotkeyBackend
{
    /// <summary>Низкоуровневый хук клавиатуры (WH_KEYBOARD_LL): нажатие гарантированно не доходит до игры.</summary>
    LowLevelHook,
    /// <summary>Классический RegisterHotKey: проще, но некоторые игры с Raw Input всё равно видят клавишу.</summary>
    RegisterHotKey,
}

public enum NotificationMode
{
    /// <summary>Собственная плашка поверх экрана (не забирает фокус у игры).</summary>
    Overlay,
    /// <summary>Уведомления Windows (в полноэкранных играх Windows их обычно скрывает).</summary>
    Windows,
    None,
}

public sealed class AppSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Действие -> комбинация. Пустая строка в файле — действие без клавиши.</summary>
    public Dictionary<HotkeyAction, HotkeyGesture> Hotkeys { get; set; } = new(HotkeyActionInfo.Defaults);

    [JsonConverter(typeof(JsonStringEnumConverter<HotkeyBackend>))]
    public HotkeyBackend Backend { get; set; } = HotkeyBackend.LowLevelHook;

    [JsonConverter(typeof(JsonStringEnumConverter<NotificationMode>))]
    public NotificationMode Notifications { get; set; } = NotificationMode.Overlay;

    /// <summary>Показывать плашку при смене трека.</summary>
    public bool NotifyOnTrackChange { get; set; } = true;

    /// <summary>Шаг громкости в процентах.</summary>
    public int VolumeStep { get; set; } = 5;

    /// <summary>Шаг перемотки в секундах.</summary>
    public int SeekStepSeconds { get; set; } = 10;

    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; } = true;
    public bool CloseToTray { get; set; } = true;

    /// <summary>Открывать vk.com/audio в браузере по умолчанию, если через 15 с после запуска нет подключения.</summary>
    public bool OpenVkIfNotConnected { get; set; }

    /// <summary>Дополнительные ID расширения (если вы упаковали его со своим ключом).</summary>
    public List<string> ExtraExtensionIds { get; set; } = new();

    public AppSettings Clone()
    {
        var c = (AppSettings)MemberwiseClone();
        c.Hotkeys = new Dictionary<HotkeyAction, HotkeyGesture>(Hotkeys);
        c.ExtraExtensionIds = new List<string>(ExtraExtensionIds);
        return c;
    }

    /// <summary>Приводит значения к допустимым диапазонам и дополняет отсутствующие действия.</summary>
    public void Normalize()
    {
        Version = CurrentVersion;
        Hotkeys ??= new();
        foreach (var (action, gesture) in HotkeyActionInfo.Defaults)
            Hotkeys.TryAdd(action, gesture);
        foreach (var unknown in Hotkeys.Keys.Where(k => !Enum.IsDefined(k)).ToList())
            Hotkeys.Remove(unknown);
        VolumeStep = Math.Clamp(VolumeStep, 1, 25);
        SeekStepSeconds = Math.Clamp(SeekStepSeconds, 1, 120);
        if (!Enum.IsDefined(Backend)) Backend = HotkeyBackend.LowLevelHook;
        if (!Enum.IsDefined(Notifications)) Notifications = NotificationMode.Overlay;
        ExtraExtensionIds ??= new();
        ExtraExtensionIds = ExtraExtensionIds
            .Where(id => id is { Length: 32 } && id.All(ch => ch is >= 'a' and <= 'p'))
            .Distinct().ToList();
    }
}
