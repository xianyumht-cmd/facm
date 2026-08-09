using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace FACM.MachineCatPrototype;

internal sealed class MachineCatWindow : Window
{
    private const int WmNcHitTest = 0x0084;
    private const int HtTransparent = -1;
    private const double DragThresholdDip = 6d;

    private static readonly PetState[] StateOrder =
    {
        PetState.Idle,
        PetState.Walk,
        PetState.Run,
        PetState.Turn,
        PetState.Observe,
        PetState.Raised,
        PetState.Recover,
        PetState.Sleep
    };

    private readonly MachineCatAnimator _animator = new();
    private readonly MachineCatRig _rig = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly TextBlock _debugText;

    private double _lastRenderSeconds;
    private double _stateEnteredSeconds;
    private bool _debugVisible;
    private bool _mouseDown;
    private bool _dragging;
    private Point _dragStartCursorDip;
    private Point _dragStartWindowDip;
    private HwndSource? _hwndSource;

    public MachineCatWindow(PetState initialState)
    {
        Title = "FACM Machine Cat Gate 1";
        Width = 240d;
        Height = 240d;
        MinWidth = MaxWidth = Width;
        MinHeight = MaxHeight = Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;

        var host = new Grid
        {
            Width = 240d,
            Height = 240d,
            Background = Brushes.Transparent
        };
        host.Children.Add(_rig.Visual);

        _debugText = new TextBlock
        {
            Margin = new Thickness(8d),
            Padding = new Thickness(7d, 4d, 7d, 4d),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(176, 12, 18, 28)),
            FontFamily = new FontFamily("Microsoft YaHei UI"),
            FontSize = 11d,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        host.Children.Add(_debugText);
        Content = host;

        Loaded += OnLoaded;
        Closed += OnClosed;
        SourceInitialized += OnSourceInitialized;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseLeftButtonDown += OnMouseLeftButtonDown;
        PreviewMouseMove += OnMouseMove;
        PreviewMouseLeftButtonUp += OnMouseLeftButtonUp;

        SetState(initialState);
        _rig.Apply(_animator.CurrentPose);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 32d;
        Top = workArea.Bottom - Height - 32d;
        _lastRenderSeconds = _clock.Elapsed.TotalSeconds;
        CompositionTarget.Rendering += OnRendering;
        Focus();
        UpdateDebugText();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
        if (_hwndSource is not null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(WndProc);
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed.TotalSeconds;
        var rawDelta = now - _lastRenderSeconds;

        if (_animator.State == PetState.Sleep && rawDelta < (1d / 30d))
            return;

        _lastRenderSeconds = now;
        var delta = MachineCatAnimator.ClampDelta(rawDelta);

        if (_animator.State == PetState.Recover && now - _stateEnteredSeconds > 1.45d)
            SetState(PetState.Idle);

        var mouseDirection = ReadMouseDirection();
        var pose = _animator.Update(delta, mouseDirection);
        _rig.Apply(pose);

        if (_debugVisible)
            UpdateDebugText();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var target = e.Key switch
        {
            Key.D1 or Key.NumPad1 => PetState.Idle,
            Key.D2 or Key.NumPad2 => PetState.Walk,
            Key.D3 or Key.NumPad3 => PetState.Run,
            Key.D4 or Key.NumPad4 => PetState.Turn,
            Key.D5 or Key.NumPad5 => PetState.Observe,
            Key.D6 or Key.NumPad6 => PetState.Raised,
            Key.D7 or Key.NumPad7 => PetState.Recover,
            Key.D8 or Key.NumPad8 => PetState.Sleep,
            _ => (PetState?)null
        };

        if (target.HasValue)
        {
            SetState(target.Value);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space)
        {
            var index = Array.IndexOf(StateOrder, _animator.State);
            SetState(StateOrder[(index + 1 + StateOrder.Length) % StateOrder.Length]);
            e.Handled = true;
        }
        else if (e.Key is Key.D or Key.F1)
        {
            _debugVisible = !_debugVisible;
            _debugText.Visibility = _debugVisible ? Visibility.Visible : Visibility.Collapsed;
            UpdateDebugText();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_rig.ApproximateInteractiveBounds.Contains(e.GetPosition(this))) return;

        _mouseDown = true;
        _dragging = false;
        _dragStartCursorDip = GetCursorScreenDip();
        _dragStartWindowDip = new Point(Left, Top);
        CaptureMouse();
        Focus();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_mouseDown || e.LeftButton != MouseButtonState.Pressed) return;

        var cursor = GetCursorScreenDip();
        var delta = cursor - _dragStartCursorDip;
        if (!_dragging && Math.Sqrt((delta.X * delta.X) + (delta.Y * delta.Y)) >= DragThresholdDip)
        {
            _dragging = true;
            SetState(PetState.Raised);
        }

        if (!_dragging) return;

        Left = _dragStartWindowDip.X + delta.X;
        Top = _dragStartWindowDip.Y + delta.Y;
        e.Handled = true;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_mouseDown) return;

        _mouseDown = false;
        ReleaseMouseCapture();

        if (_dragging)
        {
            _dragging = false;
            KeepMostlyOnPrimaryWorkArea();
            SetState(PetState.Recover);
        }
        else
        {
            SetState(PetState.Observe);
        }

        e.Handled = true;
    }

    private void SetState(PetState state)
    {
        _animator.SetState(state);
        _stateEnteredSeconds = _clock.Elapsed.TotalSeconds;
        UpdateDebugText();
    }

    private Vector2 ReadMouseDirection()
    {
        if (!GetCursorPos(out var screenPoint)) return Vector2.Zero;

        try
        {
            var local = PointFromScreen(new Point(screenPoint.X, screenPoint.Y));
            var x = Math.Clamp((local.X - (Width / 2d)) / (Width * 0.42d), -1d, 1d);
            var y = Math.Clamp((local.Y - (Height / 2d)) / (Height * 0.42d), -1d, 1d);
            return new Vector2((float)x, (float)y);
        }
        catch
        {
            return Vector2.Zero;
        }
    }

    private Point GetCursorScreenDip()
    {
        if (!GetCursorPos(out var point)) return new Point(0d, 0d);
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return transform.Transform(new Point(point.X, point.Y));
    }

    private void KeepMostlyOnPrimaryWorkArea()
    {
        var area = SystemParameters.WorkArea;
        const double visibleMargin = 52d;
        Left = Math.Clamp(Left, area.Left - Width + visibleMargin, area.Right - visibleMargin);
        Top = Math.Clamp(Top, area.Top - Height + visibleMargin, area.Bottom - visibleMargin);
    }

    private void UpdateDebugText()
    {
        if (_debugText is null) return;
        _debugText.Text = $"Gate 1 · {_animator.State}\n1-8 状态  Space 下一项  D/F1 调试  Esc 退出";
    }

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmNcHitTest || _debugVisible || _mouseDown)
            return IntPtr.Zero;

        var x = unchecked((short)(long)lParam);
        var y = unchecked((short)((long)lParam >> 16));
        try
        {
            var local = PointFromScreen(new Point(x, y));
            if (!_rig.ApproximateInteractiveBounds.Contains(local))
            {
                handled = true;
                return new IntPtr(HtTransparent);
            }
        }
        catch
        {
            // Fall through to normal hit testing if coordinate conversion fails.
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
