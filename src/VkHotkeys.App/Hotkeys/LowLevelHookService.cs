using System.Runtime.InteropServices;
using VkHotkeys.App.Interop;
using VkHotkeys.App.Services;
using VkHotkeys.Core.Hotkeys;
using static VkHotkeys.App.Interop.NativeMethods;

namespace VkHotkeys.App.Hotkeys;

/// <summary>
/// Глобальные горячие клавиши через низкоуровневый хук клавиатуры WH_KEYBOARD_LL.
///
/// Почему хук, а не RegisterHotKey: хук вызывается раньше, чем нажатие попадёт в любое окно и в Raw Input,
/// поэтому, вернув 1, мы гарантированно не пропускаем F8/F9/F10 в Dota 2 (игра их вообще не видит).
///
/// Надёжность:
/// * хук живёт в отдельном потоке с собственным циклом сообщений и повышенным приоритетом —
///   подвисание интерфейса не влияет на обработку клавиш;
/// * обработчик ничего не ждёт: действие уходит в пул потоков, хук сразу возвращает управление
///   (Windows молча снимает «медленные» хуки — LowLevelHooksTimeout);
/// * сторож раз в 30 с переустанавливает хук на случай, если Windows его всё-таки сняла.
/// </summary>
public sealed class LowLevelHookService : IHotkeyService
{
    private const uint WM_REINSTALL = WM_APP + 1;

    private readonly HotkeyMatcher _matcher = new();
    private readonly LowLevelKeyboardProc _proc;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private readonly Timer _watchdog;
    private uint _threadId;
    private IntPtr _hook;
    private volatile bool _suspended;
    private volatile bool _disposed;

    public event Action<HotkeyAction>? Triggered;
    public string Name => "низкоуровневый хук";

    public LowLevelHookService()
    {
        _proc = HookCallback; // держим ссылку, чтобы GC не собрал делегат
        _thread = new Thread(ThreadMain)
        {
            IsBackground = true,
            Name = "VkHotkeys keyboard hook",
            Priority = ThreadPriority.Highest,
        };
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
        _watchdog = new Timer(_ => PostThreadMessage(_threadId, WM_REINSTALL, IntPtr.Zero, IntPtr.Zero),
            null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public bool Suspended
    {
        get => _suspended;
        set => _suspended = value;
    }

    public IReadOnlyList<HotkeyIssue> Apply(IReadOnlyDictionary<HotkeyAction, HotkeyGesture> bindings)
    {
        _matcher.SetBindings(bindings);
        if (_hook == IntPtr.Zero)
        {
            return bindings.Where(b => !b.Value.IsEmpty)
                .Select(b => new HotkeyIssue(b.Key, IssueSeverity.Error, "Не удалось установить перехват клавиатуры"))
                .ToList();
        }
        return Array.Empty<HotkeyIssue>();
    }

    private void ThreadMain()
    {
        _threadId = GetCurrentThreadId();
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0); // создаём очередь сообщений потока
        Install();
        _ready.Set();

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_REINSTALL)
            {
                Install();
                continue;
            }
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    /// <summary>Сначала ставим новый хук, потом снимаем старый — без «окна», в котором клавиши не ловятся.</summary>
    private void Install()
    {
        if (_disposed) return;
        var newHook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (newHook == IntPtr.Zero)
        {
            Log.Write($"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");
            return;
        }
        var old = _hook;
        _hook = newHook;
        if (old != IntPtr.Zero) UnhookWindowsHookEx(old);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !_suspended)
        {
            // KBDLLHOOKSTRUCT: vkCode @0, scanCode @4, flags @8 — читаем без аллокаций.
            var vk = Marshal.ReadInt32(lParam, 0);
            var flags = Marshal.ReadInt32(lParam, 8);
            var message = (int)wParam;

            if (!(vk == VK_MASK && (flags & LLKHF_INJECTED) != 0))
            {
                KeyEventDecision decision = default;
                if (message is WM_KEYDOWN or WM_SYSKEYDOWN)
                {
                    var mods = CurrentModifiers();
                    decision = _matcher.OnKeyDown(vk, mods);
                    if (decision.Fire is not null && (mods & (HotkeyModifiers.Win | HotkeyModifiers.Alt)) != 0
                        && (mods & HotkeyModifiers.Control) == 0)
                        SendMaskKey();
                }
                else if (message is WM_KEYUP or WM_SYSKEYUP)
                {
                    decision = _matcher.OnKeyUp(vk);
                }

                if (decision.Fire is { } action)
                    ThreadPool.UnsafeQueueUserWorkItem(static s => s.Item1.Raise(s.Item2), (this, action), false);

                if (decision.Swallow) return 1;
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void Raise(HotkeyAction action)
    {
        try
        {
            Triggered?.Invoke(action);
        }
        catch (Exception ex)
        {
            Log.Write($"hotkey handler failed: {ex}");
        }
    }

    private static HotkeyModifiers CurrentModifiers()
    {
        var m = HotkeyModifiers.None;
        if (IsDown(VirtualKeys.Control)) m |= HotkeyModifiers.Control;
        if (IsDown(VirtualKeys.Menu)) m |= HotkeyModifiers.Alt;
        if (IsDown(VirtualKeys.Shift)) m |= HotkeyModifiers.Shift;
        if (IsDown(VirtualKeys.LWin) || IsDown(VirtualKeys.RWin)) m |= HotkeyModifiers.Win;
        return m;
    }

    /// <summary>
    /// Если проглотить клавишу в комбинации Win+X или Alt+X, то последующее отпускание Win/Alt
    /// Windows воспримет как одиночное нажатие (меню Пуск / строка меню). Маскирующее нажатие это предотвращает.
    /// </summary>
    private static void SendMaskKey()
    {
        var inputs = new INPUT[2];
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = VK_MASK;
        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].u.ki.wVk = VK_MASK;
        inputs[1].u.ki.dwFlags = KEYEVENTF_KEYUP;
        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watchdog.Dispose();
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
        _ready.Dispose();
    }
}
