using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VkHotkeys.Core.Hotkeys;

namespace VkHotkeys.App.Controls;

/// <summary>
/// Поле для записи комбинации: кликните и нажмите клавиши.
/// Esc — отмена, Backspace — очистить. Пока поле активно, глобальные горячие клавиши отключены
/// (событие <see cref="CaptureChanged"/>), чтобы F10 не переключил трек во время настройки.
/// </summary>
public sealed class HotkeyBox : Control
{
    public static event Action<bool>? CaptureChanged;

    public static readonly DependencyProperty GestureProperty = DependencyProperty.Register(
        nameof(Gesture), typeof(HotkeyGesture), typeof(HotkeyBox),
        new FrameworkPropertyMetadata(HotkeyGesture.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((HotkeyBox)d).Refresh()));

    private static readonly DependencyPropertyKey DisplayTextKey = DependencyProperty.RegisterReadOnly(
        nameof(DisplayText), typeof(string), typeof(HotkeyBox), new PropertyMetadata("Не назначено"));
    public static readonly DependencyProperty DisplayTextProperty = DisplayTextKey.DependencyProperty;

    private static readonly DependencyPropertyKey IsCapturingKey = DependencyProperty.RegisterReadOnly(
        nameof(IsCapturing), typeof(bool), typeof(HotkeyBox), new PropertyMetadata(false));
    public static readonly DependencyProperty IsCapturingProperty = IsCapturingKey.DependencyProperty;

    private static readonly DependencyPropertyKey IsEmptyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsEmpty), typeof(bool), typeof(HotkeyBox), new PropertyMetadata(true));
    public static readonly DependencyProperty IsEmptyProperty = IsEmptyKey.DependencyProperty;

    static HotkeyBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(HotkeyBox), new FrameworkPropertyMetadata(typeof(HotkeyBox)));
        FocusableProperty.OverrideMetadata(typeof(HotkeyBox), new FrameworkPropertyMetadata(true));
    }

    public HotkeyBox() => Refresh();

    public HotkeyGesture Gesture
    {
        get => (HotkeyGesture)GetValue(GestureProperty);
        set => SetValue(GestureProperty, value);
    }

    public string DisplayText => (string)GetValue(DisplayTextProperty);
    public bool IsCapturing => (bool)GetValue(IsCapturingProperty);
    public bool IsEmpty => (bool)GetValue(IsEmptyProperty);

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        e.Handled = true;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        SetValue(IsCapturingKey, true);
        SetValue(DisplayTextKey, "Нажмите комбинацию…");
        CaptureChanged?.Invoke(true);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        SetValue(IsCapturingKey, false);
        Refresh();
        CaptureChanged?.Invoke(false);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key,
        };
        var mods = ToModifiers(Keyboard.Modifiers);

        if (key == Key.Tab && mods == HotkeyModifiers.None)
            return; // обычная навигация

        e.Handled = true;

        if (key == Key.Escape && mods == HotkeyModifiers.None)
        {
            EndCapture();
            return;
        }
        if (key == Key.Back && mods == HotkeyModifiers.None)
        {
            Gesture = HotkeyGesture.Empty;
            EndCapture();
            return;
        }

        var vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return;

        if (VirtualKeys.IsModifier(vk))
        {
            // Показываем промежуточное состояние: «Ctrl + Alt + …»
            var partial = new HotkeyGesture(1, mods).ToString();
            var cut = partial.LastIndexOf('+');
            SetValue(DisplayTextKey, cut > 0 ? partial[..cut] + "+ …" : "Нажмите комбинацию…");
            return;
        }

        Gesture = new HotkeyGesture(vk, mods);
        EndCapture();
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        // PrintScreen приходит в WPF только как KeyUp.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Snapshot)
        {
            Gesture = new HotkeyGesture(VirtualKeys.PrintScreen, ToModifiers(Keyboard.Modifiers));
            e.Handled = true;
            EndCapture();
            return;
        }
        if (IsCapturing && Keyboard.Modifiers == ModifierKeys.None)
            SetValue(DisplayTextKey, "Нажмите комбинацию…");
    }

    private void EndCapture()
    {
        // Убираем фокус на родительский элемент, чтобы следующее нажатие снова работало как хоткей.
        var scope = FocusManager.GetFocusScope(this);
        FocusManager.SetFocusedElement(scope, null);
        Keyboard.ClearFocus();
    }

    private void Refresh()
    {
        if (IsCapturing) return;
        var g = Gesture;
        SetValue(IsEmptyKey, g.IsEmpty);
        SetValue(DisplayTextKey, g.IsEmpty ? "Не назначено" : g.ToString());
    }

    private static HotkeyModifiers ToModifiers(ModifierKeys keys)
    {
        var m = HotkeyModifiers.None;
        if (keys.HasFlag(ModifierKeys.Control)) m |= HotkeyModifiers.Control;
        if (keys.HasFlag(ModifierKeys.Alt)) m |= HotkeyModifiers.Alt;
        if (keys.HasFlag(ModifierKeys.Shift)) m |= HotkeyModifiers.Shift;
        if (keys.HasFlag(ModifierKeys.Windows)) m |= HotkeyModifiers.Win;
        return m;
    }
}
