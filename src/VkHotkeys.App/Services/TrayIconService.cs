using System.Windows;
using VkHotkeys.Core.Hotkeys;
using Forms = System.Windows.Forms;

namespace VkHotkeys.App.Services;

/// <summary>Значок в системном трее с меню управления.</summary>
public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;

    public event Action? ShowRequested;
    public event Action? ExitRequested;
    public event Action<HotkeyAction>? CommandRequested;

    public TrayIconService()
    {
        var menu = new Forms.ContextMenuStrip
        {
            ShowImageMargin = false,
            BackColor = System.Drawing.Color.FromArgb(0x23, 0x26, 0x2B),
            ForeColor = System.Drawing.Color.FromArgb(0xE7, 0xE9, 0xEC),
            RenderMode = Forms.ToolStripRenderMode.System,
        };
        menu.Items.Add("Открыть VK Hotkeys", null, (_, _) => ShowRequested?.Invoke());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("⏮  Предыдущий трек", null, (_, _) => CommandRequested?.Invoke(HotkeyAction.PreviousTrack));
        menu.Items.Add("⏯  Пауза / воспроизведение", null, (_, _) => CommandRequested?.Invoke(HotkeyAction.PlayPause));
        menu.Items.Add("⏭  Следующий трек", null, (_, _) => CommandRequested?.Invoke(HotkeyAction.NextTrack));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitRequested?.Invoke());

        _icon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "VK Hotkeys",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) ShowRequested?.Invoke();
        };
    }

    private static System.Drawing.Icon LoadIcon()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (info is not null)
            {
                using var stream = info.Stream;
                return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
            }
        }
        catch (Exception ex)
        {
            Log.Write($"tray icon: {ex.Message}");
        }
        return System.Drawing.SystemIcons.Application;
    }

    public void SetTooltip(string text)
    {
        // Ограничение Windows — 127 символов.
        _icon.Text = text.Length > 127 ? text[..126] + "…" : text;
    }

    public void ShowBalloon(string title, string text, bool error = false)
    {
        _icon.ShowBalloonTip(3000, title, string.IsNullOrWhiteSpace(text) ? " " : text,
            error ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.None);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }
}
