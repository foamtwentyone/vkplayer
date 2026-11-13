using VkHotkeys.Core.Hotkeys;

namespace VkHotkeys.Core.Tests;

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("F10", VirtualKeys.F10, HotkeyModifiers.None)]
    [InlineData("Ctrl + Alt + Up", VirtualKeys.Up, HotkeyModifiers.Control | HotkeyModifiers.Alt)]
    [InlineData("ctrl+alt+shift+right", VirtualKeys.Right, HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift)]
    [InlineData("Win + Num5", VirtualKeys.NumPad0 + 5, HotkeyModifiers.Win)]
    public void Parses(string text, int vk, HotkeyModifiers mods)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var g));
        Assert.Equal(new HotkeyGesture(vk, mods), g);
    }

    [Theory]
    [InlineData("Ctrl + Alt")]
    [InlineData("F9 + F10")]
    [InlineData("Ctrl + Nope")]
    public void RejectsInvalid(string text) => Assert.False(HotkeyGesture.TryParse(text, out _));

    [Fact]
    public void EmptyStringIsEmptyGesture()
    {
        Assert.True(HotkeyGesture.TryParse("", out var g));
        Assert.True(g.IsEmpty);
    }

    [Fact]
    public void RoundTripsAllDefaults()
    {
        foreach (var g in HotkeyActionInfo.Defaults.Values)
            Assert.Equal(g, HotkeyGesture.Parse(g.ToString()));
    }

    [Fact]
    public void DefaultsMatchSpecification()
    {
        var d = HotkeyActionInfo.Defaults;
        Assert.Equal("F8", d[HotkeyAction.PlayPause].ToString());
        Assert.Equal("F9", d[HotkeyAction.PreviousTrack].ToString());
        Assert.Equal("F10", d[HotkeyAction.NextTrack].ToString());
        Assert.Equal("Ctrl + Alt + Up", d[HotkeyAction.VolumeUp].ToString());
        Assert.Equal("Ctrl + Alt + Down", d[HotkeyAction.VolumeDown].ToString());
        Assert.Equal("Ctrl + Alt + M", d[HotkeyAction.ToggleMute].ToString());
        Assert.Equal("Ctrl + Alt + Shift + Right", d[HotkeyAction.SeekForward].ToString());
        Assert.Equal("Ctrl + Alt + Shift + Left", d[HotkeyAction.SeekBackward].ToString());
    }
}

public class ConflictCheckerTests
{
    [Fact]
    public void DefaultsHaveNoErrors()
    {
        var issues = HotkeyConflictChecker.Check(HotkeyActionInfo.Defaults);
        Assert.False(HotkeyConflictChecker.HasErrors(issues));
    }

