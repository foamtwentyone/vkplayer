using System.Windows.Input;
using VkHotkeys.Core.Hotkeys;

namespace VkHotkeys.App.ViewModels;

public enum IssueLevel { None, Warning, Error }

/// <summary>Строка таблицы горячих клавиш.</summary>
public sealed class HotkeyRowViewModel : ObservableObject
{
    private readonly Action _changed;
    private HotkeyGesture _gesture;
    private string? _issue;
    private IssueLevel _level;

    public HotkeyRowViewModel(HotkeyAction action, HotkeyGesture gesture, Action changed)
    {
        Action = action;
        _gesture = gesture;
        _changed = changed;
        ClearCommand = new RelayCommand(() => Gesture = HotkeyGesture.Empty, () => !Gesture.IsEmpty);
        ResetCommand = new RelayCommand(() => Gesture = HotkeyActionInfo.Defaults[action],
            () => Gesture != HotkeyActionInfo.Defaults[action]);
    }

    public HotkeyAction Action { get; }
    public string Name => Action.DisplayName();
    public string DefaultText => $"По умолчанию: {HotkeyActionInfo.Defaults[Action]}";

    public HotkeyGesture Gesture
    {
        get => _gesture;
        set
        {
            if (Set(ref _gesture, value)) _changed();
        }
    }

    public string? Issue
    {
        get => _issue;
        private set
        {
            if (Set(ref _issue, value)) OnPropertyChanged(nameof(HasIssue));
        }
    }

    public IssueLevel Level
    {
        get => _level;
        private set => Set(ref _level, value);
    }

    public bool HasIssue => !string.IsNullOrEmpty(Issue);

    public void SetIssue(IssueLevel level, string? text)
    {
        Level = level;
        Issue = text;
    }

    public ICommand ClearCommand { get; }
    public ICommand ResetCommand { get; }
}
