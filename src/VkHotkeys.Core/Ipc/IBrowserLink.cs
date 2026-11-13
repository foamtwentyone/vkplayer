using VkHotkeys.Core.Protocol;

namespace VkHotkeys.Core.Ipc;

/// <summary>Подключение одного браузера (через мост Native Messaging).</summary>
public interface IBrowserLink
{
    Guid Id { get; }
    /// <summary>Имя браузера из hello-сообщения ("Chrome", "Edge", ...).</summary>
    string BrowserName { get; }
    Task SendAsync(OutgoingCommand command, CancellationToken ct = default);
}
