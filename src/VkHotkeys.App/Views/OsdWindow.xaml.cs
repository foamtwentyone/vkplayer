using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using static VkHotkeys.App.Interop.NativeMethods;

namespace VkHotkeys.App.Views;

/// <summary>
/// Всплывающая плашка поверх всех окон. Не активируется, не перехватывает мышь и не отбирает фокус у игры:
/// WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW.
/// Видна поверх игр в режиме «Окно без рамки» и (в Windows 10/11) в большинстве полноэкранных режимов.
/// </summary>
public partial class OsdWindow : Window
{
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromSeconds(2.6) };
    private IntPtr _hwnd;

    public OsdWindow()
    {
        InitializeComponent();
        _hideTimer.Tick += (_, _) => FadeOut();
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            var ex = (long)GetWindowLongPtr(_hwnd, GWL_EXSTYLE);
            ex |= WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED;
            SetWindowLongPtr(_hwnd, GWL_EXSTYLE, new IntPtr(ex));
        };
    }

    public void ShowTrack(string title, string? artist) =>
        Present("", title, artist, null, accent: true);

    public void ShowVolume(int percent, bool muted) =>
        Present(muted || percent == 0 ? "" : "", muted ? "Звук выключен" : $"Громкость {percent}%",
            "Громкость плеера VK", muted ? 0 : percent, accent: true);

    public void ShowMessage(string glyph, string title, string? subtitle, bool error) =>
        Present(glyph, title, subtitle, null, accent: !error);

    private void Present(string glyph, string title, string? subtitle, int? bar, bool accent)
    {
        Glyph.Text = glyph;
        TitleText.Text = title;
        SubtitleText.Text = subtitle ?? "";
        SubtitleText.Visibility = string.IsNullOrEmpty(subtitle) ? Visibility.Collapsed : Visibility.Visible;
        IconBack.Background = accent
            ? (Brush)FindResource("AccentBrush")
            : new SolidColorBrush(Color.FromRgb(0xE0, 0x4F, 0x4F));
        if (bar is { } b)
        {
            Bar.Value = b;
            Bar.Visibility = Visibility.Visible;
        }
        else
        {
            Bar.Visibility = Visibility.Collapsed;
        }

        if (!IsVisible) Show();
        UpdateLayout();
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 16;
        Top = area.Top + 16;
        if (_hwnd != IntPtr.Zero)
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);

        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void FadeOut()
    {
        _hideTimer.Stop();
        var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(350));
        anim.Completed += (_, _) =>
        {
            if (!_hideTimer.IsEnabled && Opacity == 0) Hide();
        };
        BeginAnimation(OpacityProperty, anim);
    }
}
