using VkHotkeys.Core.Hotkeys;
using static VkHotkeys.App.Interop.NativeMethods;

namespace VkHotkeys.App.Hotkeys;

/// <summary>
/// Проверяет, не заняты ли комбинации другими программами (пробная регистрация RegisterHotKey).
/// В режиме хука это только предупреждение: хук всё равно получит клавишу первым.
/// </summary>
public static class HotkeyProbe
{
    private const int ProbeId = 0x5F00;

    public static bool IsTakenByOtherApp(HotkeyGesture gesture)
    {
        if (gesture.IsEmpty) return false;
        if (RegisterHotKey(IntPtr.Zero, ProbeId, (int)gesture.Modifiers | MOD_NOREPEAT, gesture.VirtualKey))
        {
            UnregisterHotKey(IntPtr.Zero, ProbeId);
            return false;
        }
        return System.Runtime.InteropServices.Marshal.GetLastWin32Error() == ERROR_HOTKEY_ALREADY_REGISTERED;
    }
}
