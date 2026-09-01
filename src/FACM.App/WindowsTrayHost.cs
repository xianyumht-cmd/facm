using System.Drawing;
using System.Windows.Forms;
using FACM.Core.Desktop;
using FACM.Core.Text;

namespace FACM.App;

/// <summary>
/// The one FACM native tray owner. It contains no product workflow; menu actions are supplied by App.
/// </summary>
internal sealed class WindowsTrayHost : IDisposable
{
    private const string IconResourceName = "FACM.Resources.FACM.ico";

    private readonly TrayCommandRouter _commands;
    private readonly ContextMenuStrip _menu;
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _icon;
    private bool _disposed;

    public WindowsTrayHost(
        IUiTextProvider text,
        IReadOnlyDictionary<TrayCommand, Action> commandHandlers)
    {
        ArgumentNullException.ThrowIfNull(text);
        _commands = new TrayCommandRouter(commandHandlers);
        _menu = BuildMenu(text);
        _icon = LoadIcon();
        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = LimitToolTip(text.Get(UiTextKeys.AppName)),
            ContextMenuStrip = _menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += OnDoubleClick;
    }

    public void ShowContextMenuAtCursor()
    {
        if (_disposed) return;
        try { _menu.Show(Cursor.Position); } catch { }
    }

    public void ShowBalloonTip(string title, string message)
    {
        if (_disposed) return;
        try
        {
            _notifyIcon.ShowBalloonTip(
                5000,
                LimitToolTip(title),
                LimitToolTip(message),
                ToolTipIcon.Info);
        }
        catch
        {
            // Notification is optional and may fail while Explorer is restarting or during shutdown.
        }
    }

    private ContextMenuStrip BuildMenu(IUiTextProvider text)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(CreateItem(text.Get(UiTextKeys.ControlCenter), TrayCommand.OpenCompactLauncher));
        menu.Items.Add(CreateItem(text.Get(UiTextKeys.Cleanup), TrayCommand.OpenCleanup));
        menu.Items.Add(CreateItem(text.Get(UiTextKeys.ShellLeague), TrayCommand.OpenLeague));

        var more = new ToolStripMenuItem(text.Get(UiTextKeys.TrayMore));
        more.DropDownItems.Add(CreateItem(text.Get(UiTextKeys.ThemeSettings), TrayCommand.OpenPersonalization));
        more.DropDownItems.Add(CreateItem(text.Get(UiTextKeys.DesktopPet), TrayCommand.OpenDesktopPetSettings));
        more.DropDownItems.Add(CreateItem(text.Get(UiTextKeys.TrayRestoreLauncher), TrayCommand.RestoreDefaultLauncher));
        more.DropDownItems.Add(CreateItem(text.Get(UiTextKeys.TrayResetDesktopPosition), TrayCommand.ResetDesktopPosition));
        more.DropDownItems.Add(CreateItem(text.Get(UiTextKeys.CheckUpdate), TrayCommand.CheckForUpdates));
        more.DropDownItems.Add(CreateItem(text.Get(UiTextKeys.OpenLog), TrayCommand.OpenLog));
        menu.Items.Add(more);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(CreateItem(text.Get(UiTextKeys.Exit), TrayCommand.Exit));
        return menu;
    }

    private ToolStripMenuItem CreateItem(string text, TrayCommand command)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => _commands.TryDispatch(command);
        return item;
    }

    private void OnDoubleClick(object? sender, EventArgs args) => _commands.TryDispatch(TrayCommand.OpenCompactLauncher);

    private static Icon LoadIcon()
    {
        try
        {
            using var stream = typeof(App).Assembly.GetManifestResourceStream(IconResourceName);
            if (stream is not null) return new Icon(stream);
        }
        catch
        {
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    private static string LimitToolTip(string? value)
    {
        var result = string.IsNullOrWhiteSpace(value) ? "GGman" : value.Trim();
        return result.Length <= 63 ? result : result[..63];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notifyIcon.DoubleClick -= OnDoubleClick;
        try { _notifyIcon.Visible = false; } catch { }
        _notifyIcon.ContextMenuStrip = null;
        _menu.Dispose();
        _notifyIcon.Dispose();
        _commands.Dispose();
        _icon.Dispose();
    }
}
