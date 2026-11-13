using VkHotkeys.Core.Protocol;

namespace VkHotkeys.Core.Player;

public enum LinkStatus
{
    /// <summary>Ни один браузер с расширением не подключён к приложению.</summary>
    WaitingForBrowser,
    /// <summary>Браузер подключён, но вкладок VK нет.</summary>
    NoVkTab,
    /// <summary>Вкладка VK есть, но плеер на ней не найден.</summary>
    PlayerNotFound,
    /// <summary>Плеер найден и готов к управлению.</summary>
    Ready,
}

public sealed record PlayerStatus(
    LinkStatus Status,
    string? BrowserName,
    int ConnectedBrowsers,
    PlayerSnapshot? Player,
    bool IsMuted)
{
    public static PlayerStatus Initial { get; } = new(LinkStatus.WaitingForBrowser, null, 0, null, false);

    public int? VolumePercent => Player?.Volume is { } v ? (int)Math.Round(Math.Clamp(v, 0, 1) * 100) : null;
    public bool IsPlaying => Status == LinkStatus.Ready && Player?.Playing == true;

    public string Describe() => Status switch
    {
        LinkStatus.WaitingForBrowser => "Браузер не подключён",
        LinkStatus.NoVkTab => $"{BrowserName}: откройте vk.com",
        LinkStatus.PlayerNotFound => $"{BrowserName}: плеер VK не найден",
        LinkStatus.Ready => $"Подключено · {BrowserName}",
        _ => Status.ToString(),
    };
}

public sealed record CommandOutcome(bool Success, string Message)
{
    public static CommandOutcome Ok(string message) => new(true, message);
    public static CommandOutcome Fail(string? errorCode) => new(false, ErrorCodes.Describe(errorCode));
}
