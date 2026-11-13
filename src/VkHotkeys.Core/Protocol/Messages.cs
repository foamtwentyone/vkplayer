using System.Text.Json;
using System.Text.Json.Serialization;

namespace VkHotkeys.Core.Protocol;

/// <summary>
/// Типы сообщений протокола между расширением браузера и Windows-приложением.
/// Формат кадра одинаковый на всём пути: 4 байта длины (little-endian) + UTF-8 JSON
/// (это формат Chrome Native Messaging; мост пересылает кадры в именованный канал без изменений).
/// </summary>
public static class MessageTypes
{
    // Расширение -> приложение
    public const string Hello = "hello";
    public const string State = "state";
    public const string Result = "result";
    public const string Pong = "pong";

    // Приложение -> расширение
    public const string Command = "command";
    public const string Ping = "ping";

    // Мост -> расширение (служебное)
    public const string AppStatus = "appStatus";
}

/// <summary>Действия, которые понимает расширение.</summary>
public static class PlayerActions
{
    public const string Next = "next";
    public const string Previous = "prev";
    public const string Toggle = "toggle";
    public const string Play = "play";
    public const string Pause = "pause";
    public const string Seek = "seek";          // value = смещение в секундах (+/-)
    public const string SetVolume = "setVolume"; // value = 0..1
}

/// <summary>Коды ошибок, которые возвращает расширение.</summary>
public static class ErrorCodes
{
    public const string NoVkTab = "no_vk_tab";
    public const string PlayerNotFound = "player_not_found";
    public const string NoTrack = "no_track";
    public const string NoEffect = "no_effect";
    public const string VolumeUnavailable = "volume_unavailable";
    public const string SeekUnavailable = "seek_unavailable";
    public const string TabDisconnected = "tab_disconnected";
    public const string Timeout = "timeout";
    public const string NoBrowser = "no_browser";
    public const string AutoplayBlocked = "autoplay_blocked";
    public const string Internal = "internal";

    public static string Describe(string? code) => code switch
    {
        NoVkTab => "Вкладка VK не открыта",
        PlayerNotFound => "Плеер VK не найден на странице",
        NoTrack => "Трек не выбран — запустите музыку в VK",
        NoEffect => "Плеер VK не отреагировал на команду",
        VolumeUnavailable => "Громкость плеера недоступна",
        SeekUnavailable => "Перемотка недоступна для этого трека",
        TabDisconnected => "Вкладка VK была закрыта",
        Timeout => "Браузер не ответил вовремя",
        NoBrowser => "Браузер с расширением не подключён",
        AutoplayBlocked => "Браузер запретил автозапуск — кликните один раз на странице VK",
        null or "" => "Неизвестная ошибка",
        _ => $"Ошибка: {code}",
    };
}

/// <summary>Снимок состояния плеера VK (из выбранной вкладки).</summary>
public sealed class PlayerSnapshot
{
    /// <summary>Количество открытых вкладок VK в этом браузере.</summary>
    public int VkTabs { get; set; }
    public int? TabId { get; set; }
    public bool PlayerFound { get; set; }
    public bool HasTrack { get; set; }
    public bool Playing { get; set; }
    public string? Title { get; set; }
    public string? Artist { get; set; }
    /// <summary>Громкость плеера VK 0..1 (null — недоступна).</summary>
    public double? Volume { get; set; }
    public double? Position { get; set; }
    public double? Duration { get; set; }
    public bool CanSeek { get; set; }
    public string? Strategy { get; set; }
    /// <summary>Unix-время (мс) последнего воспроизведения в этой вкладке.</summary>
    public long LastPlayingAt { get; set; }
}

/// <summary>Сообщение от расширения (или моста).</summary>
public sealed class IncomingMessage
{
    public string Type { get; set; } = "";
    public string? Id { get; set; }
    public bool? Ok { get; set; }
    public string? Error { get; set; }
    public string? Browser { get; set; }
    public string? Version { get; set; }
    public PlayerSnapshot? State { get; set; }
    public bool? Connected { get; set; }
}

/// <summary>Команда от приложения к расширению.</summary>
public sealed class OutgoingCommand
{
    public string Type { get; set; } = MessageTypes.Command;
    public string Id { get; set; } = "";
    public string? Action { get; set; }
    public double? Value { get; set; }
}

public static class ProtocolJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    public static IncomingMessage? DeserializeIncoming(ReadOnlySpan<byte> utf8)
    {
        try
        {
            return JsonSerializer.Deserialize<IncomingMessage>(utf8, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
