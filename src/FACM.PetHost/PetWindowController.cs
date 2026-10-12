using System.Windows.Interop;
using System.Windows.Media;
using VPet_Simulator.Core;
using FormsScreen = System.Windows.Forms.Screen;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;

namespace FACM.PetHost;

internal sealed class PetWindowController : IController
{
    private readonly PetHostWindow _window;

    public PetWindowController(PetHostWindow window)
    {
        _window = window;
    }

    public double ZoomRatio
    {
        get
        {
            var width = _window.ActualWidth > 1 ? _window.ActualWidth : _window.Width;
            return Math.Max(0.2, width / 500d);
        }
    }

    public int PressLength => 450;
    public bool EnableFunction => false;
    public int InteractionCycle => 10;

    // VPet-Simulator.Core 1.1.0.66 exposes the historical misspelling RePostionActive.
    // Keep the corrected alias too so this host remains source-compatible with newer VPet Core builds.
    public bool RePostionActive { get; set; } = true;
    public bool RePositionActive
    {
        get { return RePostionActive; }
        set { RePostionActive = value; }
    }

    public bool AutoChangeWindow => false;

    public void MoveWindows(double x, double y)
    {
        _window.Dispatcher.Invoke(() =>
        {
            _window.Left += x * ZoomRatio;
            _window.Top += y * ZoomRatio;
        });
    }

    public double GetWindowsDistanceLeft()
    {
        return _window.Dispatcher.Invoke(() => _window.Left - GetWorkingAreaLogical(false).Left);
    }

    public double GetWindowsDistanceRight()
    {
        return _window.Dispatcher.Invoke(() => GetWorkingAreaLogical(false).Right - _window.Left - _window.ActualWidth);
    }

    public double GetWindowsDistanceUp()
    {
        return _window.Dispatcher.Invoke(() => _window.Top - GetWorkingAreaLogical(false).Top);
    }

    public double GetWindowsDistanceDown()
    {
        return _window.Dispatcher.Invoke(() => GetWorkingAreaLogical(false).Bottom - _window.Top - _window.ActualHeight);
    }

    public bool IfInActivateScreen() => true;

    public void SetNowScreenActivate()
    {
        // FACM intentionally follows whichever monitor currently contains the pet.
    }

    public void ShowPanel()
    {
        _window.NotifyOpenFacm();
    }

    public void ResetPosition()
    {
        _window.Dispatcher.Invoke(() => ClampInto(GetWorkingAreaLogical(false)));
    }

    public bool CheckPosition()
    {
        return _window.Dispatcher.Invoke(() =>
        {
            var area = GetWorkingAreaLogical(false);
            var width = Math.Max(1, _window.ActualWidth);
            var height = Math.Max(1, _window.ActualHeight);
            return _window.Left < area.Left - width * 0.25 ||
                   _window.Left + width > area.Right + width * 0.25 ||
                   _window.Top < area.Top - height * 0.25 ||
                   _window.Top + height > area.Bottom + height * 0.25;
        });
    }

    public void ResetToPrimaryScreen()
    {
        _window.Dispatcher.Invoke(() =>
        {
            var area = GetWorkingAreaLogical(true);
            var width = Math.Max(1, _window.ActualWidth > 1 ? _window.ActualWidth : _window.Width);
            var height = Math.Max(1, _window.ActualHeight > 1 ? _window.ActualHeight : _window.Height);
            _window.Left = area.Left + (area.Width - width) / 2d;
            _window.Top = area.Bottom - height - 24d;
            ClampInto(area);
        });
    }

    private void ClampInto(WpfRect area)
    {
        var width = Math.Max(1, _window.ActualWidth > 1 ? _window.ActualWidth : _window.Width);
        var height = Math.Max(1, _window.ActualHeight > 1 ? _window.ActualHeight : _window.Height);
        var minLeft = area.Left - width * 0.20;
        var maxLeft = area.Right - width * 0.80;
        var minTop = area.Top - height * 0.15;
        var maxTop = area.Bottom - height * 0.80;
        _window.Left = Math.Max(minLeft, Math.Min(_window.Left, maxLeft));
        _window.Top = Math.Max(minTop, Math.Min(_window.Top, maxTop));
    }

    private WpfRect GetWorkingAreaLogical(bool primary)
    {
        FormsScreen? screen;
        if (primary)
        {
            screen = FormsScreen.PrimaryScreen;
        }
        else
        {
            var handle = new WindowInteropHelper(_window).Handle;
            screen = handle == IntPtr.Zero ? FormsScreen.PrimaryScreen : FormsScreen.FromHandle(handle);
        }

        var bounds = (screen ?? FormsScreen.PrimaryScreen)?.WorkingArea ?? new System.Drawing.Rectangle(
            0,
            0,
            (int)SystemParameters.PrimaryScreenWidth,
            (int)SystemParameters.PrimaryScreenHeight);

        var source = PresentationSource.FromVisual(_window);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = transform.Transform(new WpfPoint(bounds.Left, bounds.Top));
        var bottomRight = transform.Transform(new WpfPoint(bounds.Right, bounds.Bottom));
        return new WpfRect(topLeft, bottomRight);
    }
}
