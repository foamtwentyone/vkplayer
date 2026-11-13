using VkHotkeys.Core.Hotkeys;
using VkHotkeys.Core.Settings;

namespace VkHotkeys.Core.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vkhk-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "settings.json");

    [Fact]
    public void MissingFileGivesDefaults()
    {
        var s = new SettingsStore(FilePath).Load();
        Assert.Equal(HotkeyActionInfo.Defaults.Count, s.Hotkeys.Count);
        Assert.Equal(HotkeyBackend.LowLevelHook, s.Backend);
    }

    [Fact]
    public void RoundTrip()
    {
        var store = new SettingsStore(FilePath);
        var s = store.Load();
        s.Hotkeys[HotkeyAction.NextTrack] = new HotkeyGesture(VirtualKeys.PageDown, HotkeyModifiers.Control);
        s.Hotkeys[HotkeyAction.SeekBackward] = HotkeyGesture.Empty;
        s.VolumeStep = 7;
        s.StartWithWindows = true;
        s.Notifications = NotificationMode.Windows;
        store.Save(s);

        var json = File.ReadAllText(FilePath);
        Assert.Contains("\"Ctrl + PageDown\"", json);

        var loaded = new SettingsStore(FilePath).Load();
        Assert.Equal(new HotkeyGesture(VirtualKeys.PageDown, HotkeyModifiers.Control), loaded.Hotkeys[HotkeyAction.NextTrack]);
        Assert.True(loaded.Hotkeys[HotkeyAction.SeekBackward].IsEmpty); // очищенный бинд не возвращается к умолчанию
        Assert.Equal(7, loaded.VolumeStep);
        Assert.True(loaded.StartWithWindows);
        Assert.Equal(NotificationMode.Windows, loaded.Notifications);
    }

    [Fact]
    public void CorruptedFileFallsBackToDefaultsAndKeepsBackup()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ это не json");
        var s = new SettingsStore(FilePath).Load();
        Assert.Equal(HotkeyActionInfo.Defaults[HotkeyAction.NextTrack], s.Hotkeys[HotkeyAction.NextTrack]);
        Assert.True(File.Exists(FilePath + ".corrupted"));
    }

    [Fact]
    public void NormalizeClampsAndFillsMissing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{ "volumeStep": 500, "seekStepSeconds": -3, "hotkeys": { "PlayPause": "Ctrl + P" } }""");
        var s = new SettingsStore(FilePath).Load();
        Assert.Equal(25, s.VolumeStep);
        Assert.Equal(1, s.SeekStepSeconds);
        Assert.Equal("Ctrl + P", s.Hotkeys[HotkeyAction.PlayPause].ToString());
        Assert.Equal(HotkeyActionInfo.Defaults[HotkeyAction.NextTrack], s.Hotkeys[HotkeyAction.NextTrack]);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
