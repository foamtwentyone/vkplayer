namespace VkHotkeys.Core.Hotkeys;

/// <summary>Модификаторы. Значения совпадают с MOD_* из WinAPI RegisterHotKey.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>Комбинация клавиш: виртуальный код клавиши Windows + модификаторы.</summary>
public readonly record struct HotkeyGesture(int VirtualKey, HotkeyModifiers Modifiers)
{
    public static HotkeyGesture Empty => default;

    public bool IsEmpty => VirtualKey == 0;

    public override string ToString()
    {
        if (IsEmpty) return "";
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(KeyNames.GetName(VirtualKey));
        return string.Join(" + ", parts);
    }

    /// <summary>Разбирает строку вида "Ctrl + Alt + Up" или "F10". Пустая строка — пустая комбинация.</summary>
    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = Empty;
        if (string.IsNullOrWhiteSpace(text)) return true;

        var tokens = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) return true;

        var mods = HotkeyModifiers.None;
        int? key = null;
        foreach (var token in tokens)
        {
            switch (token.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= HotkeyModifiers.Control; continue;
                case "alt": mods |= HotkeyModifiers.Alt; continue;
                case "shift": mods |= HotkeyModifiers.Shift; continue;
                case "win" or "windows": mods |= HotkeyModifiers.Win; continue;
            }
            if (key is not null) return false;       // две основные клавиши
            if (!KeyNames.TryGetKey(token, out var vk)) return false;
            key = vk;
        }
        if (key is null) return false;               // одни модификаторы
        gesture = new HotkeyGesture(key.Value, mods);
        return true;
    }

    public static HotkeyGesture Parse(string text) =>
        TryParse(text, out var g) ? g : throw new FormatException($"Не удалось разобрать комбинацию «{text}»");
}
