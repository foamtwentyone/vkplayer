namespace VkHotkeys.App.Services;

/// <summary>Простой журнал в %LOCALAPPDATA%\VkHotkeys\log.txt (ограничен ~1 МБ).</summary>
public static class Log
{
    private static readonly object Gate = new();

    public static string Directory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VkHotkeys");

    public static string FilePath { get; } = Path.Combine(Directory, "log.txt");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 1024 * 1024)
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // журнал не должен ронять программу
        }
    }
}
