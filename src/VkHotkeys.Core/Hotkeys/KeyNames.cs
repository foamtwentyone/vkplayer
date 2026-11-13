namespace VkHotkeys.Core.Hotkeys;

/// <summary>Человекочитаемые имена виртуальных клавиш (и обратное преобразование для файла настроек).</summary>
public static class KeyNames
{
    private static readonly Dictionary<int, string> Names = Build();
    private static readonly Dictionary<string, int> Keys =
        Names.ToDictionary(p => p.Value, p => p.Key, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<int, string> Build()
    {
        var d = new Dictionary<int, string>
        {
            [VirtualKeys.Back] = "Backspace", [VirtualKeys.Tab] = "Tab", [VirtualKeys.Enter] = "Enter",
            [VirtualKeys.Pause] = "Pause", [VirtualKeys.CapsLock] = "CapsLock", [VirtualKeys.Escape] = "Esc",
            [VirtualKeys.Space] = "Space", [VirtualKeys.PageUp] = "PageUp", [VirtualKeys.PageDown] = "PageDown",
            [VirtualKeys.End] = "End", [VirtualKeys.Home] = "Home", [VirtualKeys.Left] = "Left",
            [VirtualKeys.Up] = "Up", [VirtualKeys.Right] = "Right", [VirtualKeys.Down] = "Down",
            [VirtualKeys.PrintScreen] = "PrintScreen", [VirtualKeys.Insert] = "Insert", [VirtualKeys.Delete] = "Delete",
            [VirtualKeys.Apps] = "Menu",
            [VirtualKeys.Multiply] = "Num*", [VirtualKeys.Add] = "Num+", [VirtualKeys.Subtract] = "Num-",
            [VirtualKeys.Decimal] = "Num.", [VirtualKeys.Divide] = "Num/",
            [VirtualKeys.NumLock] = "NumLock", [VirtualKeys.ScrollLock] = "ScrollLock",
            [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".", [0xBF] = "/",
            [0xC0] = "`", [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'",
            [0xAD] = "VolumeMute", [0xAE] = "VolumeDown", [0xAF] = "VolumeUp",
            [0xB0] = "MediaNext", [0xB1] = "MediaPrev", [0xB2] = "MediaStop", [0xB3] = "MediaPlayPause",
        };
        for (var vk = VirtualKeys.D0; vk <= VirtualKeys.D9; vk++) d[vk] = ((char)vk).ToString();
        for (var vk = VirtualKeys.A; vk <= VirtualKeys.Z; vk++) d[vk] = ((char)vk).ToString();
        for (var i = 0; i <= 9; i++) d[VirtualKeys.NumPad0 + i] = $"Num{i}";
        for (var i = 0; i < 24; i++) d[VirtualKeys.F1 + i] = $"F{i + 1}";
        return d;
    }

    public static string GetName(int vk) => Names.TryGetValue(vk, out var n) ? n : $"0x{vk:X2}";

    public static bool TryGetKey(string name, out int vk)
    {
        if (Keys.TryGetValue(name, out vk)) return true;
        if (name.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(name.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out vk) && vk is > 0 and < 256)
            return true;
        vk = 0;
        return false;
    }

    public static bool IsKnown(int vk) => Names.ContainsKey(vk);
}
