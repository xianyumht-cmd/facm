using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace FACM.MachineCat3DPrototype;

internal sealed class MachineCat3DWindow : Window
{
    private readonly RigidPartRig _rig;
    private readonly ModelVisual3D _facingRoot;
    private readonly DesktopMotionController _motion = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly TextBlock _debug;
    private readonly bool _smokeTest;
    private readonly string _modelLabel;
    private double _lastSeconds;
    private double _stateStarted;
    private int _renderedFrames;
    private MotionState _state = MotionState.Idle;
    private bool _autoMotion = true;

    public MachineCat3DWindow(RigidModel model, string modelLabel, bool smokeTest = false)
    {
        _smokeTest = smokeTest;
        _modelLabel = modelLabel;
        Width = 350;
        Height = 380;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        Title = "FACM Machine Cat 3D Desktop Motion Prototype";

        var root = new Grid { Background = Brushes.Transparent };
        Content = root;

        var viewport = new Viewport3D
        {
            ClipToBounds = false,
            IsHitTestVisible = false
        };
        root.Children.Add(viewport);

        var cameraPosition = new Point3D(4.65, 2.45, 8.25);
        var target = new Point3D(-0.03, 1.25, 0.0);
        viewport.Camera = new PerspectiveCamera
        {
            Position = cameraPosition,
            LookDirection = target - cameraPosition,
            UpDirection = new Vector3D(0, 1, 0),
            FieldOfView = 29d,
            NearPlaneDistance = 0.1d,
            FarPlaneDistance = 100d
        };

        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(Color.FromRgb(116, 116, 122)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(248, 248, 248), new Vector3D(-0.45, -0.72, -1.0)));
        lights.Children.Add(new DirectionalLight(Color.FromRgb(145, 175, 205), new Vector3D(0.75, -0.15, 0.45)));
        viewport.Children.Add(new ModelVisual3D { Content = lights });

        _rig = new RigidPartRig(model);
        _facingRoot = new ModelVisual3D();
        _facingRoot.Children.Add(_rig.Visual);
        viewport.Children.Add(_facingRoot);

        _debug = new TextBlock
        {
            Text = BuildDebugText(),
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(175, 18, 18, 22)),
            FontSize = 12d,
            Padding = new Thickness(8, 5, 8, 5),
            Margin = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        root.Children.Add(_debug);

        Loaded += (_, _) =>
        {
            PositionOnGroundNearBottomRight();
            _motion.Reset(SystemParameters.WorkArea, Width, Height, Left, _clock.Elapsed.TotalSeconds);
        };
        KeyDown += OnKeyDown;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        CompositionTarget.Rendering += OnRendering;
        Closed += (_, _) => CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var delta = now - _lastSeconds;
        _lastSeconds = now;
        if (!double.IsFinite(delta) || delta <= 0d) delta = 1d / 120d;
        if (delta > 0.05d) delta = 0.05d;

        if (_autoMotion && !_smokeTest)
        {
            var frame = _motion.Step(delta, now, SystemParameters.WorkArea, Width, Height, Left);
            Left = frame.Left;
            Top = frame.Top;

            if (_state != frame.State)
            {
                _state = frame.State;
                _stateStarted = now;
            }

            // Automatic direction changes rotate the whole 3D character continuously.
            // During that short turn we keep the limb rig idle instead of playing the
            // old full-360 showcase turn animation.
            var rigState = frame.State == MotionState.Turn ? MotionState.Idle : frame.State;
            _rig.Apply(rigState, Math.Max(0d, now - _stateStarted));
            _facingRoot.Transform = new RotateTransform3D(
                new AxisAngleRotation3D(new Vector3D(0, 1, 0), frame.FacingYaw),
                new Point3D(-0.03, 1.30, 0.0));

            if (_debug.Visibility == Visibility.Visible)
                _debug.Text = BuildDebugText(frame);
        }
        else
        {
            _rig.Apply(_state, Math.Max(0d, now - _stateStarted));
            _facingRoot.Transform = Transform3D.Identity;
        }

        _renderedFrames++;
        if (_smokeTest && _renderedFrames >= 5)
            Close();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.A:
                _autoMotion = !_autoMotion;
                if (_autoMotion)
                {
                    _motion.Reset(SystemParameters.WorkArea, Width, Height, Left, _clock.Elapsed.TotalSeconds);
                    _state = MotionState.Idle;
                    _stateStarted = _clock.Elapsed.TotalSeconds;
                }
                break;
            case Key.D1:
            case Key.NumPad1:
                SetManualState(MotionState.Idle);
                break;
            case Key.D2:
            case Key.NumPad2:
                SetManualState(MotionState.Walk);
                break;
            case Key.D3:
            case Key.NumPad3:
                SetManualState(MotionState.Run);
                break;
            case Key.D4:
            case Key.NumPad4:
                SetManualState(MotionState.Turn);
                break;
            case Key.D:
            case Key.F1:
                _debug.Visibility = _debug.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                break;
            case Key.Escape:
                Close();
                break;
        }

        if (_debug.Visibility == Visibility.Visible)
            _debug.Text = BuildDebugText();
    }

    private void SetManualState(MotionState state)
    {
        _autoMotion = false;
        if (_state == state) return;
        _state = state;
        _stateStarted = _clock.Elapsed.TotalSeconds;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        _autoMotion = false;
        try { DragMove(); }
        catch (InvalidOperationException) { }
    }

    private void PositionOnGroundNearBottomRight()
    {
        var area = SystemParameters.WorkArea;
        Left = Math.Max(area.Left + 14d, area.Right - Width - 42d);
        Top = Math.Max(area.Top, area.Bottom - Height + 48d);
    }

    private string BuildDebugText(DesktopMotionFrame? frame = null)
    {
        var speed = frame?.Speed ?? _motion.Speed;
        var target = frame?.TargetLeft ?? _motion.TargetLeft;
        return $"FACM 3D Desktop Motion | {_modelLabel}\n" +
               $"模式: {(_autoMotion ? "AUTO" : "MANUAL")} | 状态: {_state} | speed={speed:0.0}px/s | targetX={target:0}\n" +
               "A 自动巡走  1 Idle  2 Walk  3 Run  4 Turn  D 调试  Esc 退出";
    }
}
