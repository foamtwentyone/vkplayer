using System.Text.Json;
using System.Text.Json.Serialization;
using VkHotkeys.Core.Hotkeys;

namespace VkHotkeys.Core.Settings;

/// <summary>Хранение настроек в %APPDATA%\VkHotkeys\settings.json с атомарной записью.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // Файл читаемый и редактируемый вручную: "Ctrl + Alt + Up", а не \u002B
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new HotkeyGestureJsonConverter() },
    };

    public string FilePath { get; }

    public SettingsStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VkHotkeys", "settings.json");
    }

    /// <summary>Загружает настройки; при отсутствии или повреждении файла — значения по умолчанию.</summary>
    public AppSettings Load()
    {
        AppSettings? settings = null;
        try
        {
            if (File.Exists(FilePath))
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Повреждённый файл не должен ломать запуск: сохраняем копию и начинаем с умолчаний.
            TryBackupCorrupted();
        }

        settings ??= new AppSettings();
        settings.Normalize();
        return settings;
    }

    public void Save(AppSettings settings)
    {
        settings.Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
        File.Move(tmp, FilePath, overwrite: true);
    }

    private void TryBackupCorrupted()
    {
        try { File.Copy(FilePath, FilePath + ".corrupted", overwrite: true); } catch { /* не критично */ }
    }
}

/// <summary>Хранит комбинацию в читаемом виде: "Ctrl + Alt + Up".</summary>
public sealed class HotkeyGestureJsonConverter : JsonConverter<HotkeyGesture>
{
    public override HotkeyGesture Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return HotkeyGesture.Empty;
        var text = reader.GetString();
        return HotkeyGesture.TryParse(text, out var g) ? g : throw new JsonException($"Неверная комбинация «{text}»");
    }

    public override void Write(Utf8JsonWriter writer, HotkeyGesture value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
