namespace VkHotkeys.Core.Hotkeys;

public enum IssueSeverity { Warning, Error }

public sealed record HotkeyIssue(HotkeyAction Action, IssueSeverity Severity, string Message);

/// <summary>
/// Проверка набора горячих клавиш: дубликаты, системные сочетания и клавиши,
/// перехват которых сломает обычную работу (набор текста и т. п.).
/// </summary>
public static class HotkeyConflictChecker
{
    public static IReadOnlyList<HotkeyIssue> Check(IReadOnlyDictionary<HotkeyAction, HotkeyGesture> bindings)
    {
        var issues = new List<HotkeyIssue>();

        foreach (var group in bindings.Where(b => !b.Value.IsEmpty).GroupBy(b => b.Value))
        {
            var actions = group.Select(g => g.Key).OrderBy(a => a).ToList();
            if (actions.Count < 2) continue;
            foreach (var action in actions)
            {
                var others = string.Join(", ", actions.Where(a => a != action).Select(a => $"«{a.DisplayName()}»"));
                issues.Add(new HotkeyIssue(action, IssueSeverity.Error, $"{group.Key} уже назначено на {others}"));
            }
        }

        foreach (var (action, gesture) in bindings)
        {
            if (gesture.IsEmpty) continue;
            var problem = CheckSingle(gesture);
            if (problem is not null) issues.Add(problem with { Action = action });
        }

        return issues;
    }

    /// <summary>Проверка одной комбинации без учёта остальных.</summary>
    public static HotkeyIssue? CheckSingle(HotkeyGesture g)
    {
        const HotkeyAction none = default;
        var vk = g.VirtualKey;
        var mods = g.Modifiers;

        if (VirtualKeys.IsModifier(vk))
            return new(none, IssueSeverity.Error, "Нужна основная клавиша, а не только модификатор");

        if (IsSystemReserved(g))
            return new(none, IssueSeverity.Error, $"{g} зарезервировано Windows");

        if (mods == HotkeyModifiers.None)
        {
            if (vk is >= VirtualKeys.A and <= VirtualKeys.Z or >= VirtualKeys.D0 and <= VirtualKeys.D9
                or VirtualKeys.Space or VirtualKeys.Enter or VirtualKeys.Back or VirtualKeys.Tab or VirtualKeys.Escape
                or >= 0xBA and <= 0xDE)
                return new(none, IssueSeverity.Error,
                    $"Клавиша {g} без модификаторов перестанет работать во всех программах (в т. ч. в чате Dota 2)");

            if (vk is VirtualKeys.Left or VirtualKeys.Right or VirtualKeys.Up or VirtualKeys.Down
                or VirtualKeys.Home or VirtualKeys.End or VirtualKeys.PageUp or VirtualKeys.PageDown
                or VirtualKeys.Delete or VirtualKeys.Insert)
                return new(none, IssueSeverity.Warning, $"{g} без модификаторов будет перехватываться во всех программах");
        }

        if (vk == VirtualKeys.F12 && mods == HotkeyModifiers.None)
            return new(none, IssueSeverity.Warning, "F12 — клавиша скриншота Steam по умолчанию");

        if (vk is >= 0xAD and <= 0xB3)
            return new(none, IssueSeverity.Warning, "Мультимедийная клавиша будет отобрана у остальных программ");

        if (mods.HasFlag(HotkeyModifiers.Win))
            return new(none, IssueSeverity.Warning, "Сочетания с Win часто заняты Windows и могут не сработать");

        return null;
    }

    private static bool IsSystemReserved(HotkeyGesture g)
    {
        var vk = g.VirtualKey;
        var m = g.Modifiers;
        return (m == (HotkeyModifiers.Control | HotkeyModifiers.Alt) && vk == VirtualKeys.Delete)
            || (m == (HotkeyModifiers.Control | HotkeyModifiers.Shift) && vk == VirtualKeys.Escape)
            || (m == HotkeyModifiers.Alt && vk is VirtualKeys.Tab or VirtualKeys.F1 + 3 or VirtualKeys.Escape)
            || (m == HotkeyModifiers.Control && vk == VirtualKeys.Escape)
            || (m.HasFlag(HotkeyModifiers.Win) && vk == 0x4C); // Win+L
    }

    public static bool HasErrors(IEnumerable<HotkeyIssue> issues) => issues.Any(i => i.Severity == IssueSeverity.Error);
}
