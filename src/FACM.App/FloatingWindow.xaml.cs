using FACM.Core.Desktop;
using FACM.Core.Text;
using FACM.Platform.Windows.Desktop;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace FACM.App;

public sealed partial class FloatingWindow : Window
{
    private const double SurfaceSideDip = 64d;
    private const double DefaultRightMarginDip = 18d;
    private const double LegacyDragThresholdPixels = 4d;
    private const long DragClickSuppressionMilliseconds = 350;

    private readonly IDesktopWorkAreaProvider _workAreas;
    private readonly WindowsFloatingSurfacePlatform _platform;
    private readonly Action _toggleCompactLauncher;
    private readonly Action _showTrayContextMenu;
    private readonly Func<DesktopPoint, Task> _persistPlacement;
    private readonly IntPtr _windowHandle;
    private readonly PointerEventHandler _pointerPressedHandler;
    private readonly PointerEventHandler _pointerMovedHandler;
    private readonly PointerEventHandler _pointerReleasedHandler;
    private readonly PointerEventHandler _pointerCanceledHandler;
    private readonly PointerEventHandler _pointerCaptureLostHandler;

    private IReadOnlyList<DesktopWorkArea>? _dragWorkAreas;
    private bool _pointerActive;
    private bool _dragMoved;
    private uint _activePointerId;
    private DesktopPoint _dragCursorStart;
    private DesktopPoint _dragWindowStart;
    private long _suppressClickUntilTick;
    private bool _closed;

    public FloatingWindow(
        IDesktopWorkAreaProvider workAreas,
        WindowsFloatingSurfacePlatform platform,
        IUiTextProvider text,
        Action toggleCompactLauncher,
        Action showTrayContextMenu,
        Func<DesktopPoint, Task> persistPlacement)
    {
        _workAreas = workAreas ?? throw new ArgumentNullException(nameof(workAreas));
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        ArgumentNullException.ThrowIfNull(text);
        _toggleCompactLauncher = toggleCompactLauncher ?? throw new ArgumentNullException(nameof(toggleCompactLauncher));
        _showTrayContextMenu = showTrayContextMenu ?? throw new ArgumentNullException(nameof(showTrayContextMenu));
        _persistPlacement = persistPlacement ?? throw new ArgumentNullException(nameof(persistPlacement));

        InitializeComponent();
        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        Title = text.Get(UiTextKeys.AppName);
        AutomationProperties.SetName(FloatingButton, text.Get(UiTextKeys.DesktopOpenShell));
        AutomationProperties.SetHelpText(FloatingButton, text.Get(UiTextKeys.DesktopOpenShellHelp));

        _pointerPressedHandler = new PointerEventHandler(OnFloatingPointerPressed);
        _pointerMovedHandler = new PointerEventHandler(OnFloatingPointerMoved);
        _pointerReleasedHandler = new PointerEventHandler(OnFloatingPointerReleased);
        _pointerCanceledHandler = new PointerEventHandler(OnFloatingPointerCanceled);
        _pointerCaptureLostHandler = new PointerEventHandler(OnFloatingPointerCaptureLost);

        // Keep WinUI as the presentation/input layer, but mirror FACM 3.5's proven interaction model:
        // the Button may handle routed pointer events itself, so observe them at the root; actual drag
        // deltas come from a frozen absolute screen cursor origin, never from the moving WinUI window.
        FloatingRoot.AddHandler(UIElement.PointerPressedEvent, _pointerPressedHandler, handledEventsToo: true);
        FloatingRoot.AddHandler(UIElement.PointerMovedEvent, _pointerMovedHandler, handledEventsToo: true);
        FloatingRoot.AddHandler(UIElement.PointerReleasedEvent, _pointerReleasedHandler, handledEventsToo: true);
        FloatingRoot.AddHandler(UIElement.PointerCanceledEvent, _pointerCanceledHandler, handledEventsToo: true);
        FloatingRoot.AddHandler(UIElement.PointerCaptureLostEvent, _pointerCaptureLostHandler, handledEventsToo: true);
        FloatingButton.Click += OnFloatingButtonClick;
        AppWindow.Changed += OnAppWindowChanged;
        Closed += OnClosed;

        ConfigurePresenter();
        ApplyWindowShape();
    }

