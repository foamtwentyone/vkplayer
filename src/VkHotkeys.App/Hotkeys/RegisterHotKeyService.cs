using System.Runtime.InteropServices;
using VkHotkeys.App.Services;
using VkHotkeys.Core.Hotkeys;
using static VkHotkeys.App.Interop.NativeMethods;

namespace VkHotkeys.App.Hotkeys;

/// <summary>
/// Альтернативный режим: стандартный RegisterHotKey. Нажатие не доходит до окон как WM_KEYDOWN,
/// но игры, читающие клавиатуру через Raw Input, могут всё равно его увидеть.
/// Работает в отдельном потоке: WM_HOTKEY приходит в очередь того потока, который зарегистрировал клавиши.
/// </summary>
public sealed class RegisterHotKeyService : IHotkeyService
{
    private const uint WM_APPLY = WM_APP + 2;
    private const int BaseId = 0x5100;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private readonly object _gate = new();
    private uint _threadId;
    private IReadOnlyDictionary<HotkeyAction, HotkeyGesture> _requested = new Dictionary<HotkeyAction, HotkeyGesture>();
    private TaskCompletionSource<IReadOnlyList<HotkeyIssue>>? _applyResult;
    private readonly Dictionary<int, HotkeyAction> _registered = new();
    private volatile bool _suspended;

    public event Action<HotkeyAction>? Triggered;
    public string Name => "RegisterHotKey";

    public RegisterHotKeyService()
    {
        _thread = new Thread(ThreadMain) { IsBackground = true, Name = "VkHotkeys RegisterHotKey", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    public bool Suspended
    {
        get => _suspended;
        set => _suspended = value;
    }

    public IReadOnlyList<HotkeyIssue> Apply(IReadOnlyDictionary<HotkeyAction, HotkeyGesture> bindings)
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<HotkeyIssue>>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _requested = new Dictionary<HotkeyAction, HotkeyGesture>(bindings);
            _applyResult = tcs;
        }
        PostThreadMessage(_threadId, WM_APPLY, IntPtr.Zero, IntPtr.Zero);
        return tcs.Task.Wait(TimeSpan.FromSeconds(3)) ? tcs.Task.Result : Array.Empty<HotkeyIssue>();
    }

    private void ThreadMain()
    {
        _threadId = GetCurrentThreadId();
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
        _ready.Set();

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            switch (msg.message)
            {
                case WM_APPLY:
                    ApplyOnThread();
                    break;
                case WM_HOTKEY:
                    if (!_suspended && _registered.TryGetValue((int)msg.wParam, out var action))
                        ThreadPool.QueueUserWorkItem(_ => Raise(action));
                    break;
            }
        }
        UnregisterAll();
    }

    private void ApplyOnThread()
    {
        IReadOnlyDictionary<HotkeyAction, HotkeyGesture> requested;
        TaskCompletionSource<IReadOnlyList<HotkeyIssue>>? tcs;
        lock (_gate)
        {
            requested = _requested;
            tcs = _applyResult;
            _applyResult = null;
        }

        UnregisterAll();
        var issues = new List<HotkeyIssue>();
        foreach (var (action, gesture) in requested)
        {
            if (gesture.IsEmpty) continue;
            var id = BaseId + (int)action;
            var mods = (int)gesture.Modifiers | (action.IsRepeatable() ? 0 : MOD_NOREPEAT);
            if (RegisterHotKey(IntPtr.Zero, id, mods, gesture.VirtualKey))
            {
                _registered[id] = action;
            }
            else
            {
                var err = Marshal.GetLastWin32Error();
                issues.Add(new HotkeyIssue(action, IssueSeverity.Error, err == ERROR_HOTKEY_ALREADY_REGISTERED
                    ? $"{gesture} уже занято другой программой"
                    : $"Не удалось зарегистрировать {gesture} (код {err})"));
            }
        }
        tcs?.TrySetResult(issues);
    }

    private void UnregisterAll()
    {
        foreach (var id in _registered.Keys) UnregisterHotKey(IntPtr.Zero, id);
        _registered.Clear();
    }

    private void Raise(HotkeyAction action)
    {
        try { Triggered?.Invoke(action); }
        catch (Exception ex) { Log.Write($"hotkey handler failed: {ex}"); }
    }

    public void Dispose()
    {
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
        _ready.Dispose();
    }
}