    [Fact]
    public void DetectsDuplicates()
    {
        var b = new Dictionary<HotkeyAction, HotkeyGesture>(HotkeyActionInfo.Defaults)
        {
            [HotkeyAction.PlayPause] = new(VirtualKeys.F10, HotkeyModifiers.None),
        };
        var issues = HotkeyConflictChecker.Check(b);
        Assert.Contains(issues, i => i.Action == HotkeyAction.PlayPause && i.Severity == IssueSeverity.Error);
        Assert.Contains(issues, i => i.Action == HotkeyAction.NextTrack && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void EmptyBindingsDoNotConflict()
    {
        var b = HotkeyActionInfo.All.ToDictionary(a => a, _ => HotkeyGesture.Empty);
        Assert.Empty(HotkeyConflictChecker.Check(b));
    }

    [Theory]
    [InlineData(VirtualKeys.A, HotkeyModifiers.None, IssueSeverity.Error)]           // сломает набор текста
    [InlineData(VirtualKeys.Delete, HotkeyModifiers.Control | HotkeyModifiers.Alt, IssueSeverity.Error)]
    [InlineData(VirtualKeys.F12, HotkeyModifiers.None, IssueSeverity.Warning)]      // скриншот Steam
    [InlineData(VirtualKeys.Up, HotkeyModifiers.None, IssueSeverity.Warning)]
    public void FlagsProblematicKeys(int vk, HotkeyModifiers mods, IssueSeverity severity)
    {
        var issue = HotkeyConflictChecker.CheckSingle(new HotkeyGesture(vk, mods));
        Assert.NotNull(issue);
        Assert.Equal(severity, issue!.Severity);
    }

    [Fact]
    public void FunctionKeysAreFine() =>
        Assert.Null(HotkeyConflictChecker.CheckSingle(new HotkeyGesture(VirtualKeys.F9, HotkeyModifiers.None)));
}

public class HotkeyMatcherTests
{
    private static HotkeyMatcher Create()
    {
        var m = new HotkeyMatcher();
        m.SetBindings(HotkeyActionInfo.Defaults);
        return m;
    }

    [Fact]
    public void F10FiresNextAndSwallowsBothDownAndUp()
    {
        var m = Create();
        var down = m.OnKeyDown(VirtualKeys.F10, HotkeyModifiers.None);
        Assert.True(down.Swallow);
        Assert.Equal(HotkeyAction.NextTrack, down.Fire);
        var up = m.OnKeyUp(VirtualKeys.F10);
        Assert.True(up.Swallow);
        Assert.Null(up.Fire);
    }

    [Fact]
    public void HoldingF10DoesNotSkipMultipleTracks()
    {
        var m = Create();
        Assert.Equal(HotkeyAction.NextTrack, m.OnKeyDown(VirtualKeys.F10, HotkeyModifiers.None).Fire);
        for (var i = 0; i < 10; i++)
        {
            var repeat = m.OnKeyDown(VirtualKeys.F10, HotkeyModifiers.None); // автоповтор
            Assert.True(repeat.Swallow);
            Assert.Null(repeat.Fire);
        }
        m.OnKeyUp(VirtualKeys.F10);
        Assert.Equal(HotkeyAction.NextTrack, m.OnKeyDown(VirtualKeys.F10, HotkeyModifiers.None).Fire);
    }

    [Fact]
    public void HoldingVolumeRepeats()
    {
        var m = Create();
        var mods = HotkeyModifiers.Control | HotkeyModifiers.Alt;
        Assert.Equal(HotkeyAction.VolumeUp, m.OnKeyDown(VirtualKeys.Up, mods).Fire);
        Assert.Equal(HotkeyAction.VolumeUp, m.OnKeyDown(VirtualKeys.Up, mods).Fire);
    }

    [Fact]
    public void ModifiersMustMatchExactly()
    {
        var m = Create();
        var d = m.OnKeyDown(VirtualKeys.F9, HotkeyModifiers.Shift);
        Assert.False(d.Swallow);
        Assert.Null(d.Fire);
        Assert.False(m.OnKeyUp(VirtualKeys.F9).Swallow); // чужое отпускание не трогаем
    }

    [Fact]
    public void OtherKeysPassThrough()
    {
        var m = Create();
        Assert.Equal(default, m.OnKeyDown(VirtualKeys.A, HotkeyModifiers.None));
        Assert.Equal(default, m.OnKeyDown(VirtualKeys.LControl, HotkeyModifiers.Control));
        Assert.Equal(default, m.OnKeyUp(VirtualKeys.A));
    }

    [Fact]
    public void ReleasingModifierFirstStillSwallowsKeyUp()
    {
        var m = Create();
        var mods = HotkeyModifiers.Control | HotkeyModifiers.Alt;
        Assert.Equal(HotkeyAction.ToggleMute, m.OnKeyDown(VirtualKeys.M, mods).Fire);
        Assert.False(m.OnKeyUp(VirtualKeys.LControl).Swallow);
        Assert.True(m.OnKeyUp(VirtualKeys.M).Swallow);
    }
}