    public AnchorPlacementResult ApplyPlacement(DesktopPoint? preferredTopLeft)
    {
        var areas = _workAreas.GetWorkingAreas();
        if (preferredTopLeft is { IsFinite: true } saved)
        {
            var selected = AnchorPlacementService.SelectWorkArea(areas, saved);
            var size = DesktopDpi.DipsToPixels(
                new DesktopSize(SurfaceSideDip, SurfaceSideDip),
                selected);
            var restored = FloatingSurfaceDragService.ClampLegacyBallTopLeft(
                areas,
                size,
                saved,
                saved);
            var recovered = restored.TopLeft != saved;
            var width = Math.Max(1, ToInt32(size.Width));
            var height = Math.Max(1, ToInt32(size.Height));
            AppWindow.MoveAndResize(new RectInt32(
                ToInt32(restored.TopLeft.X),
                ToInt32(restored.TopLeft.Y),
                width,
                height));
            ApplyWindowShape(width, height);
            return new AnchorPlacementResult(
                restored.TopLeft,
                restored.WorkArea,
                DesktopAnchor.Auto,
                recovered);
        }

        var defaultArea = AnchorPlacementService.SelectWorkArea(areas, null);
        var defaultSize = DesktopDpi.DipsToPixels(
            new DesktopSize(SurfaceSideDip, SurfaceSideDip),
            defaultArea);
        var defaultRightMarginPixels = DesktopDpi.UniformDipsToPixels(DefaultRightMarginDip, defaultArea);
        var defaultTopLeft = FloatingSurfaceDragService.DefaultLegacyBallTopLeft(
            defaultArea,
            defaultSize,
            defaultRightMarginPixels);
        var defaultWidth = Math.Max(1, ToInt32(defaultSize.Width));
        var defaultHeight = Math.Max(1, ToInt32(defaultSize.Height));
        AppWindow.MoveAndResize(new RectInt32(
            ToInt32(defaultTopLeft.X),
            ToInt32(defaultTopLeft.Y),
            defaultWidth,
            defaultHeight));
        ApplyWindowShape(defaultWidth, defaultHeight);
        return new AnchorPlacementResult(
            defaultTopLeft,
            defaultArea,
            DesktopAnchor.Right,
            false);
    }

    public DesktopRect GetCurrentBounds()
    {
        var position = AppWindow.Position;
        var size = AppWindow.Size;
        return new DesktopRect(position.X, position.Y, size.Width, size.Height);
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

    private void OnFloatingButtonClick(object sender, RoutedEventArgs e)
    {
        // WinUI Button raises Click during its own PointerReleased class handling. Do not reject the
        // click merely because our root observer has not reset _pointerActive yet. A real drag marks
        // suppression as soon as it crosses the 3.5 threshold, so only click-like releases get here.
        if (_dragMoved || Environment.TickCount64 <= _suppressClickUntilTick) return;
        _toggleCompactLauncher();
    }

    private void OnFloatingPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_closed || _pointerActive) return;
        var point = e.GetCurrentPoint(FloatingRoot);
        var properties = point.Properties;
        if (properties.IsRightButtonPressed)
        {
            e.Handled = true;
            _showTrayContextMenu();
            return;
        }
        var primaryContact = properties.IsLeftButtonPressed || (!properties.IsRightButtonPressed && point.IsInContact);
        if (!primaryContact) return;
        if (!_platform.TryGetCursorPosition(out var cursor)) return;

        IReadOnlyList<DesktopWorkArea> areas;
        try
        {
            areas = _workAreas.GetWorkingAreas();
        }
        catch
        {
            return;
        }

