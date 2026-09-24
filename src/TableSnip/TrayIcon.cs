using System.Windows.Forms;

namespace TableSnip;

/// <summary>
/// System tray icon with a small menu. It lets TableSnip keep running (and keep its global
/// hotkey registered) after the window is closed.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu;

    public event Action? OpenRequested;
    public event Action? SnipRequested;
    public event Action? QuitRequested;

    public TrayIcon(System.Drawing.Icon icon, string? hotkeyText)
    {
        _menu = new ContextMenuStrip();
        var snip = _menu.Items.Add(hotkeyText != null ? $"Snip a table\t{hotkeyText}" : "Snip a table", null, (_, _) => SnipRequested?.Invoke());
        snip.Font = new System.Drawing.Font(snip.Font, System.Drawing.FontStyle.Bold);
        _menu.Items.Add("Open TableSnip", null, (_, _) => OpenRequested?.Invoke());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Quit TableSnip", null, (_, _) => QuitRequested?.Invoke());

        _icon = new NotifyIcon
        {
            Icon = icon,
            Text = "TableSnip",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) OpenRequested?.Invoke();
        };
    }

    public void ShowBalloon(string title, string text) =>
        _icon.ShowBalloonTip(5000, title, text, ToolTipIcon.None);

    /// <summary>Swap the glyph, e.g. when the taskbar flips between light and dark.</summary>
    public void SetIcon(System.Drawing.Icon icon)
    {
        var old = _icon.Icon;
        _icon.Icon = icon;
        old?.Dispose();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }
}
