namespace VkHotkeys.App.Services;

/// <summary>Один экземпляр программы на пользователя; повторный запуск показывает уже открытое окно.</summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\VkHotkeys.SingleInstance";
    private const string EventName = @"Local\VkHotkeys.ShowWindow";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly RegisteredWaitHandle? _wait;

    public bool IsFirst { get; }

    public SingleInstance(Action onShowRequested)
    {
        _mutex = new Mutex(true, MutexName, out var created);
        IsFirst = created;
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        if (IsFirst)
        {
            _wait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => onShowRequested(), null,
                Timeout.Infinite, executeOnlyOnce: false);
        }
    }

    public void SignalFirstInstance() => _showEvent.Set();

    public void Dispose()
    {
        _wait?.Unregister(null);
        _showEvent.Dispose();
        if (IsFirst)
        {
            try { _mutex.ReleaseMutex(); } catch { /* игнорируем */ }
        }
        _mutex.Dispose();
    }
}