        _pointerActive = true;
        _dragMoved = false;
        _activePointerId = e.Pointer.PointerId;
        _dragWorkAreas = areas;
        _dragCursorStart = cursor;
        _dragWindowStart = new DesktopPoint(AppWindow.Position.X, AppWindow.Position.Y);
    }

    private void OnFloatingPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_pointerActive || e.Pointer.PointerId != _activePointerId || _dragWorkAreas is null) return;
        if (!_platform.TryGetCursorPosition(out var currentCursor)) return;

        if (!_dragMoved && !FloatingSurfaceDragService.HasExceededLegacyBallThreshold(
                _dragCursorStart,
                currentCursor,
                LegacyDragThresholdPixels))
        {
            return;
        }

        if (!_dragMoved)
        {
            _dragMoved = true;
            // Button.Click can be raised before our root PointerReleased observer. Suppress it as soon
            // as movement becomes a drag instead of waiting until release.
            _suppressClickUntilTick = Environment.TickCount64 + DragClickSuppressionMilliseconds;
        }

        var proposed = new DesktopPoint(
            _dragWindowStart.X + currentCursor.X - _dragCursorStart.X,
            _dragWindowStart.Y + currentCursor.Y - _dragCursorStart.Y);

        try
        {
            var placement = FloatingSurfaceDragService.ClampLegacyBallTopLeft(
                _dragWorkAreas,
                new DesktopSize(AppWindow.Size.Width, AppWindow.Size.Height),
                proposed,
                currentCursor);
            AppWindow.Move(new PointInt32(
                ToInt32(placement.TopLeft.X),
                ToInt32(placement.TopLeft.Y)));
            e.Handled = true;
        }
        catch
        {
            // Pointer movement is best-effort. Keep the last valid window position on transient display errors.
        }
    }

    private async void OnFloatingPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_pointerActive || e.Pointer.PointerId != _activePointerId) return;

        var moved = _dragMoved;
        ResetPointerState();
        if (!moved)
        {
            // Leave the release unhandled so the WinUI Button completes its normal Click path.
            return;
        }

        e.Handled = true;
        await PersistCurrentPlacementAsync();
    }

    private async void OnFloatingPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (!_pointerActive || e.Pointer.PointerId != _activePointerId) return;

        var moved = _dragMoved;
        ResetPointerState();
        e.Handled = true;
        if (moved) await PersistCurrentPlacementAsync();
    }

    private async void OnFloatingPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_pointerActive || e.Pointer.PointerId != _activePointerId) return;

        var moved = _dragMoved;
        ResetPointerState();
        if (moved) await PersistCurrentPlacementAsync();
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (_closed || !args.DidSizeChange) return;
        ApplyWindowShape(sender.Size.Width, sender.Size.Height);
    }

    private async Task PersistCurrentPlacementAsync()
    {
        try
        {
            var position = AppWindow.Position;
            await _persistPlacement(new DesktopPoint(position.X, position.Y));
        }
        catch
        {
            // Position persistence must never make the always-available launcher unusable.
        }
    }

    private void ResetPointerState()
    {
        _pointerActive = false;
        _dragMoved = false;
        _activePointerId = 0;
        _dragWorkAreas = null;
    }

    private void ApplyWindowShape() => ApplyWindowShape(AppWindow.Size.Width, AppWindow.Size.Height);

    private void ApplyWindowShape(int widthPixels, int heightPixels)
    {
        _ = _platform.TryApplyCircularRegion(_windowHandle, widthPixels, heightPixels);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (_closed) return;
        _closed = true;
        ResetPointerState();

        FloatingRoot.RemoveHandler(UIElement.PointerPressedEvent, _pointerPressedHandler);
        FloatingRoot.RemoveHandler(UIElement.PointerMovedEvent, _pointerMovedHandler);
        FloatingRoot.RemoveHandler(UIElement.PointerReleasedEvent, _pointerReleasedHandler);
        FloatingRoot.RemoveHandler(UIElement.PointerCanceledEvent, _pointerCanceledHandler);
        FloatingRoot.RemoveHandler(UIElement.PointerCaptureLostEvent, _pointerCaptureLostHandler);
        FloatingButton.Click -= OnFloatingButtonClick;
        AppWindow.Changed -= OnAppWindowChanged;
        Closed -= OnClosed;
    }

    private static int ToInt32(double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        return (int)Math.Clamp(rounded, int.MinValue, int.MaxValue);
    }
}
