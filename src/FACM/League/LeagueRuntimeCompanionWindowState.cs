using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FACM.Services;

namespace FACM.League
{
    /// <summary>
    /// Window-state adapter for the Runtime Companion.
    ///
    /// The presentation Form continues to own its pin/collapse behavior. This adapter observes that
    /// existing behavior, persists the result through the shared AppSettings owner, restores it only
    /// after WinForms has applied per-monitor DPI, and clamps saved coordinates when monitor topology
    /// changes. It intentionally does not own League data, Gameflow or LCU writes.
    /// </summary>
    internal sealed class LeagueRuntimeCompanionWindowState : IDisposable
    {
        private const int DesignMinimumExpandedHeight = 360;
        private const int DesignMaximumExpandedHeight = 420;
        private const int DesignVerticalMargin = 32;
        private const double WorkingAreaHeightRatio = 0.70;
        private const int DesignAnchorMargin = 18;
        private const int DesignLeftSafetyMargin = 12;

        private readonly Form _form;
        private readonly AppSettings _settings;
        private readonly Panel _header;
        private readonly Button _pinButton;
        private readonly Button _collapseButton;
        private readonly Control[] _dragSurfaces;
        private bool _restoring;
        private bool _disposed;

        private LeagueRuntimeCompanionWindowState(Form form, AppSettings settings)
        {
            _form = form ?? throw new ArgumentNullException(nameof(form));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _header = FindHeader(form);

            var buttons = _header == null
                ? new Button[0]
                : _header.Controls.OfType<Button>().OrderBy(button => button.Left).ToArray();
            if (buttons.Length >= 3)
            {
                // Runtime Companion header order is pin, collapse, close. Resolve from the right so
                // title/status width changes do not affect the contract.
                _pinButton = buttons[buttons.Length - 3];
                _collapseButton = buttons[buttons.Length - 2];
            }

            _dragSurfaces = _header == null
                ? new Control[0]
                : new[] { (Control)_header }
                    .Concat(_header.Controls.OfType<Label>().Cast<Control>())
                    .ToArray();

            _form.Shown += HandleShown;
            _form.FormClosed += HandleClosed;
            if (_pinButton != null) _pinButton.Click += HandlePinClicked;
            if (_collapseButton != null) _collapseButton.Click += HandleCollapseClicked;
            foreach (var surface in _dragSurfaces) surface.MouseUp += HandleDragMouseUp;
        }

        public static void Attach(Form form, AppSettings settings)
        {
            if (form == null || settings == null) return;
            // The event subscriptions intentionally keep this small adapter alive for exactly the
            // lifetime of the Form. HandleClosed removes every subscription.
            new LeagueRuntimeCompanionWindowState(form, settings);
        }

        private void HandleShown(object sender, EventArgs e)
        {
            if (_disposed || _form.IsDisposed) return;
            _restoring = true;
            try
            {
                var targetScreen = ResolveInitialScreen(_settings);

                // Form.HandleShown has already captured its DPI-scaled expanded dimensions. Shrink
                // only when the resulting physical height cannot fit the selected monitor.
                FitExpandedHeightToWorkingArea(targetScreen.WorkingArea);

                RestorePinnedState();
                RestoreCollapsedState();

                var desired = HasSavedPosition(_settings)
                    ? new Point(_settings.LeagueRuntimeCompanionX, _settings.LeagueRuntimeCompanionY)
                    : ResolveDefaultLocation(
                        targetScreen.WorkingArea,
                        _form.Size,
                        ResolveDpiScale(_form));
                _form.Location = ClampLocation(targetScreen.WorkingArea, _form.Size, desired);
            }
            finally
            {
                _restoring = false;
            }
        }

        private void RestorePinnedState()
        {
            var desired = _settings.LeagueRuntimeCompanionPinned;
            if (_pinButton != null && _form.TopMost != desired)
            {
                // PerformClick keeps the Form's own _pinned field, glyph color and tooltip aligned.
                _pinButton.PerformClick();
            }
            else
            {
                _form.TopMost = desired;
            }
        }

        private void RestoreCollapsedState()
        {
            var desired = _settings.LeagueRuntimeCompanionCollapsed;
            var current = IsCollapsed();
            if (desired != current && _collapseButton != null)
                _collapseButton.PerformClick();

            if (!IsCollapsed())
                FitExpandedHeightToWorkingArea(Screen.FromRectangle(_form.Bounds).WorkingArea);
        }

