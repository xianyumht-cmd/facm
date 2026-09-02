using System.Drawing;
using System.Windows.Forms;
using FACM.Core.Desktop;
using FACM.Core.League;
using FACM.Core.Text;

namespace FACM.App;

/// <summary>
/// The one FACM native tray owner. It contains no product workflow; menu actions are supplied by App.
/// </summary>
internal sealed class WindowsTrayHost : IDisposable
{
    private const string ConnectedIconResourceName = "FACM.Resources.GGman.Tray.Connected.ico";
    private const string ConnectingIconResourceName = "FACM.Resources.GGman.Tray.Connecting.ico";
    private const string OfflineIconResourceName = "FACM.Resources.GGman.Tray.Offline.ico";

    private readonly TrayCommandRouter _commands;
    private readonly ContextMenuStrip _menu;
    private readonly NotifyIcon _notifyIcon;
    private readonly IReadOnlyDictionary<TrayLeagueStatus, Icon> _icons;
    private TrayLeagueStatus _status = TrayLeagueStatus.Offline;
    private bool _disposed;

    public WindowsTrayHost(
        IUiTextProvider text,
        IReadOnlyDictionary<TrayCommand, Action> commandHandlers)
    {
        ArgumentNullException.ThrowIfNull(text);
        _commands = new TrayCommandRouter(commandHandlers);
        _menu = BuildMenu(text);
        _icons = LoadIcons();
        _notifyIcon = new NotifyIcon
        {
            Icon = _icons[_status],
            Text = LimitToolTip(text.Get(UiTextKeys.AppName)),
            ContextMenuStrip = _menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += OnDoubleClick;
    }

    public void SetLeagueConnectionState(LeagueConnectionState connectionState)
    {
        if (_disposed) return;
        var next = connectionState switch
        {
            LeagueConnectionState.Connected => TrayLeagueStatus.Connected,
            LeagueConnectionState.Connecting => TrayLeagueStatus.Connecting,
            LeagueConnectionState.Unavailable => TrayLeagueStatus.Connecting,
            _ => TrayLeagueStatus.Offline
        };
        if (next == _status) return;
        _status = next;
        try { _notifyIcon.Icon = _icons[next]; } catch { }
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

    private static IReadOnlyDictionary<TrayLeagueStatus, Icon> LoadIcons()
    {
        return new Dictionary<TrayLeagueStatus, Icon>
        {
            [TrayLeagueStatus.Connected] = LoadIcon(ConnectedIconResourceName),
            [TrayLeagueStatus.Connecting] = LoadIcon(ConnectingIconResourceName),
            [TrayLeagueStatus.Offline] = LoadIcon(OfflineIconResourceName)
        };
    }

    private static Icon LoadIcon(string resourceName)
    {
        try
        {
            using var stream = typeof(App).Assembly.GetManifestResourceStream(resourceName);
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
        foreach (var icon in _icons.Values) icon.Dispose();
    }

    private enum TrayLeagueStatus
    {
        Offline,
        Connecting,
        Connected
    }
}
