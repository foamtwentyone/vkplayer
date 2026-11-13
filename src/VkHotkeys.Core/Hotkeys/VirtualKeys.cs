namespace VkHotkeys.Core.Hotkeys;

/// <summary>Виртуальные коды клавиш Windows (VK_*), нужные в логике программы.</summary>
public static class VirtualKeys
{
    public const int Back = 0x08, Tab = 0x09, Enter = 0x0D, Pause = 0x13, CapsLock = 0x14, Escape = 0x1B, Space = 0x20;
    public const int PageUp = 0x21, PageDown = 0x22, End = 0x23, Home = 0x24;
    public const int Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28;
    public const int PrintScreen = 0x2C, Insert = 0x2D, Delete = 0x2E;
    public const int D0 = 0x30, D9 = 0x39, A = 0x41, M = 0x4D, Z = 0x5A;
    public const int LWin = 0x5B, RWin = 0x5C, Apps = 0x5D;
    public const int NumPad0 = 0x60, NumPad9 = 0x69, Multiply = 0x6A, Add = 0x6B, Subtract = 0x6D, Decimal = 0x6E, Divide = 0x6F;
    public const int F1 = 0x70, F8 = 0x77, F9 = 0x78, F10 = 0x79, F12 = 0x7B, F24 = 0x87;
    public const int NumLock = 0x90, ScrollLock = 0x91;
    public const int Shift = 0x10, Control = 0x11, Menu = 0x12;
    public const int LShift = 0xA0, RShift = 0xA1, LControl = 0xA2, RControl = 0xA3, LMenu = 0xA4, RMenu = 0xA5;

    public static bool IsModifier(int vk) => vk is Shift or Control or Menu or LShift or RShift
        or LControl or RControl or LMenu or RMenu or LWin or RWin;

    public static bool IsFunctionKey(int vk) => vk is >= F1 and <= F24;
}
