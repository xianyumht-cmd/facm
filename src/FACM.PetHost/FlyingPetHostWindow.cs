using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace FACM.PetHost;

internal sealed class FlyingPetHostWindow : Window
{
    private const double PetWindowSize = 164d;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;

    private readonly PetHostIpc _ipc;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Random _random = new(unchecked(Environment.TickCount * 397));
    private readonly Grid _root;

    private FrameworkElement? _petVisual;
    private FlyingPetProfile _profile;
    private string _petId;
    private double _lastTickSeconds;
    private double _stateUntilSeconds;
    private double _animationSeconds;
    private double _x;
    private double _y;
    private double _vx;
    private double _vy;
    private double _targetVx;
    private double _targetVy;
    private double _headingDegrees;
    private bool _headingInitialized;
    private bool _facingRight = true;
    private bool _positionInitialized;
    private bool _dragging;
    private bool _dragMoved;
    private int _dragCursorX;
    private int _dragCursorY;
    private double _dragWindowX;
    private double _dragWindowY;
    private bool _closed;

    public FlyingPetHostWindow(PetHostIpc ipc, string? petId)
    {
        _ipc = ipc ?? throw new ArgumentNullException(nameof(ipc));
        _petId = FlyingPetProfiles.Contains(petId) ? petId! : "greenfly";
        _profile = FlyingPetProfiles.Get(_petId);

        Title = PetHostUiText.Translate("FACM 桌面宠物");
        Width = PetWindowSize;
        Height = PetWindowSize;
        MinWidth = MaxWidth = Width;
        MinHeight = MaxHeight = Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        _root = new Grid
        {
            Width = PetWindowSize,
            Height = PetWindowSize,
            Background = Brushes.Transparent
        };
        Content = _root;
        RebuildVisual();

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += OnClosed;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseRightButtonDown += OnMouseRightButtonDown;

        _lastTickSeconds = _clock.Elapsed.TotalSeconds;
        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _timer.Tick += OnTick;
        ChooseNewMotion(forceMove: true);
        _ipc.Start(HandleCommand);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        ResetToPrimaryScreen();
        _timer.Start();
        await _ipc.SendEventAsync("ready", "flying-runtime;pet=" + _petId).ConfigureAwait(false);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
            style |= WsExToolWindow | WsExNoActivate;
            _ = SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style));
        }
        catch
        {
            // ShowInTaskbar=false + ShowActivated=false remain the fail-soft path.
        }
    }

    private void HandleCommand(string line)
    {
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            var parts = line.Split('|', 2);
            var command = parts[0].Trim().ToLowerInvariant();
            switch (command)
            {
                case "activate":
                    if (parts.Length > 1 && FlyingPetProfiles.Contains(parts[1]))
                        SetPet(parts[1]);
                    if (!IsVisible) Show();
                    Topmost = true;
                    break;
                case "reset":
                    ResetToPrimaryScreen();
                    break;
                case "stop":
                    Close();
                    break;
                case "ping":
                    _ = _ipc.SendEventAsync("pong");
                    break;
            }
        }));
    }

    private void SetPet(string petId)
    {
        _petId = petId;
        _profile = FlyingPetProfiles.Get(petId);
        _vx = _vy = _targetVx = _targetVy = 0;
        _headingDegrees = 0;
        _headingInitialized = false;
        _animationSeconds = 0;
        RebuildVisual();
        ChooseNewMotion(forceMove: true);
    }

    private void ResetToPrimaryScreen()
    {
        var area = Forms.Screen.PrimaryScreen?.WorkingArea ?? Forms.SystemInformation.WorkingArea;
        SetPetLocation(
            area.Left + (area.Width - Width) / 2d,
            area.Top + (area.Height - Height) / 2d);
        _vx = _vy = 0;
        ChooseNewMotion(forceMove: true);
        Topmost = true;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_closed || !IsVisible) return;
        var now = _clock.Elapsed.TotalSeconds;
        var delta = now - _lastTickSeconds;
        _lastTickSeconds = now;
        if (delta <= 0) return;
        if (delta > 0.050) delta = 0.050;
        _animationSeconds += delta;

        if (!_dragging)
        {
            if (now >= _stateUntilSeconds) ChooseNewMotion(forceMove: false);
            SmoothVelocity(delta);
            MoveOneFrame(delta);
        }
        UpdateVisualPose();
    }

    private void ChooseNewMotion(bool forceMove)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var idle = !forceMove && _random.NextDouble() < _profile.IdleChance;
        var duration = idle
            ? RandomBetween(_profile.IdleMinSeconds, _profile.IdleMaxSeconds)
            : RandomBetween(_profile.MoveMinSeconds, _profile.MoveMaxSeconds);
        _stateUntilSeconds = now + duration;

        if (idle)
        {
            _targetVx = 0;
            _targetVy = 0;
            return;
        }

        var angle = _random.NextDouble() * Math.PI * 2d;
        var baseSpeed = RandomBetween(_profile.MinBaseSpeed, _profile.MaxBaseSpeed);
        var speed = baseSpeed * Math.Max(0.55d, _profile.SpeedMultiplier);
        _targetVx = Math.Cos(angle) * speed;
        _targetVy = Math.Sin(angle) * speed;

        if (!_headingInitialized)
        {
            _headingDegrees = HeadingDegreesForVector(_targetVx, _targetVy);
            _facingRight = _targetVx >= 0;
            _headingInitialized = true;
        }
    }

    private void SmoothVelocity(double dt)
    {
        var velocityBlend = 1d - Math.Exp(-_profile.VelocityResponse * dt);
        _vx += (_targetVx - _vx) * velocityBlend;
        _vy += (_targetVy - _vy) * velocityBlend;

        if (string.Equals(_petId, "real-bee", StringComparison.OrdinalIgnoreCase))
        {
            var facingThreshold = Math.Max(8d, Math.Abs(_vy) * 0.22d);
            if (Math.Abs(_vx) >= facingThreshold) _facingRight = _vx >= 0;
            var horizontalReference = Math.Max(Math.Abs(_vx), 24d);
            var pitch = Math.Atan2(_vy, horizontalReference) * 180d / Math.PI;
            pitch = Math.Clamp(pitch, -32d, 32d);
            var blend = 1d - Math.Exp(-_profile.HeadingResponse * dt);
            _headingDegrees += (pitch - _headingDegrees) * blend;
            return;
        }

        var target = HeadingDegreesForVector(
            Math.Abs(_vx) < 1d ? _targetVx : _vx,
            Math.Abs(_vy) < 1d ? _targetVy : _vy);
        var delta = ShortestAngleDelta(_headingDegrees, target);
        var headingBlend = 1d - Math.Exp(-_profile.HeadingResponse * dt);
        _headingDegrees = NormalizeDegrees(_headingDegrees + delta * headingBlend);
        _facingRight = true;
    }

    private void MoveOneFrame(double dt)
    {
        EnsurePosition();
        var jitterX = Math.Sin(_animationSeconds * _profile.JitterXFrequency) * _profile.JitterXAmplitude;
        var jitterY = Math.Cos(_animationSeconds * _profile.JitterYFrequency) * _profile.JitterYAmplitude;

        // Preserve 3.5.15 semantics: the flying pet roams freely instead of bouncing against an
        // invisible monitor wall. "复位桌面位置" is the explicit recovery path.
        _x += (_vx + jitterX) * dt;
        _y += (_vy + jitterY) * dt;
        Left = _x;
        Top = _y;
    }

    private void EnsurePosition()
    {
        if (!_positionInitialized) ResetToPrimaryScreen();
    }

    private void SetPetLocation(double x, double y)
    {
        _x = x;
        _y = y;
        _positionInitialized = true;
        Left = x;
        Top = y;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var cursor = Forms.Control.MousePosition;
        _dragging = true;
        _dragMoved = false;
        _dragCursorX = cursor.X;
        _dragCursorY = cursor.Y;
        _dragWindowX = Left;
        _dragWindowY = Top;
        Mouse.Capture(this);
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var cursor = Forms.Control.MousePosition;
        var dx = cursor.X - _dragCursorX;
        var dy = cursor.Y - _dragCursorY;
        if (Math.Abs(dx) + Math.Abs(dy) > 4) _dragMoved = true;
        SetPetLocation(_dragWindowX + dx, _dragWindowY + dy);
        e.Handled = true;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        Mouse.Capture(null);
        _vx = _vy = 0;
        _stateUntilSeconds = _clock.Elapsed.TotalSeconds + 0.55d;
        e.Handled = true;
        if (!_dragMoved) _ = _ipc.SendEventAsync("click");
    }

    private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _ = _ipc.SendEventAsync("right-click");
    }

    private void RebuildVisual()
    {
        _root.Children.Clear();
        _petVisual = CreatePetVisual(_petId);
        _petVisual.HorizontalAlignment = HorizontalAlignment.Center;
        _petVisual.VerticalAlignment = VerticalAlignment.Center;
        _root.Children.Add(_petVisual);
        UpdateVisualPose();
    }

    private void UpdateVisualPose()
    {
        var visual = _petVisual;
        if (visual is null) return;
        var wingPulse = 1d + Math.Sin(_animationSeconds * 22d) * 0.035d;
        var scale = Math.Clamp(_profile.VisualScale * 1.35d, 0.62d, 1.15d);
        var transforms = new TransformGroup();
        transforms.Children.Add(new ScaleTransform(
            (string.Equals(_petId, "real-bee", StringComparison.OrdinalIgnoreCase) && !_facingRight ? -1d : 1d) * scale,
            scale * wingPulse));
        transforms.Children.Add(new RotateTransform(_headingDegrees));
        visual.RenderTransformOrigin = new Point(0.5, 0.5);
        visual.RenderTransform = transforms;
    }

    private static FrameworkElement CreatePetVisual(string petId)
    {
        return petId.ToLowerInvariant() switch
        {
            "bee" => CreateBee(realistic: false),
            "real-bee" => CreateBee(realistic: true),
            "dragonfly" => CreateDragonfly(),
            "butterfly" => CreateButterfly(),
            "moth" => CreateMoth(),
            _ => CreateGreenFly()
        };
    }

    private static Grid CreateGreenFly()
    {
        var grid = BaseVisual(98, 74);
        grid.Children.Add(Ellipse(44, 28, 20, 23, Color.FromRgb(45, 80, 48)));
        grid.Children.Add(Ellipse(40, 20, 48, 27, Color.FromArgb(120, 205, 235, 220)));
        grid.Children.Add(Ellipse(40, 20, 48, 43, Color.FromArgb(120, 205, 235, 220)));
        grid.Children.Add(Ellipse(22, 22, 66, 26, Color.FromRgb(75, 115, 67)));
        grid.Children.Add(Ellipse(8, 9, 79, 28, Colors.Black));
        grid.Children.Add(Ellipse(8, 9, 79, 39, Colors.Black));
        return grid;
    }

    private static Grid CreateBee(bool realistic)
    {
        var grid = BaseVisual(108, 78);
        var wing = realistic ? Color.FromArgb(105, 225, 235, 230) : Color.FromArgb(125, 220, 242, 242);
        grid.Children.Add(Ellipse(48, 22, 33, 12, wing));
        grid.Children.Add(Ellipse(48, 22, 33, 45, wing));
        grid.Children.Add(Ellipse(56, 28, 25, 26, realistic ? Color.FromRgb(184, 131, 37) : Color.FromRgb(235, 181, 42)));
        grid.Children.Add(Rect(5, 26, 39, 27, Color.FromRgb(58, 43, 26)));
        grid.Children.Add(Rect(5, 26, 51, 27, Color.FromRgb(58, 43, 26)));
        grid.Children.Add(Ellipse(28, 30, 74, 25, Color.FromRgb(74, 54, 31)));
        return grid;
    }

    private static Grid CreateDragonfly()
    {
        var grid = BaseVisual(124, 92);
        grid.Children.Add(Ellipse(72, 16, 15, 38, Color.FromRgb(50, 135, 120)));
        grid.Children.Add(Ellipse(60, 18, 45, 10, Color.FromArgb(90, 180, 224, 235)));
        grid.Children.Add(Ellipse(60, 18, 45, 64, Color.FromArgb(90, 180, 224, 235)));
        grid.Children.Add(Ellipse(24, 26, 91, 33, Color.FromRgb(44, 102, 86)));
        return grid;
    }

    private static Grid CreateButterfly()
    {
        var grid = BaseVisual(112, 96);
        grid.Children.Add(Ellipse(55, 42, 19, 7, Color.FromArgb(225, 231, 101, 177)));
        grid.Children.Add(Ellipse(55, 42, 19, 47, Color.FromArgb(225, 128, 96, 222)));
        grid.Children.Add(Ellipse(14, 62, 62, 17, Color.FromRgb(69, 49, 54)));
        return grid;
    }

    private static Grid CreateMoth()
    {
        var grid = BaseVisual(106, 90);
        grid.Children.Add(Ellipse(50, 34, 23, 12, Color.FromArgb(235, 176, 154, 116)));
        grid.Children.Add(Ellipse(50, 34, 23, 44, Color.FromArgb(235, 151, 132, 102)));
        grid.Children.Add(Ellipse(18, 52, 61, 19, Color.FromRgb(83, 71, 57)));
        return grid;
    }

    private static Grid BaseVisual(double width, double height) =>
        new()
        {
            Width = width,
            Height = height,
            Background = Brushes.Transparent
        };

    private static Ellipse Ellipse(double width, double height, double left, double top, Color color)
    {
        var shape = new Ellipse
        {
            Width = width,
            Height = height,
            Fill = new SolidColorBrush(color),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(left, top, 0, 0),
            IsHitTestVisible = false
        };
        return shape;
    }

    private static Rectangle Rect(double width, double height, double left, double top, Color color)
    {
        return new Rectangle
        {
            Width = width,
            Height = height,
            Fill = new SolidColorBrush(color),
            RadiusX = 2,
            RadiusY = 2,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(left, top, 0, 0),
            IsHitTestVisible = false
        };
    }

    private static double RandomBetween(double min, double max) =>
        min + Random.Shared.NextDouble() * Math.Max(0.01d, max - min);

    private static double HeadingDegreesForVector(double vx, double vy)
    {
        if (Math.Abs(vx) < 0.001d && Math.Abs(vy) < 0.001d) return 0d;
        return NormalizeDegrees(Math.Atan2(vy, vx) * 180d / Math.PI);
    }

    private static double ShortestAngleDelta(double fromDegrees, double toDegrees)
    {
        var delta = NormalizeDegrees(toDegrees) - NormalizeDegrees(fromDegrees);
        if (delta > 180d) delta -= 360d;
        if (delta < -180d) delta += 360d;
        return delta;
    }

    private static double NormalizeDegrees(double degrees)
    {
        degrees %= 360d;
        return degrees < 0d ? degrees + 360d : degrees;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_closed) return;
        _closed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        try { Mouse.Capture(null); } catch { }
        try { _ipc.Dispose(); } catch { }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr newLong);
}