        private void HandlePinClicked(object sender, EventArgs e)
        {
            if (_restoring || _disposed) return;
            _settings.LeagueRuntimeCompanionPinned = _form.TopMost;
            _settings.Save();
        }

        private void HandleCollapseClicked(object sender, EventArgs e)
        {
            if (_disposed) return;
            if (!IsCollapsed())
                FitExpandedHeightToWorkingArea(Screen.FromRectangle(_form.Bounds).WorkingArea);
            if (_restoring) return;
            _settings.LeagueRuntimeCompanionCollapsed = IsCollapsed();
            _settings.Save();
        }

        private void HandleDragMouseUp(object sender, MouseEventArgs e)
        {
            if (_restoring || _disposed || e.Button != MouseButtons.Left || _form.IsDisposed) return;

            var area = Screen.FromRectangle(_form.Bounds).WorkingArea;
            if (!IsCollapsed()) FitExpandedHeightToWorkingArea(area);
            _form.Location = ClampLocation(area, _form.Size, _form.Location);
            _settings.LeagueRuntimeCompanionX = _form.Left;
            _settings.LeagueRuntimeCompanionY = _form.Top;
            _settings.Save();
        }

        private bool IsCollapsed()
        {
            if (_header == null || _header.IsDisposed) return false;
            return _form.ClientSize.Height <= _header.Height + Math.Max(4, ScalePixels(4, ResolveDpiScale(_form)));
        }

        private void FitExpandedHeightToWorkingArea(Rectangle workingArea)
        {
            if (_form.IsDisposed || IsCollapsed()) return;
            var target = ResolveExpandedHeight(workingArea.Height, ResolveDpiScale(_form));
            if (target <= 0 || _form.ClientSize.Height <= target) return;

            // The Form uses min/max equality only as a resize fence. It is borderless, so clearing
            // those fences here cannot expose a user resize affordance; it only lets us correct the
            // post-DPI physical height. On a later expand, this adapter re-applies the fit again.
            _form.MinimumSize = Size.Empty;
            _form.MaximumSize = Size.Empty;
            _form.ClientSize = new Size(_form.ClientSize.Width, target);
            _form.MinimumSize = _form.Size;
            _form.MaximumSize = _form.Size;
            _form.Location = ClampLocation(workingArea, _form.Size, _form.Location);
        }

        private void HandleClosed(object sender, FormClosedEventArgs e)
        {
            Dispose();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _form.Shown -= HandleShown;
            _form.FormClosed -= HandleClosed;
            if (_pinButton != null) _pinButton.Click -= HandlePinClicked;
            if (_collapseButton != null) _collapseButton.Click -= HandleCollapseClicked;
            foreach (var surface in _dragSurfaces) surface.MouseUp -= HandleDragMouseUp;
        }

        private static Panel FindHeader(Form form)
        {
            if (form == null) return null;
            return form.Controls.OfType<Panel>().FirstOrDefault(panel =>
                panel.Dock == DockStyle.Top && panel.Cursor == Cursors.SizeAll);
        }

        private static Screen ResolveInitialScreen(AppSettings settings)
        {
            if (HasSavedPosition(settings))
                return Screen.FromPoint(new Point(settings.LeagueRuntimeCompanionX, settings.LeagueRuntimeCompanionY));
            return Screen.FromPoint(Cursor.Position);
        }

        internal static bool HasSavedPosition(AppSettings settings)
        {
            return settings != null &&
                   settings.LeagueRuntimeCompanionX != int.MinValue &&
                   settings.LeagueRuntimeCompanionY != int.MinValue;
        }

        internal static float ResolveDpiScale(Control control)
        {
            if (control == null) return 1F;
            return Math.Max(0.5F, control.DeviceDpi / 96F);
        }

        internal static int ResolveExpandedHeight(int workingAreaHeight, float dpiScale)
        {
            var scale = Math.Max(0.5F, dpiScale);
            var minimum = ScalePixels(DesignMinimumExpandedHeight, scale);
            var maximum = ScalePixels(DesignMaximumExpandedHeight, scale);
            var margin = ScalePixels(DesignVerticalMargin, scale);
            var usable = Math.Max(1, workingAreaHeight - margin);
            var compactCap = Math.Max(1, (int)Math.Round(workingAreaHeight * WorkingAreaHeightRatio, MidpointRounding.AwayFromZero));
            var target = Math.Min(usable, compactCap);
            if (target < minimum) return target;
            return Math.Min(maximum, target);
        }

