using VkHotkeys.Core.Hotkeys;

namespace VkHotkeys.App.Hotkeys;

public interface IHotkeyService : IDisposable
{
    /// <summary>Срабатывание комбинации. Вызывается в фоновом потоке.</summary>
    event Action<HotkeyAction>? Triggered;

    /// <summary>Применяет набор комбинаций. Возвращает комбинации, которые не удалось зарегистрировать.</summary>
    IReadOnlyList<HotkeyIssue> Apply(IReadOnlyDictionary<HotkeyAction, HotkeyGesture> bindings);

    /// <summary>Временное отключение (пока пользователь вводит новую комбинацию в настройках).</summary>
    bool Suspended { get; set; }

    string Name { get; }
}
