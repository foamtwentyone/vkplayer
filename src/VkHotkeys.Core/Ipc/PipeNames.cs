namespace VkHotkeys.Core.Ipc;

public static class PipeNames
{
    /// <summary>Переменная окружения для переопределения имени канала (используется в тестах).</summary>
    public const string OverrideVariable = "VKHOTKEYS_PIPE";

    public static string Bridge =>
        Environment.GetEnvironmentVariable(OverrideVariable) is { Length: > 0 } custom
            ? custom
            : $"VkHotkeys.Bridge.v1.{Environment.UserName}";

    /// <summary>Имя хоста Native Messaging (должно совпадать с extension/background.js).</summary>
    public const string NativeHostName = "com.vkhotkeys.bridge";

    /// <summary>ID расширения: вычисляется из поля "key" в extension/manifest.json и одинаков в Chrome и Edge.</summary>
    public const string ExtensionId = "jpmdbkdhjihibaiicamanjichgkpgila";
}
