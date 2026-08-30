using System.ComponentModel;
using FACM.App.ViewModels;
using FACM.Core.Cleanup;
using FACM.Core.Desktop;
using FACM.Core.League;
using FACM.Core.State;
using FACM.Core.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage.Pickers;

namespace FACM.App;

public sealed partial class MainWindow : Window
{
    private const double OrbSizeDip = 36d;
    private const double OrbTransientWidthDip = 220d;
    private const double ControlMatrixWidthDip = 360d;
    private const double ControlMatrixHeightDip = 176d;
    private const double RepairSurfaceWidthDip = 520d;
    private const double RepairSurfaceHeightDip = 440d;
    private const double LeagueSurfaceWidthDip = 540d;
    private const double LeagueSurfaceHeightDip = 430d;
    private const double SettingsSurfaceWidthDip = 500d;
    private const double SettingsSurfaceHeightDip = 380d;
    private const double PersonalizationSurfaceWidthDip = 500d;
    private const double PersonalizationSurfaceHeightDip = 400d;
    private const double ChampSelectStripWidthDip = 560d;
    private const double ChampSelectWaitingWidthDip = 280d;
    private const double ChampSelectStripHeightDip = 56d;
    private const double SurfaceEdgeMarginDip = 8d;
    private const double SurfaceDragThresholdPixels = 4d;
    private const long SurfaceDragClickSuppressionMilliseconds = 350;

    private readonly ControlCenterViewModel _controlCenter;
    private readonly CleanupViewModel _cleanupCenter;
    private readonly RepairToolsViewModel _repairTools;
    private readonly LeagueWorkbenchViewModel _leagueWorkbench;
    private readonly DiagnosticsCenterViewModel _diagnosticsCenter;
    private readonly IUiTextProvider _text;
    private readonly DesktopSurfaceOutsideClickWatcher _outsideClickWatcher;
    private readonly bool _morphingSurfaceEnabled;
    private readonly IDesktopWorkAreaProvider? _surfaceWorkAreas;
    private readonly IDesktopCursorPositionProvider? _surfacePlatform;
    private readonly Func<DesktopPoint, Task>? _persistSurfacePlacement;
    private readonly Action? _showTrayContextMenu;
    private readonly FacmSurfaceStateMachine _surfaceStateMachine;
    private readonly Action<FacmSurfaceTransition>? _surfaceTransitionReporter;
    private readonly Action<FacmSurfacePresentationFailure>? _surfacePresentationFailureReporter;
    private int _outsideCloseSuppression;
    private bool _closed;
    private bool _cleanupInitialized;
    private bool _cleanupUiBusy;
    private bool _diagnosticsLoaded;
    private bool _diagnosticsBusy;
    private UIElement? _inspectorHoverElement;
    private UIElement? _inspectorFocusElement;
    private DesktopPoint? _surfaceAnchor;
    private bool _manualOpenOverride;
    private bool _surfacePointerActive;
    private bool _surfaceDragMoved;
    private uint _surfacePointerId;
    private DesktopPoint _surfaceDragCursorStart;
    private DesktopPoint _surfaceDragWindowStart;
    private long _surfaceSuppressClickUntilTick;
    private LeagueProductState? _lastSurfaceGameflowState;
    private string _activeSection = "repair";
    private DispatcherQueueTimer? _transientRailTimer;
    private bool _transientRailVisible;
    private bool _transientRailOnLeft;
    private string _lastRuntimeStatusSignature = string.Empty;
    private FacmSurfacePresentation? _currentPresentation;