        internal static Point ResolveDefaultLocation(Rectangle workingArea, Size windowSize, float dpiScale)
        {
            var scale = Math.Max(0.5F, dpiScale);
            var right = ScalePixels(DesignAnchorMargin, scale);
            var top = ScalePixels(DesignAnchorMargin, scale);
            var leftSafety = ScalePixels(DesignLeftSafetyMargin, scale);
            var x = Math.Max(workingArea.Left + leftSafety, workingArea.Right - windowSize.Width - right);
            var y = workingArea.Top + top;
            return ClampLocation(workingArea, windowSize, new Point(x, y));
        }

        internal static Point ClampLocation(Rectangle workingArea, Size windowSize, Point desired)
        {
            var maxLeft = Math.Max(workingArea.Left, workingArea.Right - Math.Max(1, windowSize.Width));
            var maxTop = Math.Max(workingArea.Top, workingArea.Bottom - Math.Max(1, windowSize.Height));
            return new Point(
                Math.Max(workingArea.Left, Math.Min(desired.X, maxLeft)),
                Math.Max(workingArea.Top, Math.Min(desired.Y, maxTop)));
        }

        private static int ScalePixels(int logical, float scale)
        {
            return Math.Max(1, (int)Math.Round(logical * scale, MidpointRounding.AwayFromZero));
        }

        internal static void ValidateForSmokeTest()
        {
            if (ResolveExpandedHeight(728, 1F) != 420)
                throw new InvalidOperationException("Runtime Companion 100% DPI compact height policy drifted.");
            if (ResolveExpandedHeight(728, 1.25F) != 510)
                throw new InvalidOperationException("Runtime Companion 125% DPI compact-display height policy drifted.");
            if (ResolveExpandedHeight(1040, 1.5F) != 630)
                throw new InvalidOperationException("Runtime Companion 150% DPI compact height policy drifted.");
            if (ResolveExpandedHeight(1040, 2F) != 728)
                throw new InvalidOperationException("Runtime Companion 200% DPI working-area guard drifted.");
            if (ResolveExpandedHeight(2160, 2F) != 840)
                throw new InvalidOperationException("Runtime Companion 200% DPI compact maximum height cap drifted.");
            if (ResolveExpandedHeight(2160, 1F) != DesignMaximumExpandedHeight)
                throw new InvalidOperationException("Runtime Companion compact maximum expanded height drifted.");

            var leftMonitor = new Rectangle(-1920, 0, 1920, 1080);
            var size = new Size(480, 630);
            var clamped = ClampLocation(leftMonitor, size, new Point(-4000, 2000));
            if (clamped.X != leftMonitor.Left || clamped.Y != leftMonitor.Bottom - size.Height)
                throw new InvalidOperationException("Runtime Companion negative-coordinate monitor clamp failed.");

            var anchored = ResolveDefaultLocation(leftMonitor, new Size(480, 630), 1.5F);
            if (anchored.X < leftMonitor.Left || anchored.Y < leftMonitor.Top ||
                anchored.X + 480 > leftMonitor.Right || anchored.Y + 630 > leftMonitor.Bottom)
                throw new InvalidOperationException("Runtime Companion DPI-aware default anchor left the working area.");

            var compact125 = new Rectangle(0, 0, 1366, 728);
            var anchored125 = ResolveDefaultLocation(compact125, new Size(400, 510), 1.25F);
            if (anchored125.X < compact125.Left || anchored125.Y < compact125.Top ||
                anchored125.X + 400 > compact125.Right || anchored125.Y + 510 > compact125.Bottom)
                throw new InvalidOperationException("Runtime Companion 125% DPI compact-display anchor left the working area.");

            var fullHd200 = new Rectangle(0, 0, 1920, 1040);
            var anchored200 = ResolveDefaultLocation(fullHd200, new Size(640, 728), 2F);
            if (anchored200.X < fullHd200.Left || anchored200.Y < fullHd200.Top ||
                anchored200.X + 640 > fullHd200.Right || anchored200.Y + 728 > fullHd200.Bottom)
                throw new InvalidOperationException("Runtime Companion 200% DPI anchor left the working area.");

            var settings = new AppSettings();
            if (HasSavedPosition(settings))
                throw new InvalidOperationException("Runtime Companion default position sentinel drifted.");
            settings.LeagueRuntimeCompanionX = -800;
            settings.LeagueRuntimeCompanionY = 120;
            if (!HasSavedPosition(settings))
                throw new InvalidOperationException("Runtime Companion saved position detection failed.");

            AppSettings.ValidateRuntimeCompanionPreferencesForSmokeTest();
        }
    }
}
