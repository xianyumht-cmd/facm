using FACM.Core.Desktop;
using FACM.Core.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace FACM.App;

public sealed partial class CompactLauncherWindow : Window
{
    private const double BaseWidthDip = 420d;
    private const double BaseHeightDip = 680d;
    private const double GapDip = 14d;
    private const double EdgeMarginDip = 8d;

    private readonly IDesktopWorkAreaProvider _workAreas;
    private readonly Action<string> _openShellSection;
    private readonly DesktopSurfaceOutsideClickWatcher _outsideClickWatcher;
    private int _outsideCloseSuppression;
    private bool _closed;

    public CompactLauncherWindow(
        IDesktopWorkAreaProvider workAreas,
        IUiTextProvider text,
        Action<string> openShellSection)
    {
        _workAreas = workAreas ?? throw new ArgumentNullException(nameof(workAreas));
        ArgumentNullException.ThrowIfNull(text);
        _openShellSection = openShellSection ?? throw new ArgumentNullException(nameof(openShellSection));

        InitializeComponent();
        ApplyText(text);
        ConfigurePresenter();
        _outsideClickWatcher = new DesktopSurfaceOutsideClickWatcher(
            DispatcherQueue,
            GetScreenBounds,
            () => Volatile.Read(ref _outsideCloseSuppression) != 0,
            Close);
        _outsideClickWatcher.Start();

        CloseButton.Click += OnCloseClick;
        RepairButton.Click += (_, _) => OpenSection("repair");
        LeagueButton.Click += (_, _) => OpenSection("league");
        PersonalizationButton.Click += (_, _) => OpenSection("personalization");
        SettingsButton.Click += (_, _) => OpenSection("settings");
        ControlCenterButton.Click += (_, _) => OpenSection("repair");
        Closed += OnClosed;
    }

    public IDisposable SuppressOutsideClose()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        Interlocked.Increment(ref _outsideCloseSuppression);
        return new OutsideCloseSuppressionScope(this);
    }

    public void ShowNextTo(DesktopRect floatingBounds)
    {
        if (_closed || !floatingBounds.IsValid) return;

        var areas = _workAreas.GetWorkingAreas();
        var anchorCenter = new DesktopPoint(
            floatingBounds.Left + (floatingBounds.Width / 2d),
            floatingBounds.Top + (floatingBounds.Height / 2d));
        var area = AnchorPlacementService.SelectWorkArea(areas, anchorCenter);
        var size = DesktopDpi.DipsToPixels(new DesktopSize(BaseWidthDip, BaseHeightDip), area);
        var gap = DesktopDpi.UniformDipsToPixels(GapDip, area);
        var edge = DesktopDpi.UniformDipsToPixels(EdgeMarginDip, area);

        var openLeft = anchorCenter.X > area.Bounds.Left + (area.Bounds.Width / 2d);
        var x = openLeft
            ? floatingBounds.Left - size.Width - gap
            : floatingBounds.Right + gap;
        var y = floatingBounds.Top + (floatingBounds.Height / 2d) - (size.Height / 2d);

        var minX = area.Bounds.Left + edge;
        var maxX = area.Bounds.Right - size.Width - edge;
        var minY = area.Bounds.Top + edge;
        var maxY = area.Bounds.Bottom - size.Height - edge;

        if (maxX < minX)
            minX = maxX = area.Bounds.Left + Math.Max(0d, (area.Bounds.Width - size.Width) / 2d);
        if (maxY < minY)
            minY = maxY = area.Bounds.Top + Math.Max(0d, (area.Bounds.Height - size.Height) / 2d);

        x = Math.Clamp(x, minX, maxX);
        y = Math.Clamp(y, minY, maxY);

        AppWindow.MoveAndResize(new RectInt32(
            ToInt32(x),
            ToInt32(y),
            Math.Max(1, ToInt32(size.Width)),
            Math.Max(1, ToInt32(size.Height))));
        Activate();
    }

    private void ApplyText(IUiTextProvider text)
    {
        var appName = text.Get(UiTextKeys.AppName);
        Title = appName + " " + text.Get(UiTextKeys.ControlCenter);
        BrandText.Text = appName;
        ControlCenterText.Text = text.Get(UiTextKeys.ControlCenter);
        StatusTitle.Text = text.Get(UiTextKeys.ShellStatusLabel);
        StatusBody.Text = text.Get(UiTextKeys.ShellStatusReady);
        RepairButton.Content = text.Get(UiTextKeys.ShellRepairTools);
        LeagueButton.Content = text.Get(UiTextKeys.ShellLeague);
        PersonalizationButton.Content = text.Get(UiTextKeys.ShellPersonalization);
        SettingsButton.Content = text.Get(UiTextKeys.ShellMoreSettings);
        ControlCenterButton.Content = text.Get(UiTextKeys.ControlCenter);
        FooterTitle.Text = text.Get(UiTextKeys.DesktopOpenShell);
        FooterHint.Text = text.Get(UiTextKeys.DesktopOpenShellHelp);
    }

    private void ConfigurePresenter()
    {
        ExtendsContentIntoTitleBar = true;
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is not OverlappedPresenter presenter) return;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
    }

    private void OnCloseClick(object sender, RoutedEventArgs args) => Close();

    private void OpenSection(string section)
    {
        if (_closed) return;
        _openShellSection(section);
        Close();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (_closed) return;
        _closed = true;
        _outsideClickWatcher.Dispose();
        Closed -= OnClosed;
    }

    private DesktopRect? GetScreenBounds()
    {
        var position = AppWindow.Position;
        var size = AppWindow.Size;
        var bounds = new DesktopRect(position.X, position.Y, size.Width, size.Height);
        return bounds.IsValid ? bounds : null;
    }

    private void ReleaseOutsideCloseSuppression()
    {
        if (Volatile.Read(ref _outsideCloseSuppression) == 0) return;
        Interlocked.Decrement(ref _outsideCloseSuppression);
    }

    private sealed class OutsideCloseSuppressionScope(CompactLauncherWindow owner) : IDisposable
    {
        private CompactLauncherWindow? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ReleaseOutsideCloseSuppression();
    }

    private static int ToInt32(double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        return (int)Math.Clamp(rounded, int.MinValue, int.MaxValue);
    }
}