    public MainWindow(
        ControlCenterViewModel controlCenter,
        CleanupViewModel cleanupCenter,
        RepairToolsViewModel repairTools,
        LeagueWorkbenchViewModel leagueWorkbench,
        DiagnosticsCenterViewModel diagnosticsCenter,
        IUiTextProvider text,
        bool morphingSurfaceEnabled = false,
        IDesktopWorkAreaProvider? surfaceWorkAreas = null,
        IDesktopCursorPositionProvider? surfacePlatform = null,
        Func<DesktopPoint, Task>? persistSurfacePlacement = null,
        Action? showTrayContextMenu = null,
        Action<FacmSurfaceTransition>? surfaceTransitionReporter = null,
        Action<FacmSurfacePresentationFailure>? surfacePresentationFailureReporter = null)
    {
        _controlCenter = controlCenter ?? throw new ArgumentNullException(nameof(controlCenter));
        _cleanupCenter = cleanupCenter ?? throw new ArgumentNullException(nameof(cleanupCenter));
        _repairTools = repairTools ?? throw new ArgumentNullException(nameof(repairTools));
        _leagueWorkbench = leagueWorkbench ?? throw new ArgumentNullException(nameof(leagueWorkbench));
        _diagnosticsCenter = diagnosticsCenter ?? throw new ArgumentNullException(nameof(diagnosticsCenter));
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _morphingSurfaceEnabled = morphingSurfaceEnabled;
        _surfaceWorkAreas = surfaceWorkAreas;
        _surfacePlatform = surfacePlatform;
        _persistSurfacePlacement = persistSurfacePlacement;
        _showTrayContextMenu = showTrayContextMenu;
        _surfaceTransitionReporter = surfaceTransitionReporter;
        _surfacePresentationFailureReporter = surfacePresentationFailureReporter;
        _surfaceStateMachine = new FacmSurfaceStateMachine();
        _surfaceStateMachine.Transitioned += OnSurfaceTransitioned;
        try
        {
            InitializeComponent();
        }
        catch (Exception exception)
        {
            StartupFailureObserver.TryWrite(exception, "main-window-xaml-failure.txt");
            throw;
        }
        _outsideClickWatcher = new DesktopSurfaceOutsideClickWatcher(
            DispatcherQueue,
            GetScreenBounds,
            () => Volatile.Read(ref _outsideCloseSuppression) != 0,
            RequestOutsideClickDismissal);
        _outsideClickWatcher.Start();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ConfigureMorphingPresenter();
        ApplyStaticText();
        InitializeInspector();
        ApplyLeagueRuntimeState();
        ApplyCleanupRuntimeState();
        ApplyRepairToolsState();
        InitializePersonalizationSurface();
        SurfaceRoot.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnSurfacePointerPressed), handledEventsToo: true);
        SurfaceRoot.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnSurfacePointerMoved), handledEventsToo: true);
        SurfaceRoot.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnSurfacePointerReleased), handledEventsToo: true);
        SurfaceRoot.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnSurfacePointerCanceled), handledEventsToo: true);
        SurfaceRoot.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnSurfacePointerCaptureLost), handledEventsToo: true);
        _cleanupCenter.PropertyChanged += OnCleanupPropertyChanged;
        _leagueWorkbench.PropertyChanged += OnLeagueWorkbenchPropertyChanged;
        Closed += OnClosed;
        RootNavigation.SelectedItem = RepairNav;
        RootNavigation.Loaded += OnRootNavigationLoaded;
        if (!_morphingSurfaceEnabled) ApplyLegacySurfaceMode();
    }

    private void OnSurfaceTransitioned(object? sender, FacmSurfaceTransition transition)
    {
        try
        {
            _surfaceTransitionReporter?.Invoke(transition);
        }
        catch
        {
            // Presentation is already committed. Diagnostics must never invalidate a safe surface.
        }
    }

    private void SetFeatureTitleBar() => SetTitleBar((UIElement)AppTitleBar);

    private void ConfigureMorphingPresenter()
    {
        if (!_morphingSurfaceEnabled) return;
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is not OverlappedPresenter presenter) return;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
    }

    private void ApplyLegacySurfaceMode()
    {
        OrbPresentationRoot.Visibility = Visibility.Collapsed;
        OrbSurface.Visibility = Visibility.Collapsed;
        OrbTransientRail.Visibility = Visibility.Collapsed;
        ControlMatrixSurface.Visibility = Visibility.Collapsed;
        ChampSelectSurface.Visibility = Visibility.Collapsed;
        LegacyFeatureSurface.Visibility = Visibility.Visible;
        RootNavigation.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
        RootNavigation.IsPaneVisible = true;
        RootNavigation.Background = (Brush)Application.Current.Resources["FacmBackgroundBrush"];
        RootNavigation.IsPaneToggleButtonVisible = true;
        RootNavigation.IsPaneOpen = true;
        SurfaceCollapseButton.Visibility = Visibility.Collapsed;
        SurfaceCloseButton.Visibility = Visibility.Collapsed;
    }

    private void PrepareSurfaceContent(FacmSurfacePresentation presentation)
    {
        presentation.EnsureValid();
        var mode = presentation.Mode;
        if (mode == FacmSurfaceMode.Orb)
        {
            OrbPresentationRoot.Visibility = Visibility.Visible;
            OrbSurface.Visibility = Visibility.Visible;
            OrbTransientRail.Visibility = presentation.RailVisible ? Visibility.Visible : Visibility.Collapsed;
        }
        else if (mode == FacmSurfaceMode.ControlMatrix)
        {
            ControlMatrixSurface.Visibility = Visibility.Visible;
            MatrixTitle.Text = _text.Get(UiTextKeys.AppName);
            MatrixStatus.Text = "LCU · " + _leagueWorkbench.LeagueState;
            SetTitleBar(MatrixTitleBar);
        }
        else if (mode == FacmSurfaceMode.ChampSelectStrip)
        {
            ChampSelectSurface.Visibility = Visibility.Visible;
            ApplyMorphingChampSelectState();
        }
        else if (mode is FacmSurfaceMode.FeatureSurface or FacmSurfaceMode.LeagueSurface)
        {
            LegacyFeatureSurface.Visibility = Visibility.Visible;
            SetFeatureTitleBar();
        }
    }

    private void CommitSurfaceContent(FacmSurfacePresentation presentation)
    {
        var mode = presentation.Mode;
        if (mode != FacmSurfaceMode.Orb)
        {
            _transientRailTimer?.Stop();
            _transientRailVisible = false;
            OrbTransientRailText.Text = string.Empty;
            ResetTransientRailPlacement();
        }
        if (mode != FacmSurfaceMode.ChampSelectStrip)
        {
            CancelChampSelectIdentityLoad();
            _champSelectRequestedSignature = string.Empty;
            _champSelectRenderedSignature = string.Empty;
            _champSelectHasCandidates = false;
        }
        RootNavigation.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
        RootNavigation.IsPaneVisible = false;
        RootNavigation.IsPaneOpen = false;
        RootNavigation.IsPaneToggleButtonVisible = false;
        RootNavigation.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        OrbPresentationRoot.Visibility = presentation.OrbVisible ? Visibility.Visible : Visibility.Collapsed;
        OrbSurface.Visibility = presentation.OrbVisible ? Visibility.Visible : Visibility.Collapsed;
        OrbTransientRail.Visibility = presentation.RailVisible ? Visibility.Visible : Visibility.Collapsed;
        ControlMatrixSurface.Visibility = mode == FacmSurfaceMode.ControlMatrix ? Visibility.Visible : Visibility.Collapsed;
        ChampSelectSurface.Visibility = mode == FacmSurfaceMode.ChampSelectStrip ? Visibility.Visible : Visibility.Collapsed;
        var featureVisible = mode is FacmSurfaceMode.FeatureSurface or FacmSurfaceMode.LeagueSurface;
        LegacyFeatureSurface.Visibility = featureVisible ? Visibility.Visible : Visibility.Collapsed;
        SurfaceBackButton.Visibility = featureVisible ? Visibility.Visible : Visibility.Collapsed;
        SurfaceCollapseButton.Visibility = featureVisible ? Visibility.Visible : Visibility.Collapsed;
        SurfaceCloseButton.Visibility = featureVisible ? Visibility.Visible : Visibility.Collapsed;
        MatrixCloseButton.Visibility = mode == FacmSurfaceMode.ControlMatrix ? Visibility.Visible : Visibility.Collapsed;
        ApplyMorphingFeatureDensity(featureVisible);

        if (presentation.ContentVisible &&
            OrbPresentationRoot.Visibility != Visibility.Visible &&
            ControlMatrixSurface.Visibility != Visibility.Visible &&
            ChampSelectSurface.Visibility != Visibility.Visible &&
            LegacyFeatureSurface.Visibility != Visibility.Visible)
            throw new InvalidOperationException("FACM presentation would expose a window with no visible content.");
    }

    private void ApplyMorphingFeatureDensity(bool featureVisible)
    {
        var showExplanatoryCopy = !_morphingSurfaceEnabled;
        ProductTitle.Visibility = showExplanatoryCopy ? Visibility.Visible : Visibility.Collapsed;
        SectionTitle.Visibility = showExplanatoryCopy ? Visibility.Visible : Visibility.Collapsed;
        SectionSubtitle.Visibility = showExplanatoryCopy ? Visibility.Visible : Visibility.Collapsed;
        RepairToolsDescription.Visibility = showExplanatoryCopy ? Visibility.Visible : Visibility.Collapsed;
        RepairGameDescription.Visibility = showExplanatoryCopy ? Visibility.Visible : Visibility.Collapsed;
        CleanupDirectoryDescription.Visibility = showExplanatoryCopy ? Visibility.Visible : Visibility.Collapsed;
        CleanupPreviewDescription.Visibility = showExplanatoryCopy ? Visibility.Visible : Visibility.Collapsed;
        RepairDriverCleanupHint.Visibility = showExplanatoryCopy ? Visibility.Visible : Visibility.Collapsed;
        RepairFixWindowHint.Visibility = showExplanatoryCopy ? Visibility.Visible : Visibility.Collapsed;
        RepairAutoWindowHint.Visibility = showExplanatoryCopy ? Visibility.Visible : Visibility.Collapsed;
        RepairSkipSettlementHint.Visibility = showExplanatoryCopy ? Visibility.Visible : Visibility.Collapsed;
        RepairRestartClientUxHint.Visibility = showExplanatoryCopy ? Visibility.Visible : Visibility.Collapsed;
        RepairExitGameHint.Visibility = showExplanatoryCopy ? Visibility.Visible : Visibility.Collapsed;
        OverviewBody.Visibility = showExplanatoryCopy ? Visibility.Visible : Visibility.Collapsed;
        StateBody.Visibility = showExplanatoryCopy ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsSubtitle.Visibility = showExplanatoryCopy ? Visibility.Visible : Visibility.Collapsed;
        LeagueAutoMatchmakingHint.Visibility = showExplanatoryCopy ? Visibility.Visible : Visibility.Collapsed;
        LeagueAutoAcceptHint.Visibility = showExplanatoryCopy ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsSummaryText.MinHeight = _morphingSurfaceEnabled ? 120 : 180;
        if (_morphingSurfaceEnabled)
        {
            TitleBarText.FontSize = 14;
            SectionTitle.FontSize = 16;
            if (featureVisible) ApplyMorphingCardDensity(LegacyFeatureSurface);
        }
    }

    private static void ApplyMorphingCardDensity(DependencyObject root)
    {
        var cardStyle = (Style)Application.Current.Resources["FacmCardBorderStyle"];
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is Border border && ReferenceEquals(border.Style, cardStyle))
            {
                border.Padding = new Thickness(10, 8, 10, 8);
                border.CornerRadius = new CornerRadius(8);
            }

            ApplyMorphingCardDensity(child);
        }
    }

    private DesktopRect CalculateSurfaceBounds(FacmSurfaceMode mode)
    {
        if (mode == FacmSurfaceMode.HiddenInGame)
            throw new InvalidOperationException("HiddenInGame does not have visible surface bounds.");

        var currentPosition = AppWindow.Position;
        var dipSize = GetSurfaceDipSize(mode);
        var workAreas = _surfaceWorkAreas;
        if (workAreas is null)
            return new DesktopRect(currentPosition.X, currentPosition.Y, dipSize.Width, dipSize.Height);

        var areas = workAreas.GetWorkingAreas();
        if (areas.Count == 0)
            return new DesktopRect(currentPosition.X, currentPosition.Y, dipSize.Width, dipSize.Height);
        var anchor = _surfaceAnchor ?? new DesktopPoint(currentPosition.X, currentPosition.Y);
        var probe = new DesktopPoint(anchor.X + (OrbSizeDip / 2d), anchor.Y + (OrbSizeDip / 2d));
        var area = AnchorPlacementService.SelectWorkArea(areas, probe);
        var size = DesktopDpi.DipsToPixels(dipSize, area);
        var transientRail = mode == FacmSurfaceMode.Orb && _transientRailVisible;
        return FacmSurfaceGeometryService.ExpandFromAnchor(new(
            anchor,
            size,
            area,
            DesktopDpi.UniformDipsToPixels(SurfaceEdgeMarginDip, area),
            mode == FacmSurfaceMode.Orb && !transientRail));
    }

    private void ApplySurfaceGeometry(FacmSurfacePresentation presentation)
    {
        if (!_morphingSurfaceEnabled || !presentation.WindowVisible) return;
        var physicalRect = presentation.TargetBounds ??
                           throw new InvalidOperationException("Visible presentation bounds are missing.");
        if (presentation.RailVisible)
        {
            var currentPosition = AppWindow.Position;
            var anchor = _surfaceAnchor ?? new DesktopPoint(currentPosition.X, currentPosition.Y);
            ApplyTransientRailPlacement(anchor, physicalRect);
        }
        var rect = new RectInt32(
            ToInt32(physicalRect.Left),
            ToInt32(physicalRect.Top),
            Math.Max(1, ToInt32(physicalRect.Width)),
            Math.Max(1, ToInt32(physicalRect.Height)));
        AppWindow.MoveAndResize(rect);
    }

    private void ApplyTransientRailPlacement(DesktopPoint anchor, DesktopRect physicalRect)
    {
        var railOnLeft = physicalRect.Left < anchor.X;
        _transientRailOnLeft = railOnLeft;
        OrbSurface.HorizontalAlignment = railOnLeft ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        OrbTransientRail.HorizontalAlignment = railOnLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        OrbTransientRail.Margin = railOnLeft ? new Thickness(0, 3, 0, 0) : new Thickness(42, 3, 0, 0);
    }

    private void ResetTransientRailPlacement()
    {
        _transientRailOnLeft = false;
        OrbSurface.HorizontalAlignment = HorizontalAlignment.Left;
        OrbTransientRail.HorizontalAlignment = HorizontalAlignment.Left;
        OrbTransientRail.Margin = new Thickness(42, 3, 0, 0);
    }

    private void SetTransientRailVisible(bool visible, bool resize)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            _ = DispatcherQueue.TryEnqueue(() => SetTransientRailVisible(visible, resize));
            return;
        }
        _transientRailVisible = visible;
        if (!visible)
        {
            _transientRailTimer?.Stop();
            OrbTransientRailText.Text = string.Empty;
            ResetTransientRailPlacement();
        }

        if (resize && _morphingSurfaceEnabled && _surfaceStateMachine.Mode == FacmSurfaceMode.Orb)
            EnsureCurrentSurfacePresentation(visible ? "transient-rail-shown" : "transient-rail-timeout");
    }

    private void ShowTransientRail(string message, bool problem)
    {
        if (!_morphingSurfaceEnabled || _surfaceStateMachine.Mode != FacmSurfaceMode.Orb || string.IsNullOrWhiteSpace(message))
            return;

        OrbTransientRailText.Text = message.Trim();
        OrbTransientRail.Background = (Brush)Application.Current.Resources[
            problem ? "FacmErrorBrush" : "FacmSurfaceBrush"];
        OrbTransientRail.BorderBrush = (Brush)Application.Current.Resources[
            problem ? "FacmErrorBrush" : "FacmStrokeBrush"];
        SetTransientRailVisible(true, resize: true);
        _transientRailTimer ??= DispatcherQueue.CreateTimer();
        _transientRailTimer.IsRepeating = false;
        _transientRailTimer.Interval = problem
            ? TimeSpan.FromSeconds(4)
            : TimeSpan.FromSeconds(1.6);
        _transientRailTimer.Tick -= OnTransientRailTimerTick;
        _transientRailTimer.Tick += OnTransientRailTimerTick;
        _transientRailTimer.Start();
    }

    private void OnTransientRailTimerTick(DispatcherQueueTimer sender, object args) =>
        SetTransientRailVisible(false, resize: true);

    private DesktopSize GetSurfaceDipSize(FacmSurfaceMode mode) => mode switch
    {
        FacmSurfaceMode.Orb => new DesktopSize(
            _transientRailVisible ? OrbTransientWidthDip : OrbSizeDip,
            OrbSizeDip),
        FacmSurfaceMode.ControlMatrix => new DesktopSize(ControlMatrixWidthDip, ControlMatrixHeightDip),
        FacmSurfaceMode.ChampSelectStrip => new DesktopSize(
            _champSelectHasCandidates ? ChampSelectStripWidthDip : ChampSelectWaitingWidthDip,
            ChampSelectStripHeightDip),
        FacmSurfaceMode.LeagueSurface => new DesktopSize(LeagueSurfaceWidthDip, LeagueSurfaceHeightDip),
        FacmSurfaceMode.FeatureSurface when _activeSection == "repair" => new DesktopSize(RepairSurfaceWidthDip, RepairSurfaceHeightDip),
        FacmSurfaceMode.FeatureSurface when _activeSection == "settings" => new DesktopSize(SettingsSurfaceWidthDip, SettingsSurfaceHeightDip),
        FacmSurfaceMode.FeatureSurface when _activeSection == "personalization" => new DesktopSize(PersonalizationSurfaceWidthDip, PersonalizationSurfaceHeightDip),
        _ => new DesktopSize(SettingsSurfaceWidthDip, SettingsSurfaceHeightDip)
    };

    private void RequestOutsideClickDismissal()
    {
        if (!_morphingSurfaceEnabled)
        {
            Close();
            return;
        }

        if (_surfaceStateMachine.IsModalScopeActive || _surfaceStateMachine.Mode == FacmSurfaceMode.HiddenInGame)
            return;
        _manualOpenOverride = false;
        ShowMorphingSurface(FacmSurfaceMode.Orb, "outside-click", false);
    }

    private void OnOrbButtonClick(object sender, RoutedEventArgs e)
    {
        if (!_morphingSurfaceEnabled || _surfaceDragMoved || Environment.TickCount64 <= _surfaceSuppressClickUntilTick)
            return;
        ShowMorphingSurface(FacmSurfaceMode.ControlMatrix, "orb-left-click", true);
    }

    private void OnOrbTransientRailClick(object sender, RoutedEventArgs e)
    {
        if (!_morphingSurfaceEnabled || _surfaceStateMachine.Mode != FacmSurfaceMode.Orb) return;
        ShowMorphingSurface(FacmSurfaceMode.ControlMatrix, "orb-transient-left-click", true);
    }

    private void OnSurfacePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_morphingSurfaceEnabled || _surfaceStateMachine.Mode != FacmSurfaceMode.Orb || _surfacePointerActive)
            return;
        var point = e.GetCurrentPoint(SurfaceRoot);
        if (point.Properties.IsRightButtonPressed)
        {
            e.Handled = true;
            _showTrayContextMenu?.Invoke();
            return;
        }

        var primaryContact = point.Properties.IsLeftButtonPressed ||
                             (!point.Properties.IsRightButtonPressed && point.IsInContact);
        if (!primaryContact || _surfacePlatform is null || !_surfacePlatform.TryGetCursorPosition(out var cursor)) return;

        _surfacePointerActive = true;
        _surfaceDragMoved = false;
        _surfacePointerId = e.Pointer.PointerId;
        _surfaceDragCursorStart = cursor;
        _surfaceDragWindowStart = new DesktopPoint(AppWindow.Position.X, AppWindow.Position.Y);
    }

    private void OnSurfacePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_surfacePointerActive || e.Pointer.PointerId != _surfacePointerId ||
            _surfacePlatform is null || !_surfacePlatform.TryGetCursorPosition(out var cursor)) return;
        if (!_surfaceDragMoved && !FloatingSurfaceDragService.HasExceededLegacyBallThreshold(
                _surfaceDragCursorStart,
                cursor,
                SurfaceDragThresholdPixels)) return;

        if (!_surfaceDragMoved)
        {
            _surfaceDragMoved = true;
            _surfaceSuppressClickUntilTick = Environment.TickCount64 + SurfaceDragClickSuppressionMilliseconds;
        }

        var proposed = new DesktopPoint(
            _surfaceDragWindowStart.X + cursor.X - _surfaceDragCursorStart.X,
            _surfaceDragWindowStart.Y + cursor.Y - _surfaceDragCursorStart.Y);
        try
        {
            var areas = _surfaceWorkAreas?.GetWorkingAreas();
            if (areas is null || areas.Count == 0) return;
            var placement = FloatingSurfaceDragService.ClampLegacyBallTopLeft(
                areas,
                new DesktopSize(AppWindow.Size.Width, AppWindow.Size.Height),
                proposed,
                cursor);
            AppWindow.Move(new PointInt32(ToInt32(placement.TopLeft.X), ToInt32(placement.TopLeft.Y)));
            _surfaceAnchor = placement.TopLeft;
            e.Handled = true;
        }
        catch
        {
        }
    }

    private void OnSurfacePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_surfacePointerActive || e.Pointer.PointerId != _surfacePointerId) return;
        _surfacePointerActive = false;
        if (_surfaceDragMoved)
        {
            _surfaceDragMoved = false;
            if (_surfaceAnchor is { IsFinite: true } anchor && _persistSurfacePlacement is not null)
                _ = _persistSurfacePlacement(anchor);
        }
    }

    private void OnSurfacePointerCanceled(object sender, PointerRoutedEventArgs e) => ResetSurfacePointerState();

    private void OnSurfacePointerCaptureLost(object sender, PointerRoutedEventArgs e) => ResetSurfacePointerState();

    private void ResetSurfacePointerState()
    {
        _surfacePointerActive = false;
        _surfaceDragMoved = false;
    }

    internal FacmSurfaceMode SurfaceMode => _surfaceStateMachine.Mode;

    internal DesktopRect GetSurfaceBounds()
    {
        var position = AppWindow.Position;
        var size = AppWindow.Size;
        return new DesktopRect(position.X, position.Y, size.Width, size.Height);
    }

    internal void InitializeMorphingSurface(DesktopPoint? preferredAnchor)
    {
        if (!_morphingSurfaceEnabled) return;
        _surfaceAnchor = preferredAnchor;
        ShowMorphingSurface(FacmSurfaceMode.Orb, "startup", false);
    }

    internal void ApplyMorphingPlacement(DesktopPoint? preferredAnchor)
    {
        if (!_morphingSurfaceEnabled) return;
        if (!DispatcherQueue.HasThreadAccess)
        {
            _ = DispatcherQueue.TryEnqueue(() => ApplyMorphingPlacement(preferredAnchor));
            return;
        }
        _surfaceAnchor = preferredAnchor;
        EnsureCurrentSurfacePresentation("placement-changed");
    }

    internal void ShowMorphingSurface(
        FacmSurfaceMode mode,
        string reason,
        bool userInitiated,
        string? phase = null)
    {
        if (!_morphingSurfaceEnabled) return;
        if (!DispatcherQueue.HasThreadAccess)
        {
            _ = DispatcherQueue.TryEnqueue(() => ShowMorphingSurface(mode, reason, userInitiated, phase));
            return;
        }
        if (userInitiated) _manualOpenOverride = true;
        RequestSurfacePresentation(mode, reason, userInitiated, phase, activate: mode != FacmSurfaceMode.HiddenInGame);
    }

    private void EnsureCurrentSurfacePresentation(string reason)
    {
        if (!_morphingSurfaceEnabled) return;
        if (!DispatcherQueue.HasThreadAccess)
        {
            _ = DispatcherQueue.TryEnqueue(() => EnsureCurrentSurfacePresentation(reason));
            return;
        }
        RequestSurfacePresentation(_surfaceStateMachine.Mode, reason, false, null, activate: false);
    }

    private void RequestSurfacePresentation(
        FacmSurfaceMode mode,
        string reason,
        bool userInitiated,
        string? phase,
        bool activate)
    {
        if (!DispatcherQueue.HasThreadAccess)
            throw new InvalidOperationException("FACM surface presentation must run on the UI dispatcher.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A presentation reason is required.", nameof(reason));

        var previousMode = _surfaceStateMachine.Mode;
        var previousPresentation = _currentPresentation;
        var correlationId = Guid.NewGuid().ToString("N");
        FacmSurfacePresentation? requestedPresentation = null;
        try
        {
            requestedPresentation = CreateSurfacePresentation(mode, activate);
            if (mode == previousMode && IsSurfacePresentationValid(requestedPresentation)) return;

            ApplySurfacePresentationCore(requestedPresentation, animate: true);
            EnsureSurfacePresentationInvariant(requestedPresentation);
            _surfaceStateMachine.TransitionTo(mode, reason, userInitiated, phase);
            _currentPresentation = requestedPresentation;
        }
        catch (Exception exception)
        {
            RecoverSurfacePresentation(previousPresentation);
            var bounds = requestedPresentation?.TargetBounds ?? GetSurfaceBounds();
            try
            {
                _surfacePresentationFailureReporter?.Invoke(new FacmSurfacePresentationFailure(
                    mode,
                    previousMode,
                    "request:" + reason,
                    exception.GetType().FullName ?? exception.GetType().Name,
                    exception.HResult,
                    Environment.CurrentManagedThreadId,
                    bounds,
                    correlationId,
                    phase,
                    userInitiated));
            }
            catch
            {
                // Recovery is already complete; diagnostics cannot be allowed to destabilize it.
            }
        }
    }

    private FacmSurfacePresentation CreateSurfacePresentation(FacmSurfaceMode mode, bool activate)
    {
        if (mode == FacmSurfaceMode.HiddenInGame)
            return FacmSurfacePresentation.Create(mode, null, activate: false);
        return FacmSurfacePresentation.Create(
            mode,
            CalculateSurfaceBounds(mode),
            mode == FacmSurfaceMode.Orb && _transientRailVisible,
            activate);
    }

    private void ApplySurfacePresentationCore(FacmSurfacePresentation presentation, bool animate)
    {
        presentation.EnsureValid();
        if (presentation.Mode == FacmSurfaceMode.HiddenInGame)
        {
            AppWindow.Hide();
            CommitSurfaceContent(presentation);
            return;
        }

        // Make the requested content renderable before geometry changes, then remove old content only
        // after the AppWindow has reached its target bounds. This prevents an observable blank frame.
        PrepareSurfaceContent(presentation);
        ApplySurfaceGeometry(presentation);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.IsAlwaysOnTop = presentation.Topmost;
        AppWindow.Show();
        CommitSurfaceContent(presentation);
        if (presentation.Activate) Activate();
        if (animate) PlayMorphingSurfaceTransition();
    }

    private bool IsSurfacePresentationValid(FacmSurfacePresentation presentation)
    {
        try
        {
            EnsureSurfacePresentationInvariant(presentation);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void EnsureSurfacePresentationInvariant(FacmSurfacePresentation presentation)
    {
        presentation.EnsureValid();
        if (AppWindow.IsVisible != presentation.WindowVisible)
            throw new InvalidOperationException("FACM AppWindow visibility does not match its presentation.");

        if (!presentation.WindowVisible) return;
        var orbVisible = OrbPresentationRoot.Visibility == Visibility.Visible &&
                         OrbSurface.Visibility == Visibility.Visible;
        var railVisible = OrbTransientRail.Visibility == Visibility.Visible;
        var matrixVisible = ControlMatrixSurface.Visibility == Visibility.Visible;
        var champSelectVisible = ChampSelectSurface.Visibility == Visibility.Visible;
        var featureVisible = LegacyFeatureSurface.Visibility == Visibility.Visible;
        if (orbVisible != presentation.OrbVisible || railVisible != presentation.RailVisible)
            throw new InvalidOperationException("Orb or transient rail visibility violates the presentation contract.");
        if (matrixVisible != (presentation.Mode == FacmSurfaceMode.ControlMatrix) ||
            champSelectVisible != (presentation.Mode == FacmSurfaceMode.ChampSelectStrip) ||
            featureVisible != (presentation.Mode is FacmSurfaceMode.FeatureSurface or FacmSurfaceMode.LeagueSurface))
            throw new InvalidOperationException("Surface content visibility violates the presentation contract.");
        if (!orbVisible && !matrixVisible && !champSelectVisible && !featureVisible)
            throw new InvalidOperationException("FACM AppWindow is visible without visible content.");

        var expected = presentation.TargetBounds ??
                       throw new InvalidOperationException("Visible presentation bounds are missing.");
        var actual = GetSurfaceBounds();
        if (!NearlyEqual(actual.Left, expected.Left) || !NearlyEqual(actual.Top, expected.Top) ||
            !NearlyEqual(actual.Width, expected.Width) || !NearlyEqual(actual.Height, expected.Height))
            throw new InvalidOperationException("FACM AppWindow bounds do not match the presentation contract.");
    }

    private void RecoverSurfacePresentation(FacmSurfacePresentation? previousPresentation)
    {
        if (previousPresentation is not null)
        {
            try
            {
                ApplySurfacePresentationCore(previousPresentation, animate: false);
                EnsureSurfacePresentationInvariant(previousPresentation);
                _currentPresentation = previousPresentation;
                return;
            }
            catch
            {
                // Continue to the bounded emergency Orb fallback below.
            }
        }

        try
        {
            _transientRailVisible = false;
            ResetTransientRailPlacement();
            var position = AppWindow.Position;
            var emergencyOrb = FacmSurfacePresentation.Create(
                FacmSurfaceMode.Orb,
                new DesktopRect(position.X, position.Y, OrbSizeDip, OrbSizeDip),
                activate: false);
            ApplySurfacePresentationCore(emergencyOrb, animate: false);
            EnsureSurfacePresentationInvariant(emergencyOrb);
            _currentPresentation = emergencyOrb;
        }
        catch
        {
            // Hiding is the final fail-safe: a blank AppWindow must never remain interactive.
            AppWindow.Hide();
            _currentPresentation = null;
        }
    }

    private static bool NearlyEqual(double left, double right) => Math.Abs(left - right) <= 2d;

    internal void ApplyGameflowSurfaceMode(LeagueGameflowSnapshot? snapshot)
    {
        if (!_morphingSurfaceEnabled || snapshot is null) return;
        if (!DispatcherQueue.HasThreadAccess)
        {
            _ = DispatcherQueue.TryEnqueue(() => ApplyGameflowSurfaceMode(snapshot));
            return;
        }
        var inGame = snapshot.ProductState == LeagueProductState.InGame;
        var champSelect = snapshot.ProductState == LeagueProductState.ChampSelect;
        var enteredChampSelect = champSelect && _lastSurfaceGameflowState != LeagueProductState.ChampSelect;
        var lobby = snapshot.ProductState is LeagueProductState.Lobby or LeagueProductState.NotRunning;
        var lobbyRestored = lobby &&
                            _lastSurfaceGameflowState is not null &&
                            _lastSurfaceGameflowState is not (LeagueProductState.Lobby or LeagueProductState.NotRunning);
        _lastSurfaceGameflowState = snapshot.ProductState;

        if (inGame && !_manualOpenOverride)
        {
            ShowMorphingSurface(FacmSurfaceMode.HiddenInGame, "gameflow-in-game", false, snapshot.Phase);
            return;
        }

        if (champSelect)
        {
            ShowMorphingSurface(FacmSurfaceMode.ChampSelectStrip, "gameflow-champ-select", false, snapshot.Phase);
            if (enteredChampSelect) _ = RefreshChampSelectWorkbenchAsync();
            return;
        }

        if (lobbyRestored)
        {
            _manualOpenOverride = false;
            ShowMorphingSurface(FacmSurfaceMode.Orb, "gameflow-lobby-restored", false, snapshot.Phase);
        }
    }

    private void OnSurfaceBackClick(object sender, RoutedEventArgs e)
    {
        if (!_morphingSurfaceEnabled) return;
        _manualOpenOverride = false;
        ShowMorphingSurface(FacmSurfaceMode.ControlMatrix, "back-to-control-matrix", true);
    }

    private void OnSurfaceCollapseToOrbClick(object sender, RoutedEventArgs e)
    {
        if (!_morphingSurfaceEnabled) return;
        _manualOpenOverride = false;
        ShowMorphingSurface(FacmSurfaceMode.Orb, "collapse-to-orb", true);
    }

    private void OnSurfaceCloseClick(object sender, RoutedEventArgs e)
    {
        if (!_morphingSurfaceEnabled) return;
        Close();
    }

    private void OnMatrixRepairClick(object sender, RoutedEventArgs e) =>
        OpenMorphingFeature("repair", FacmSurfaceMode.FeatureSurface);

    private void OnMatrixLeagueClick(object sender, RoutedEventArgs e) =>
        OpenMorphingFeature("league", FacmSurfaceMode.LeagueSurface);

    private void OnMatrixDiagnosticsClick(object sender, RoutedEventArgs e) =>
        OpenMorphingFeature("settings", FacmSurfaceMode.FeatureSurface);

    private void OnMatrixPersonalizationClick(object sender, RoutedEventArgs e) =>
        OpenMorphingFeature("personalization", FacmSurfaceMode.FeatureSurface);

    private void OnMatrixSettingsClick(object sender, RoutedEventArgs e) =>
        OpenMorphingFeature("settings", FacmSurfaceMode.FeatureSurface);

    private void OpenMorphingFeature(string section, FacmSurfaceMode mode)
    {
        if (!_morphingSurfaceEnabled) return;
        NavigateToSection(section);
        ShowMorphingSurface(mode, "tool-selected:" + section, true);
    }

    internal void SetDesktopEntryVisible(bool visible)
    {
        if (!_morphingSurfaceEnabled) return;
        if (visible)
        {
            _manualOpenOverride = false;
            ShowMorphingSurface(FacmSurfaceMode.Orb, "desktop-entry-visible", false);
        }
        else
        {
            AppWindow.Hide();
        }
    }

    internal Task ResetMorphingSurfacePositionAsync()
    {
        if (!_morphingSurfaceEnabled) return Task.CompletedTask;
        _surfaceAnchor = null;
        ShowMorphingSurface(FacmSurfaceMode.Orb, "desktop-position-reset", false);
        return Task.CompletedTask;
    }

    internal void SetRuntimeStatus(string badge, string inspector, bool problem = false)
    {
        var normalizedBadge = badge?.Trim() ?? string.Empty;
        var normalizedInspector = inspector?.Trim() ?? string.Empty;
        var signature = string.Join("|", normalizedBadge, normalizedInspector, problem ? "problem" : "ok");
        var statusChanged = !string.Equals(_lastRuntimeStatusSignature, signature, StringComparison.Ordinal);
        _lastRuntimeStatusSignature = signature;

        OrbStatusBadgeText.Text = normalizedBadge;
        OrbStatusBadge.Visibility = string.IsNullOrWhiteSpace(normalizedBadge)
            ? Visibility.Collapsed
            : Visibility.Visible;
        OrbStatusBadge.Background = (Brush)Application.Current.Resources[
            problem ? "FacmErrorBrush" : "FacmSuccessBrush"];
        MatrixStatus.Text = normalizedInspector;
        MatrixInspectorBar.Text = normalizedInspector;
        ToolTipService.SetToolTip(OrbButton, normalizedInspector);

        if (statusChanged && _surfaceStateMachine.Mode == FacmSurfaceMode.Orb)
            ShowTransientRail(normalizedInspector, problem);
    }

    internal IDisposable SuppressOutsideClose()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        Interlocked.Increment(ref _outsideCloseSuppression);
        return new OutsideCloseSuppressionScope(this);
    }

    public void NavigateToSection(string section)
    {
        if (_closed) return;
        var normalized = section switch
        {
            "league" => "league",
            "personalization" => "personalization",
            "settings" => "settings",
            _ => "repair"
        };
        _activeSection = normalized;

        var target = normalized switch
        {
            "league" => LeagueNav,
            "personalization" => PersonalizationNav,
            "settings" => SettingsNav,
            _ => RepairNav
        };
        RootNavigation.SelectedItem = target;
        ApplySection(normalized);
    }

    private void ApplyStaticText()
    {
        var appName = _text.Get(UiTextKeys.AppName);
        var repairText = _text.Get(UiTextKeys.ShellRepairTools);
        var leagueText = _text.Get(UiTextKeys.ShellLeague);
        var personalizationText = _text.Get(UiTextKeys.ShellPersonalization);
        var settingsText = _text.Get(UiTextKeys.ShellMoreSettings);
        MatrixRepairLabel.Text = repairText;
        MatrixLeagueLabel.Text = leagueText;
        MatrixDiagnosticsLabel.Text = _text.Get(UiTextKeys.DiagnosticsTitle);
        MatrixPetLabel.Text = personalizationText;
        MatrixSettingsLabel.Text = settingsText;
        MatrixCleanupLabel.Text = _text.Get(UiTextKeys.Cleanup);
        MatrixInspectorBar.Text = _text.Get(UiTextKeys.ShellStatusReady);
        ChampSelectStatus.Text = _text.Get(UiTextKeys.LeagueStateChampSelect);
        ChampSelectAction.Text = _text.Get(UiTextKeys.ChampSelectWaitingAction);

        Title = appName + " 4.0";
        TitleBarText.Text = appName;
        ProductTitle.Text = appName + " 4.0";
        RepairNav.Content = repairText;
        LeagueNav.Content = leagueText;
        PersonalizationNav.Content = personalizationText;
        SettingsNav.Content = settingsText;
        AutomationProperties.SetName(RepairNav, repairText);
        AutomationProperties.SetHelpText(RepairNav, _text.Get(UiTextKeys.ShellRepairSubtitle));
        AutomationProperties.SetName(LeagueNav, leagueText);
        AutomationProperties.SetHelpText(LeagueNav, _text.Get(UiTextKeys.ShellLeagueSubtitle));
        AutomationProperties.SetName(PersonalizationNav, personalizationText);
        AutomationProperties.SetHelpText(PersonalizationNav, _text.Get(UiTextKeys.ShellPersonalizationSubtitle));
        AutomationProperties.SetName(SettingsNav, settingsText);
        AutomationProperties.SetHelpText(SettingsNav, _text.Get(UiTextKeys.ShellMoreSettingsSubtitle));

        StatusLabel.Text = _text.Get(UiTextKeys.ShellStatusLabel);
        OverviewTitle.Text = _text.Get(UiTextKeys.ShellOverviewTitle);
        OverviewBody.Text = _text.Get(UiTextKeys.ShellOverviewBody);
        StateTitle.Text = _text.Get(UiTextKeys.ShellStateTitle);
        StateBody.Text = _text.Get(UiTextKeys.ShellStateBody);

        RepairToolsTitle.Text = _text.Get(UiTextKeys.RepairToolsTitle);
        RepairToolsDescription.Text = _text.Get(UiTextKeys.RepairToolsDescription);
        RepairPrivilegeLabel.Text = _text.Get(UiTextKeys.RepairPrivilegeLabel);
        RepairDriverCleanupButton.Content = _text.Get(UiTextKeys.RepairDriverCleanup);
        RepairDriverCleanupHint.Text = _text.Get(UiTextKeys.RepairDriverCleanupHint);
        AutomationProperties.SetName(RepairDriverCleanupButton, _text.Get(UiTextKeys.RepairDriverCleanup));
        AutomationProperties.SetHelpText(RepairDriverCleanupButton, _text.Get(UiTextKeys.RepairDriverCleanupHint));

        CleanupDirectoryTitle.Text = _text.Get(UiTextKeys.CleanupDirectoryTitle);
        CleanupDirectoryDescription.Text = _text.Get(UiTextKeys.CleanupDirectoryDescription);
        CleanupDetectButton.Content = _text.Get(UiTextKeys.CleanupAutoDetect);
        CleanupSelectButton.Content = _text.Get(UiTextKeys.CleanupSelectDirectory);
        CleanupPreviewTitle.Text = _text.Get(UiTextKeys.CleanupPreviewTitle);
        CleanupPreviewDescription.Text = _text.Get(UiTextKeys.CleanupPreviewDescription);
        CleanupSafetyHint.Text = _text.Get(UiTextKeys.CleanupSafetyHint);
        CleanupPreviewButton.Content = _text.Get(UiTextKeys.CleanupPreview);
        AutomationProperties.SetName(CleanupDetectButton, _text.Get(UiTextKeys.CleanupAutoDetect));
        AutomationProperties.SetName(CleanupSelectButton, _text.Get(UiTextKeys.CleanupSelectDirectory));
        AutomationProperties.SetName(CleanupPreviewButton, _text.Get(UiTextKeys.CleanupPreview));
        AutomationProperties.SetHelpText(CleanupPreviewButton, _text.Get(UiTextKeys.CleanupPreviewDescription));

        LeagueStateLabel.Text = _text.Get(UiTextKeys.LeagueWorkbenchStateLabel);
        LeagueBudgetLabel.Text = _text.Get(UiTextKeys.LeagueWorkbenchBudgetLabel);
        ApplyWorkbenchSectionText(
            LeagueWorkbenchCatalog.Get(LeagueWorkbenchCatalog.Match),
            LeagueMatchTitle,
            LeagueMatchDescription);
        ApplyWorkbenchSectionText(
            LeagueWorkbenchCatalog.Get(LeagueWorkbenchCatalog.Strategy),
            LeagueStrategyTitle,
            LeagueStrategyDescription);
        ApplyWorkbenchSectionText(
            LeagueWorkbenchCatalog.Get(LeagueWorkbenchCatalog.Automation),
            LeagueAutomationTitle,
            LeagueAutomationDescription);

        var diagnosticsSummaryLabel = _text.Get(UiTextKeys.DiagnosticsSummaryLabel);
        var diagnosticsRefresh = _text.Get(UiTextKeys.DiagnosticsRefresh);
        var diagnosticsCopy = _text.Get(UiTextKeys.DiagnosticsCopySummary);
        var diagnosticsExport = _text.Get(UiTextKeys.DiagnosticsExportBundle);
        DiagnosticsTitle.Text = _text.Get(UiTextKeys.DiagnosticsTitle);
        DiagnosticsSubtitle.Text = _text.Get(UiTextKeys.DiagnosticsSubtitle);
        DiagnosticsSummaryLabel.Text = diagnosticsSummaryLabel;
        DiagnosticsRefreshButton.Content = diagnosticsRefresh;
        DiagnosticsCopyButton.Content = diagnosticsCopy;
        DiagnosticsExportButton.Content = diagnosticsExport;
        DiagnosticsStatus.Text = _text.Get(UiTextKeys.DiagnosticsStatusReady);
        AutomationProperties.SetName(DiagnosticsSummaryText, diagnosticsSummaryLabel);
        AutomationProperties.SetHelpText(DiagnosticsSummaryText, _text.Get(UiTextKeys.DiagnosticsSubtitle));
        AutomationProperties.SetName(DiagnosticsRefreshButton, diagnosticsRefresh);
        AutomationProperties.SetHelpText(DiagnosticsRefreshButton, _text.Get(UiTextKeys.DiagnosticsRefreshHelp));
        AutomationProperties.SetName(DiagnosticsCopyButton, diagnosticsCopy);
        AutomationProperties.SetHelpText(DiagnosticsCopyButton, _text.Get(UiTextKeys.DiagnosticsCopySummaryHelp));
        AutomationProperties.SetName(DiagnosticsExportButton, diagnosticsExport);
        AutomationProperties.SetHelpText(DiagnosticsExportButton, _text.Get(UiTextKeys.DiagnosticsExportBundleHelp));

        ApplySection("repair");
        ApplyStatus(UiTextKeys.ShellStatusReady);
    }

    private void InitializeInspector()
    {
        AttachInspector(RepairNav, _text.Get(UiTextKeys.ShellRepairSubtitle));
        AttachInspector(LeagueNav, _text.Get(UiTextKeys.ShellLeagueSubtitle));
        AttachInspector(PersonalizationNav, _text.Get(UiTextKeys.ShellPersonalizationSubtitle));
        AttachInspector(SettingsNav, _text.Get(UiTextKeys.ShellMoreSettingsSubtitle));
        AttachInspector(RepairDriverCleanupButton, _text.Get(UiTextKeys.RepairDriverCleanupHint));
        AttachInspector(RepairFixWindowButton, _text.Get(UiTextKeys.RepairFixWindowHint));
        AttachInspector(CleanupDetectButton, _text.Get(UiTextKeys.CleanupDirectoryDescription));
        AttachInspector(CleanupPreviewButton, _text.Get(UiTextKeys.CleanupPreviewDescription));
        AttachInspector(LeagueAutoMatchmakingToggle, _text.Get(UiTextKeys.LeagueAutoMatchmakingHint));
        AttachInspector(LeagueAutoAcceptToggle, _text.Get(UiTextKeys.LeagueAutoAcceptHint));
        AttachInspector(DiagnosticsRefreshButton, _text.Get(UiTextKeys.DiagnosticsRefreshHelp));
        AttachInspector(DiagnosticsExportButton, _text.Get(UiTextKeys.DiagnosticsExportBundleHelp));
        ApplyInspectorDefault();
    }

    private void AttachInspector(UIElement element, string text)
    {
        element.PointerEntered += (_, _) =>
        {
            _inspectorHoverElement = element;
            InspectorBar.Text = text;
        };
        element.PointerExited += (_, _) =>
        {
            if (ReferenceEquals(_inspectorHoverElement, element)) _inspectorHoverElement = null;
            if (_inspectorFocusElement is null) ApplyInspectorDefault();
        };
        element.GotFocus += (_, _) =>
        {
            _inspectorFocusElement = element;
            InspectorBar.Text = text;
        };
        element.LostFocus += (_, _) =>
        {
            if (ReferenceEquals(_inspectorFocusElement, element)) _inspectorFocusElement = null;
            if (_inspectorHoverElement is null) ApplyInspectorDefault();
        };
    }

    private void ApplyInspectorDefault()
    {
        if (_closed || _inspectorFocusElement is not null || _inspectorHoverElement is not null) return;
        var state = _leagueWorkbench.LeagueState.ToString();
        InspectorBar.Text = "LCU · " + state + " · " + _text.Get(UiTextKeys.ShellStatusReady);
    }

    private void ApplyWorkbenchSectionText(
        LeagueWorkbenchSection section,
        TextBlock title,
        TextBlock description)
    {
        title.Text = _text.Get(section.TitleTextKey);
        description.Text = _text.Get(section.DescriptionTextKey);
    }

    private async void OnRootNavigationLoaded(object sender, RoutedEventArgs e)
    {
        RootNavigation.Loaded -= OnRootNavigationLoaded;
        try
        {
            var currentVersion = typeof(App).Assembly.GetName().Version ?? new Version(4, 0, 0);
            await _controlCenter.RefreshAsync(currentVersion);
            ApplyStatus(_controlCenter.StatusTextKey);
            await EnsureCleanupInitializedAsync();
        }
        catch (Exception)
        {
            ApplyStatus(UiTextKeys.ShellStatusUnavailable);
        }
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is not NavigationViewItem item) return;
        ApplySection(item.Tag?.ToString() ?? "repair");
    }

    private void ApplySection(string tag)
    {
        var isRepair = string.Equals(tag, "repair", StringComparison.Ordinal);
        var isLeague = string.Equals(tag, "league", StringComparison.Ordinal);
        var isSettings = string.Equals(tag, "settings", StringComparison.Ordinal);
        var (titleKey, subtitleKey) = tag switch
        {
            "league" => (UiTextKeys.ShellLeague, UiTextKeys.ShellLeagueSubtitle),
            "personalization" => (UiTextKeys.ShellPersonalization, UiTextKeys.ShellPersonalizationSubtitle),
            "settings" => (UiTextKeys.ShellMoreSettings, UiTextKeys.ShellMoreSettingsSubtitle),
            _ => (UiTextKeys.ShellRepairTools, UiTextKeys.ShellRepairSubtitle)
        };
        SectionTitle.Text = _text.Get(titleKey);
        SectionSubtitle.Text = _text.Get(subtitleKey);
        CleanupPanel.Visibility = isRepair ? Visibility.Visible : Visibility.Collapsed;
        GeneralOverviewGrid.Visibility = !isRepair && !isLeague ? Visibility.Visible : Visibility.Collapsed;
        LeagueWorkbenchPanel.Visibility = isLeague ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsPanel.Visibility = isSettings ? Visibility.Visible : Visibility.Collapsed;
        if (isRepair)
        {
            ApplyRepairToolsState();
            _ = EnsureCleanupInitializedAsync();
        }
        if (isLeague) ApplyLeagueRuntimeState();
        if (isSettings && !_diagnosticsLoaded) _ = RefreshDiagnosticsAsync();
        ApplyInspectorDefault();
    }

    private async Task EnsureCleanupInitializedAsync()
    {
        if (_cleanupInitialized || _cleanupUiBusy || _closed) return;
        _cleanupUiBusy = true;
        try
        {
            await _cleanupCenter.InitializeAsync();
            _cleanupInitialized = true;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cleanupUiBusy = false;
            ApplyCleanupRuntimeState();
            ApplyRepairToolsState();
        }
    }

    private void OnRepairDriverCleanupClick(object sender, RoutedEventArgs args)
    {
        if (_repairTools.IsBusy) return;
        _ = _repairTools.LaunchDriverCleanup();
        ApplyRepairToolsState();
    }

    private void ApplyRepairToolsState()
    {
        if (_closed) return;
        RepairPrivilegeStatus.Text = _text.Get(
            _cleanupCenter.IsAdministrator
                ? UiTextKeys.RepairPrivilegeAdministrator
                : UiTextKeys.RepairPrivilegeStandard);
        RepairToolStatus.Text = _text.Get(_repairTools.StatusTextKey);
        RepairToolDetail.Text = _repairTools.StatusDetail;
        RepairDriverCleanupButton.IsEnabled = !_repairTools.IsBusy;
    }

    private async void OnCleanupDetectClick(object sender, RoutedEventArgs args)
    {
        if (_cleanupUiBusy) return;
        _cleanupUiBusy = true;
        try
        {
            await _cleanupCenter.DetectAsync();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cleanupUiBusy = false;
            ApplyCleanupRuntimeState();
        }
    }

    private async void OnCleanupSelectClick(object sender, RoutedEventArgs args)
    {
        if (_cleanupUiBusy) return;
        using var outsideCloseSuppression = SuppressOutsideClose();
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;

        _cleanupUiBusy = true;
        try
        {
            await _cleanupCenter.SetSelectedPathAsync(folder.Path);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cleanupUiBusy = false;
            ApplyCleanupRuntimeState();
        }
    }

    private async void OnCleanupPreviewClick(object sender, RoutedEventArgs args)
    {
        if (_cleanupUiBusy) return;
        _cleanupUiBusy = true;
        CleanupProgressBar.IsIndeterminate = true;
        CleanupProgressBar.Visibility = Visibility.Visible;
        try
        {
            var plan = await _cleanupCenter.PreviewAsync();
            ApplyCleanupRuntimeState();
            if (plan is not null) await ShowCleanupReviewAsync(plan);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cleanupUiBusy = false;
            CleanupProgressBar.Visibility = Visibility.Collapsed;
            ApplyCleanupRuntimeState();
        }
    }

    private async Task ShowCleanupReviewAsync(CleanupPlan plan)
    {
        var summary = new TextBlock
        {
            Text = BuildCleanupSummary(plan),
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmBodyTextStyle"]
        };
        var targets = new ListView
        {
            MaxHeight = 360,
            SelectionMode = ListViewSelectionMode.None,
            ItemsSource = plan.Targets.Select(FormatCleanupTarget).ToArray()
        };
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = _text.Get(UiTextKeys.CleanupPreviewDescription),
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["FacmMutedTextStyle"]
        });
        content.Children.Add(summary);
        content.Children.Add(targets);

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = _text.Get(UiTextKeys.CleanupConfirmTitle),
            Content = content,
            PrimaryButtonText = _text.Get(UiTextKeys.CleanupConfirmPrimary),
            CloseButtonText = _text.Get(UiTextKeys.CleanupCancel),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = plan.DeletableTargets.Count > 0
        };
        using var outsideCloseSuppression = SuppressOutsideClose();
        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        if (_cleanupCenter.RequiresElevation)
        {
            await ShowCleanupElevationAsync();
            return;
        }

        await ExecuteCleanupAsync();
    }

    private async Task ShowCleanupElevationAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = _text.Get(UiTextKeys.CleanupRequiresAdmin),
            Content = _text.Get(UiTextKeys.CleanupConfirmBody),
            PrimaryButtonText = _text.Get(UiTextKeys.CleanupRestartElevated),
            CloseButtonText = _text.Get(UiTextKeys.CleanupCancel),
            DefaultButton = ContentDialogButton.Primary
        };
        using var outsideCloseSuppression = SuppressOutsideClose();
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var started = _cleanupCenter.RestartElevatedForCleanup();
        if (!started)
        {
            CleanupOperationStatus.Text = _text.Get(UiTextKeys.CleanupFailed);
            return;
        }

        CleanupOperationStatus.Text = _text.Get(UiTextKeys.CleanupRequiresAdmin);
        _ = DispatcherQueue.TryEnqueue(() => Application.Current.Exit());
    }

    private async Task ExecuteCleanupAsync()
    {
        if (_cleanupUiBusy || _cleanupCenter.CurrentPlan is null) return;
        _cleanupUiBusy = true;
        CleanupProgressBar.IsIndeterminate = false;
        CleanupProgressBar.Minimum = 0;
        CleanupProgressBar.Maximum = Math.Max(1, _cleanupCenter.CurrentPlan.Targets.Count);
        CleanupProgressBar.Value = 0;
        CleanupProgressBar.Visibility = Visibility.Visible;
        var progress = new Progress<CleanupProgress>(item =>
        {
            CleanupProgressBar.Maximum = Math.Max(1, item.TotalTargets);
            CleanupProgressBar.Value = Math.Min(item.CompletedTargets, item.TotalTargets);
            CleanupOperationStatus.Text = item.CurrentTarget;
        });
        try
        {
            var result = await _cleanupCenter.ExecuteConfirmedAsync(confirmed: true, progress);
            ApplyCleanupRuntimeState();
            if (result is null) return;
            var resultDialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = result.Success ? _text.Get(UiTextKeys.CleanupComplete) : _text.Get(UiTextKeys.CleanupFailed),
                Content = BuildCleanupResult(result),
                CloseButtonText = _text.Get(UiTextKeys.CleanupConfirmPrimary)
            };
            using var outsideCloseSuppression = SuppressOutsideClose();
            await resultDialog.ShowAsync();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cleanupUiBusy = false;
            CleanupProgressBar.Visibility = Visibility.Collapsed;
            ApplyCleanupRuntimeState();
        }
    }

    private string BuildCleanupSummary(CleanupPlan plan)
    {
        var summary = plan.Summary;
        var parts = new[]
        {
            $"{summary.TargetCount} {_text.Get(UiTextKeys.CleanupTargetSummary)}",
            $"{summary.FileCount} files / {summary.DirectoryCount} folders",
            FormatBytes(summary.EstimatedBytes),
            $"{summary.BlockedCount} {_text.Get(UiTextKeys.CleanupBlocked)}"
        };
        return string.Join(" · ", parts);
    }

    private static string FormatCleanupTarget(CleanupTarget target)
    {
        var detail = target.IsBlocked
            ? $"BLOCKED · {target.BlockedReason}"
            : $"{target.FileCount} files · {target.DirectoryCount} folders · {FormatBytes(target.EstimatedBytes)}";
        return $"{target.FullPath}\n{detail}";
    }

    private string BuildCleanupResult(CleanupResult result)
    {
        var lines = new List<string>
        {
            $"Deleted files: {result.DeletedFiles}",
            $"Deleted folders: {result.DeletedDirectories}"
        };
        if (result.Failures.Count > 0)
        {
            lines.Add($"Failures: {result.Failures.Count}");
            lines.AddRange(result.Failures);
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024L * 1024L) return $"{bytes / 1024d:F1} KB";
        if (bytes < 1024L * 1024L * 1024L) return $"{bytes / (1024d * 1024d):F1} MB";
        return $"{bytes / (1024d * 1024d * 1024d):F2} GB";
    }

    private void ApplyCleanupRuntimeState()
    {
        CleanupPathText.Text = string.IsNullOrWhiteSpace(_cleanupCenter.GamePath)
            ? _text.Get(UiTextKeys.CleanupDirectoryMissing)
            : _cleanupCenter.GamePath;
        CleanupDirectoryStatus.Text = _text.Get(_cleanupCenter.StatusTextKey);
        CleanupDirectoryDetail.Text = _cleanupCenter.StatusDetail;
        CleanupOperationStatus.Text = _text.Get(_cleanupCenter.StatusTextKey);
        CleanupDetectButton.IsEnabled = !_cleanupUiBusy && !_cleanupCenter.IsBusy;
        CleanupSelectButton.IsEnabled = !_cleanupUiBusy && !_cleanupCenter.IsBusy;
        CleanupPreviewButton.IsEnabled = !_cleanupUiBusy && !_cleanupCenter.IsBusy && _cleanupCenter.IsGamePathValid;
    }

    private void OnCleanupPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_closed) return;
        if (DispatcherQueue.HasThreadAccess)
        {
            ApplyCleanupRuntimeState();
            ApplyRepairToolsState();
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed) return;
            ApplyCleanupRuntimeState();
            ApplyRepairToolsState();
        });
    }

    private void OnLeagueWorkbenchPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_closed) return;
        if (args.PropertyName is not (
            nameof(LeagueWorkbenchViewModel.LeagueState) or
            nameof(LeagueWorkbenchViewModel.LeagueStateTextKey) or
            nameof(LeagueWorkbenchViewModel.BudgetName)))
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(ApplyLeagueRuntimeState);
    }

    private void ApplyLeagueRuntimeState()
    {
        if (_closed) return;
        LeagueStateValue.Text = _text.Get(_leagueWorkbench.LeagueStateTextKey);
        LeagueBudgetValue.Text = _leagueWorkbench.BudgetName;
    }

    private async void OnDiagnosticsRefreshClick(object sender, RoutedEventArgs args) =>
        await RefreshDiagnosticsAsync();

    private async void OnDiagnosticsCopyClick(object sender, RoutedEventArgs args)
    {
        if (_diagnosticsBusy) return;
        if (!_diagnosticsLoaded) await RefreshDiagnosticsAsync();
        if (string.IsNullOrWhiteSpace(_diagnosticsCenter.Summary)) return;

        try
        {
            var package = new DataPackage();
            package.SetText(_diagnosticsCenter.Summary);
            Clipboard.SetContent(package);
            DiagnosticsStatus.Text = _text.Get(UiTextKeys.DiagnosticsStatusCopied);
        }
        catch (Exception)
        {
            DiagnosticsStatus.Text = _text.Get(UiTextKeys.DiagnosticsStatusFailed);
        }
    }

    private async void OnDiagnosticsExportClick(object sender, RoutedEventArgs args)
    {
        if (_diagnosticsBusy) return;
        SetDiagnosticsBusy(true);
        try
        {
            _ = await _diagnosticsCenter.ExportAsync();
            _diagnosticsLoaded = true;
            DiagnosticsSummaryText.Text = _diagnosticsCenter.Summary;
            DiagnosticsStatus.Text = _text.Get(UiTextKeys.DiagnosticsStatusExported);
        }
        catch (Exception)
        {
            DiagnosticsStatus.Text = _text.Get(UiTextKeys.DiagnosticsStatusFailed);
        }
        finally
        {
            SetDiagnosticsBusy(false);
        }
    }

    private async Task RefreshDiagnosticsAsync()
    {
        if (_diagnosticsBusy) return;
        SetDiagnosticsBusy(true);
        try
        {
            DiagnosticsSummaryText.Text = await _diagnosticsCenter.RefreshAsync();
            _diagnosticsLoaded = true;
            DiagnosticsStatus.Text = _text.Get(UiTextKeys.DiagnosticsStatusRefreshed);
        }
        catch (Exception)
        {
            DiagnosticsStatus.Text = _text.Get(UiTextKeys.DiagnosticsStatusFailed);
        }
        finally
        {
            SetDiagnosticsBusy(false);
        }
    }

    private void SetDiagnosticsBusy(bool busy)
    {
        _diagnosticsBusy = busy;
        DiagnosticsRefreshButton.IsEnabled = !busy;
        DiagnosticsCopyButton.IsEnabled = !busy;
        DiagnosticsExportButton.IsEnabled = !busy;
    }

    private void ApplyStatus(string key)
    {
        StatusValue.Text = _text.Get(key);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (_closed) return;
        _closed = true;
        _outsideClickWatcher.Dispose();
        _transientRailTimer?.Stop();
        _transientRailTimer = null;
        CancelChampSelectIdentityLoad();
        RootNavigation.Loaded -= OnRootNavigationLoaded;
        _cleanupCenter.PropertyChanged -= OnCleanupPropertyChanged;
        _leagueWorkbench.PropertyChanged -= OnLeagueWorkbenchPropertyChanged;
        _surfaceStateMachine.Transitioned -= OnSurfaceTransitioned;
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

    private sealed class OutsideCloseSuppressionScope(MainWindow owner) : IDisposable
    {
        private MainWindow? _owner = owner;
        private readonly IDisposable _modalScope = owner._surfaceStateMachine.EnterModalScope();

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.ReleaseOutsideCloseSuppression();
            _modalScope.Dispose();
        }
    }

    private static int ToInt32(double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        return (int)Math.Clamp(rounded, int.MinValue, int.MaxValue);
    }
}
