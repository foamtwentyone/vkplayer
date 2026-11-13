using System.Text.Json;
using Microsoft.Win32;
using VkHotkeys.Core.Ipc;

namespace VkHotkeys.App.Services;

/// <summary>
/// Регистрирует мост VkHotkeys.Bridge.exe как хост Native Messaging для Chromium-браузеров.
/// Пишет только в HKEY_CURRENT_USER — права администратора не нужны.
/// </summary>
public static class NativeHostRegistrar
{
    /// <summary>Ветки реестра, где браузеры ищут хосты Native Messaging (Opera использует ветку Chrome).</summary>
    private static readonly (string Browser, string Key)[] BrowserKeys =
    {
        ("Chrome", @"Software\Google\Chrome\NativeMessagingHosts"),
        ("Edge", @"Software\Microsoft\Edge\NativeMessagingHosts"),
        ("Chromium", @"Software\Chromium\NativeMessagingHosts"),
        ("Brave", @"Software\BraveSoftware\Brave-Browser\NativeMessagingHosts"),
        ("Vivaldi", @"Software\Vivaldi\NativeMessagingHosts"),
        ("Yandex", @"Software\Yandex\YandexBrowser\NativeMessagingHosts"),
    };

    public static string BridgePath => Path.Combine(AppContext.BaseDirectory, "VkHotkeys.Bridge.exe");

    public static string ManifestPath => Path.Combine(Log.Directory, PipeNames.NativeHostName + ".json");

    public sealed record Result(bool Success, string Message);

    public static Result Register(IEnumerable<string>? extraExtensionIds = null)
    {
        try
        {
            if (!File.Exists(BridgePath))
                return new(false, $"Не найден {Path.GetFileName(BridgePath)} рядом с программой");

            var origins = new[] { PipeNames.ExtensionId }
                .Concat(extraExtensionIds ?? Enumerable.Empty<string>())
                .Distinct()
                .Select(id => $"chrome-extension://{id}/")
                .ToArray();

            var manifest = new Dictionary<string, object>
            {
                ["name"] = PipeNames.NativeHostName,
                ["description"] = "VK Hotkeys bridge",
                ["path"] = BridgePath,
                ["type"] = "stdio",
                ["allowed_origins"] = origins,
            };

            Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath)!);
            var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            if (!File.Exists(ManifestPath) || File.ReadAllText(ManifestPath) != json)
                File.WriteAllText(ManifestPath, json);

            foreach (var (_, key) in BrowserKeys)
            {
                using var k = Registry.CurrentUser.CreateSubKey($@"{key}\{PipeNames.NativeHostName}");
                if (!string.Equals(k.GetValue(null) as string, ManifestPath, StringComparison.OrdinalIgnoreCase))
                    k.SetValue(null, ManifestPath);
            }
            return new(true, "Мост зарегистрирован для Chrome, Edge, Brave, Vivaldi, Opera, Яндекс");
        }
        catch (Exception ex)
        {
            Log.Write($"native host registration failed: {ex}");
            return new(false, $"Не удалось зарегистрировать мост: {ex.Message}");
        }
    }

    public static void Unregister()
    {
        foreach (var (_, key) in BrowserKeys)
        {
            try { Registry.CurrentUser.DeleteSubKeyTree($@"{key}\{PipeNames.NativeHostName}", throwOnMissingSubKey: false); }
            catch (Exception ex) { Log.Write($"unregister {key}: {ex.Message}"); }
        }
        try { File.Delete(ManifestPath); } catch { /* игнорируем */ }
    }
}
