namespace VkHotkeys.Core.Hotkeys;

public readonly record struct KeyEventDecision(bool Swallow, HotkeyAction? Fire);

/// <summary>
/// Чистая логика сопоставления событий клавиатуры с биндингами (без WinAPI — тестируется отдельно).
/// Используется низкоуровневым хуком: решает, «проглотить» ли нажатие и какое действие выполнить.
///
/// Правила:
/// * совпадение модификаторов строгое (F9 и Shift+F9 — разные комбинации);
/// * нажатие, вызвавшее действие, не передаётся дальше (в игру), как и его отпускание;
/// * автоповтор при удержании: для громкости/перемотки — повторяет действие, для треков/паузы — только глушится.
/// Не потокобезопасен: вызывается только из потока хука; биндинги заменяются атомарно.
/// </summary>
public sealed class HotkeyMatcher
{
    private volatile Dictionary<HotkeyGesture, HotkeyAction> _map = new();
    private readonly HashSet<int> _held = new();

    public void SetBindings(IReadOnlyDictionary<HotkeyAction, HotkeyGesture> bindings)
    {
        var map = new Dictionary<HotkeyGesture, HotkeyAction>();
        foreach (var (action, gesture) in bindings)
            if (!gesture.IsEmpty) map.TryAdd(gesture, action);
        _map = map;
    }

    public bool HasBindings => _map.Count > 0;

    public KeyEventDecision OnKeyDown(int vk, HotkeyModifiers mods)
    {
        if (VirtualKeys.IsModifier(vk)) return default;

        if (_held.Contains(vk))
        {
            // Автоповтор клавиши, которую мы уже перехватили.
            if (_map.TryGetValue(new HotkeyGesture(vk, mods), out var held) && held.IsRepeatable())
                return new(true, held);
            return new(true, null);
        }

        if (_map.TryGetValue(new HotkeyGesture(vk, mods), out var action))
        {
            _held.Add(vk);
            return new(true, action);
        }
        return default;
    }

    public KeyEventDecision OnKeyUp(int vk)
    {
        // Отпускание клавиши, нажатие которой мы проглотили, тоже не должно дойти до игры.
        return _held.Remove(vk) ? new(true, null) : default;
    }

    /// <summary>Сброс состояния (например, после переустановки хука).</summary>
    public void Reset() => _held.Clear();
}
