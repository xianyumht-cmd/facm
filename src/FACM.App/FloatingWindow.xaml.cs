using FACM.Core.Desktop;
using FACM.Core.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Windows.Graphics;

namespace FACM.App;

public sealed partial class FloatingWindow : Window
{
    private const double SurfaceSideDip = 64d;
    private const double MarginDip = 12d;

    private readonly IDesktopWorkAreaProvider _workAreas;
    private readonly Action _ensureMainWindow;

    public FloatingWindow(
        IDesktopWorkAreaProvider workAreas,
        IUiTextProvider text,
        Action ensureMainWindow)
    {
        _workAreas = workAreas ?? throw new ArgumentNullException(nameof(workAreas));
        ArgumentNullException.ThrowIfNull(text);
        _ensureMainWindow = ensureMainWindow ?? throw new ArgumentNullException(nameof(ensureMainWindow));

        InitializeComponent();
        Title = text.Get(UiTextKeys.AppName);
        AutomationProperties.SetName(FloatingButton, text.Get(UiTextKeys.DesktopOpenShell));
        FloatingButton.Click += OnFloatingButtonClick;
        ConfigurePresenter();
    }

    public AnchorPlacementResult ApplyPlacement(DesktopPoint? preferredTopLeft)
    {
        var areas = _workAreas.GetWorkingAreas();
        var selected = AnchorPlacementService.SelectWorkArea(areas, preferredTopLeft);
        var size = new DesktopSize(
            SurfaceSideDip * selected.DpiScaleX,
            SurfaceSideDip * selected.DpiScaleY);
        var margin = MarginDip * Math.Max(selected.DpiScaleX, selected.DpiScaleY);
        var placement = AnchorPlacementService.Place(new AnchorPlacementRequest(
            [selected],
            size,
            preferredTopLeft,
            DesktopAnchor.Auto,
            margin));

        AppWindow.MoveAndResize(new RectInt32(
            ToInt32(placement.TopLeft.X),
            ToInt32(placement.TopLeft.Y),
            Math.Max(1, ToInt32(size.Width)),
            Math.Max(1, ToInt32(size.Height))));
        return placement;
    }

    private void ConfigurePresenter()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter) return;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
    }

    private void OnFloatingButtonClick(object sender, RoutedEventArgs e) => _ensureMainWindow();

    private static int ToInt32(double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        return (int)Math.Clamp(rounded, int.MinValue, int.MaxValue);
    }
}
